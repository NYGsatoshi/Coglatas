using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Realtime;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class AqRealtimeHostedPostgreSqlTests
{
    private static readonly JsonSerializerOptions WebJson = JsonSerializerOptions.Web;

    [PostgreSqlFact]
    public Task CurrentFeedbackReaderReceivesInvalidationAndResultStopSuppressesQueuedDelivery() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(),
            async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                const string tenantSlug = SecurityCiFixtureSeed.TenantASlug;
                using var moderator = await app.LoginAsync("owner", tenantSlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var unrelated = await app.LoginAsync("member", tenantSlug, SecurityCiFixtureSeed.TenantAMemberEmail);
                using var guest = await app.CreateAnonymousClientAsync(tenantSlug);
                using (var denied = await guest.PostAsync("/hubs/app/negotiate?negotiateVersion=1", null))
                    Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

                var (tenantId, settingsId, presentationId, answerId) = await SeedAsync(database);
                await using var recipientSocket = await app.ConnectAsync(moderator, "owner", tenantSlug);
                await using var unrelatedSocket = await app.ConnectAsync(unrelated, "member", tenantSlug);
                Assert.True(await recipientSocket.SubscribeAsync("SubscribeUser"));
                Assert.True(await unrelatedSocket.SubscribeAsync("SubscribeUser"));
                using (var current = await moderator.GetAsync($"/api/v1/core/presentations/{presentationId:D}/feedback"))
                    Assert.Equal(HttpStatusCode.OK, current.StatusCode);

                using var hide = await moderator.PatchAsJsonAsync($"/api/v1/core/feedback/answers/{answerId:D}/visibility",
                    new { expectedVersionNo = 1L, action = "hide", reason = "Synthetic visibility decision" });
                Assert.Equal(HttpStatusCode.OK, hide.StatusCode);

                OutboxEvent committed;
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    committed = await db.OutboxEvents.AsNoTracking().SingleAsync(row =>
                        row.TenantId == tenantId && row.AggregateId == presentationId &&
                        row.EventType == SurveyFeedbackInvalidation.EventName);
                }
                var envelope = JsonSerializer.Deserialize<DurableEventEnvelope>(committed.PayloadJson, WebJson);
                Assert.NotNull(envelope);
                Assert.Equal(RealtimeActor.System(), envelope.Actor);
                Assert.Empty(envelope.Payload.EnumerateObject());
                var routes = JsonSerializer.Deserialize<RealtimeRoutingTarget[]>(committed.RoutingJson, WebJson);
                Assert.NotNull(routes);
                Assert.Equal(new RealtimeRoutingTarget(RealtimeSubscriptionType.User, SecurityCiFixtureSeed.TenantAOwnerUserId),
                    Assert.Single(routes));

                await recipientSocket.WaitEventAsync(committed.Id);
                await app.WaitDeliveredAsync(committed.Id);
                Assert.True(recipientSocket.ReceivedAggregateIdentity(committed.Id, SurveyFeedbackInvalidation.EventName,
                    tenantId, "Presentation", presentationId));
                Assert.True(await unrelatedSocket.SubscribeAsync("SubscribeUser"));
                Assert.False(unrelatedSocket.Received(committed.Id));

                var delayedId = await StageThenStopAsync(database, committed, envelope, settingsId);
                await app.WaitDeliveredAsync(delayedId);
                using (var stopped = await moderator.GetAsync($"/api/v1/core/presentations/{presentationId:D}/feedback"))
                    Assert.Equal(HttpStatusCode.NotFound, stopped.StatusCode);
                Assert.True(await recipientSocket.SubscribeAsync("SubscribeUser"));
                Assert.False(recipientSocket.Received(delayedId));
                Assert.False(unrelatedSocket.Received(delayedId));
            });

    private static async Task<(Guid TenantId, Guid SettingsId, Guid PresentationId, Guid AnswerId)> SeedAsync(string database)
    {
        var actorId = SecurityCiFixtureSeed.TenantAOwnerUserId;
        var now = DateTimeOffset.UtcNow;
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(row => row.Slug == SecurityCiFixtureSeed.TenantASlug);
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(row => row.TenantId == tenant.Id && row.Slug.StartsWith("sec02-"));
        var project = await db.Projects.AsNoTracking().SingleAsync(row =>
            row.TenantId == tenant.Id && row.WorkspaceId == workspace.Id && row.Slug.StartsWith("sec02-"));
        var membership = await db.WorkspaceMembers.SingleAsync(row =>
            row.TenantId == tenant.Id && row.WorkspaceId == workspace.Id && row.UserId == actorId);
        var activity = new ActivityEvent
        {
            TenantId = tenant.Id, ProjectId = project.Id, CreatedByUserId = actorId,
            Title = "Synthetic realtime event", Status = EventStatus.Published,
            StartsAt = now.AddHours(-1), EndsAt = now.AddHours(1)
        };
        var settings = EventFeedbackSettings.Create(activity, activity.StartsAt, activity.EndsAt, "UTC");
        var presentation = Presentation.Create(activity, "Synthetic realtime presentation", null, activity.StartsAt, activity.EndsAt);
        var definition = FormDefinition.Create(tenant.Id, actorId, null, null, project.Id, FormPurpose.Survey, "Synthetic realtime survey");
        var campaign = SurveyCampaign.Create(activity, project, definition);
        campaign.SetPublication(campaign.VersionNo, true, true);
        var target = SurveyTarget.Create(campaign, presentation, Guid.NewGuid().ToString("N"));
        target.Configure(target.VersionNo, settings, null, null, true, false);
        var common = FormVersion.Publish(definition, 1, [new SurveyField("note", "Synthetic question", SurveyFieldType.Text, true, [])], now);
        var additional = TargetQuestionSetVersion.Publish(target, 1, [], now);
        var schema = PublishedSurveySchema.Compose(target, common, additional, "synthetic-realtime-version", now);
        db.AddRange(activity, settings, presentation, definition, campaign, target, common, additional,
            PresentationParticipant.Assign(presentation, project, membership, PresentationParticipantRole.Reviewer));
        db.Add(new CapabilityGrant
        {
            TenantId = tenant.Id, SubjectUserId = actorId, GrantedByUserId = actorId,
            ScopeType = CapabilityScopeType.Workspace, ScopeId = workspace.Id, CapabilityKey = "feedback.moderate",
            GrantedAt = now.AddMinutes(-1), ExpiresAt = now.AddHours(1)
        });
        await db.SaveChangesAsync();
        db.Add(schema);
        await db.SaveChangesAsync();
        target.Publish(target.VersionNo, schema);
        var answers = new Dictionary<string, JsonElement>
        {
            ["note"] = JsonSerializer.SerializeToElement("Synthetic body excluded from realtime")
        };
        var submission = FormSubmission.SubmitSurvey(definition, target, schema, answers, now);
        db.Add(submission);
        await db.SaveChangesAsync();
        return (tenant.Id, settings.Id, presentation.Id, Assert.Single(submission.Values).Id);
    }

    private static async Task<Guid> StageThenStopAsync(
        string database, OutboxEvent committed, DurableEventEnvelope envelope, Guid settingsId)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var now = DateTimeOffset.UtcNow;
        var delayed = new OutboxEvent(Guid.NewGuid())
        {
            TenantId = committed.TenantId,
            EventType = committed.EventType,
            PayloadSchemaVersion = committed.PayloadSchemaVersion,
            AggregateType = committed.AggregateType,
            AggregateId = committed.AggregateId,
            OccurredAt = now,
            RoutingJson = committed.RoutingJson,
            Status = OutboxEventStatus.Pending,
            NextAttemptAt = now.AddHours(1)
        };
        delayed.PayloadJson = JsonSerializer.Serialize(envelope with { EventId = delayed.Id, OccurredAt = now }, WebJson);
        db.Add(delayed);
        await db.SaveChangesAsync();

        var settings = await db.Set<EventFeedbackSettings>().SingleAsync(row => row.Id == settingsId);
        settings.SetResultsStop(settings.VersionNo, true);
        delayed.NextAttemptAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return delayed.Id;
    }
}
