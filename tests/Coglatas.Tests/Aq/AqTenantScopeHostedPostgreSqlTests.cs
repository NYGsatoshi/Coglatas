using System.Net;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class AqTenantScopeHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task SeparateTenantRolesReadTheirOwnPresentationAndNativeScopeRelationsStayConsistent() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var fields = await SeedScopesAsync(database, host, project, activity);
            await host.LoginAsync();
            using var foreign = AqPostgreSqlScenario.CreateGuestClient(host);
            foreign.DefaultRequestHeaders.Add("X-Tenant-Slug", fields.ForeignTenantSlug);
            using (var signedIn = await AdmissionHost.PostAsync(foreign, "/api/auth/login", new
                   { email = host.ForeignRecipient.Email, password = AdmissionHost.Password }))
                Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

            var ownRoute = FeedbackPath(fields.LocalPresentationId);
            var otherRoute = FeedbackPath(fields.ForeignPresentationId);
            using (var own = await host.Client.GetAsync(ownRoute))
                Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            using (var foreignOwn = await foreign.GetAsync(otherRoute))
                Assert.Equal(HttpStatusCode.OK, foreignOwn.StatusCode);
            using (var other = await host.Client.GetAsync(otherRoute))
                Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
            using (var foreignOther = await foreign.GetAsync(ownRoute))
                Assert.Equal(HttpStatusCode.NotFound, foreignOther.StatusCode);

            var impossibleParticipant = Guid.NewGuid();
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                    INSERT INTO presentation_participants ("Id", "TenantId", "PresentationId", "UserId", "Role", "CreatedAt")
                    VALUES (@id, @tenant, @presentation, @member, 'Presenter', CURRENT_TIMESTAMP)
                    """, ("id", impossibleParticipant), ("tenant", fields.ForeignTenantId),
                    ("presentation", fields.LocalPresentationId), ("member", host.ForeignRecipient.Id)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.False(await db.Set<PresentationParticipant>().AsNoTracking().AnyAsync(row => row.Id == impossibleParticipant));
            Assert.Equal(1, await db.Set<PresentationParticipant>().AsNoTracking()
                .CountAsync(row => row.PresentationId == fields.LocalPresentationId));
        });

    private static string FeedbackPath(Guid presentationId) => $"/api/v1/core/presentations/{presentationId:D}/feedback";

    private static async Task<Fixture> SeedScopesAsync(string database, AdmissionHost host, Project localProject, ActivityEvent localEvent)
    {
        using var scope = host.Services.CreateScope();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var foreignMembership = await db.TenantUsers.AsNoTracking().SingleAsync(row => row.UserId == host.ForeignRecipient.Id);
        var foreignTenant = await db.Tenants.AsNoTracking().SingleAsync(row => row.Id == foreignMembership.TenantId);
        var foreignUser = await db.Users.SingleAsync(row => row.Id == host.ForeignRecipient.Id);
        foreignUser.PasswordHash = passwords.HashPassword(AdmissionHost.Password);
        var foreignWorkspace = new Workspace
        {
            TenantId = foreignTenant.Id, Name = "Synthetic workspace", Slug = "synthetic-foreign-workspace",
            CreatedByUserId = foreignUser.Id
        };
        var foreignWorkspaceMembership = new WorkspaceMember
        {
            TenantId = foreignTenant.Id, WorkspaceId = foreignWorkspace.Id, UserId = foreignUser.Id,
            Role = WorkspaceRole.Member, Status = MembershipStatus.Active
        };
        var foreignProject = new Project
        {
            TenantId = foreignTenant.Id, WorkspaceId = foreignWorkspace.Id, OwnerUserId = foreignUser.Id,
            CreatedByUserId = foreignUser.Id, Name = "Synthetic feedback project", Slug = "synthetic-feedback-project",
            Status = ProjectStatus.Active
        };
        var foreignEvent = new ActivityEvent
        {
            TenantId = foreignTenant.Id, ProjectId = foreignProject.Id, CreatedByUserId = foreignUser.Id,
            Title = localEvent.Title, Status = EventStatus.Published, StartsAt = localEvent.StartsAt, EndsAt = localEvent.EndsAt
        };
        var localWorkspaceMembership = await db.WorkspaceMembers.AsNoTracking().SingleAsync(row =>
            row.TenantId == localProject.TenantId && row.WorkspaceId == localProject.WorkspaceId && row.UserId == host.Actor.Id);
        var localPresentation = Presentation.Create(localEvent, "Synthetic presentation", null, localEvent.StartsAt, localEvent.EndsAt);
        var foreignPresentation = Presentation.Create(foreignEvent, "Synthetic presentation", null, foreignEvent.StartsAt, foreignEvent.EndsAt);
        db.AddRange(foreignWorkspace, foreignWorkspaceMembership, foreignProject, foreignEvent, localPresentation, foreignPresentation,
            PresentationParticipant.Assign(localPresentation, localProject, localWorkspaceMembership, PresentationParticipantRole.Presenter),
            PresentationParticipant.Assign(foreignPresentation, foreignProject, foreignWorkspaceMembership, PresentationParticipantRole.Presenter));
        db.Add(new ProjectMember
        {
            TenantId = foreignTenant.Id, ProjectId = foreignProject.Id, UserId = foreignUser.Id,
            Role = ProjectRole.Owner, JoinedAt = localEvent.StartsAt
        });
        await db.SaveChangesAsync();
        return new Fixture(localPresentation.Id, foreignPresentation.Id, foreignTenant.Id, foreignTenant.Slug);
    }

    private sealed record Fixture(Guid LocalPresentationId, Guid ForeignPresentationId, Guid ForeignTenantId, string ForeignTenantSlug);
}
