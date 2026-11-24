using System.Data;
using System.Data.Common;
using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Persistence.SurveyFeedback;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class AqSummarySnapshotPostgreSqlTests
{
    [PostgreSqlFact]
    public Task GuestCommitBetweenCountAndCellsDoesNotMixSummarySnapshots() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var publishedAt = activity.StartsAt.AddMinutes(1);
            var form = FormDefinition.Create(activity.TenantId, host.Actor.Id, null, null, project.Id,
                FormPurpose.Survey, "Synthetic summary form");
            var eventSettings = EventFeedbackSettings.Create(activity, activity.StartsAt, activity.EndsAt, "UTC");
            var presentation = Presentation.Create(activity, "Synthetic presentation", null, activity.StartsAt, activity.EndsAt);
            var campaign = SurveyCampaign.Create(activity, project, form);
            campaign.SetPublication(campaign.VersionNo, true, true);
            var target = SurveyTarget.Create(campaign, presentation, "synthetic-summary-marker");
            target.Configure(target.VersionNo, eventSettings, null, null, true, false);
            var common = FormVersion.Publish(form, 1,
                [new SurveyField("rating", "Synthetic rating", SurveyFieldType.Number, true, [])], publishedAt);
            var extra = TargetQuestionSetVersion.Publish(target, 1, [], publishedAt);
            var schema = PublishedSurveySchema.Compose(target, common, extra, "synthetic-summary-version", publishedAt);

            await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                seed.AddRange(form, eventSettings, presentation, campaign, target, common, extra);
                await seed.SaveChangesAsync();
                seed.Add(schema);
                await seed.SaveChangesAsync();
                target.Publish(target.VersionNo, schema);
                seed.Add(NewSubmission(1));
                await seed.SaveChangesAsync();
            }

            var gate = new InsertAfterSummaryCount(async cancellationToken =>
            {
                await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                writer.Add(NewSubmission(4));
                await writer.SaveChangesAsync(cancellationToken);
            });

            var tenant = new CurrentTenantService();
            tenant.SetTenant(host.Tenant.Id, host.Tenant.Slug);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).AddInterceptors(gate).Options;
            await using var reader = new AppDbContext(options, tenant);
            var repository = new EfSurveyFeedbackRepository(reader);
            var first = await repository.ReadSummaryAsync(host.Tenant.Id, presentation.Id, null, CancellationToken.None);

            Assert.True(gate.Inserted);
            Assert.Equal(IsolationLevel.RepeatableRead, gate.ObservedIsolation);
            Assert.Equal(1L, first.Count);
            Assert.Equal("1", Assert.Single(first.Values).ValueJson);

            var next = await repository.ReadSummaryAsync(host.Tenant.Id, presentation.Id, null, CancellationToken.None);
            Assert.Equal(2L, next.Count);
            Assert.Equal(new[] { "1", "4" }, next.Values.Select(value => value.ValueJson).Order());

            FormSubmission NewSubmission(int value)
            {
                var answer = JsonSerializer.SerializeToElement(value);
                return FormSubmission.SubmitSurvey(form, target, schema,
                    new Dictionary<string, JsonElement> { ["rating"] = answer }, DateTimeOffset.UtcNow);
            }
        });

    private sealed class InsertAfterSummaryCount(Func<CancellationToken, Task> insert) : DbCommandInterceptor
    {
        private int _inserted;
        public bool Inserted => Volatile.Read(ref _inserted) != 0;
        public IsolationLevel? ObservedIsolation { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("form_submissions", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _inserted, 1) == 0)
            {
                ObservedIsolation = command.Transaction?.IsolationLevel;
                await insert(cancellationToken);
            }
            return result;
        }
    }
}
