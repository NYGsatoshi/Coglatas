using System.Text.Json;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class ProjectTestRuntimePostgreSqlTests
{
    [PostgreSqlFact]
    public Task ResultBytesAndTerminalStateRollBackTogetherOnLateRevocationCancellationAndFailure() =>
        WithFixtureAsync(async fixture =>
        {
            fixture.Policy.Allowed.UnionWith([ProjectTestRuntime.StartOperation, ProjectTestRuntime.ReadOperation,
                ProjectTestRuntime.ExportOperation]);
            foreach (var failure in new[] { "revocation", "cancellation", "exception" })
            {
                await using var context = fixture.Open();
                var runtime = fixture.Runtime(context);
                var request = fixture.Request with { JobId = Guid.NewGuid() };
                var key = new ProjectTestJobKey(request.Context.Branch, request.JobId);
                Assert.NotNull(await runtime.StartAsync(request));
                using var cancellation = new CancellationTokenSource();
                var observedStagedCompletion = false;
                fixture.BeforeResultCommit = async token =>
                {
                    // Another connection must still see the original pending row while completion is staged.
                    Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(fixture.Database,
                        "SELECT state = 0 AND result IS NULL AND result_body IS NULL FROM projectide_development.project_ide_test_jobs WHERE job_id = @job",
                        ("job", key.JobId)));
                    observedStagedCompletion = true;
                    if (failure == "revocation")
                    {
                        await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(fixture.Database);
                        await writer.Sessions.Where(session => session.Id == fixture.Viewer.SessionId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, Now), token);
                    }
                    if (failure == "cancellation") cancellation.Cancel();
                    if (failure == "exception") throw new InvalidOperationException("Synthetic final-boundary failure.");
                };

                if (failure == "revocation")
                {
                    Assert.Null(await runtime.ExecuteAsync(key));
                    Assert.Null(await runtime.FindAsync(key));
                    Assert.Null(await runtime.ExportAsync(key));
                }
                else if (failure == "cancellation")
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.ExecuteAsync(key, cancellation.Token));
                else
                    await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecuteAsync(key));

                Assert.True(observedStagedCompletion);
                fixture.BeforeResultCommit = null;
                var pending = Assert.IsType<ProjectTestJob>(await new ProjectTestJobStore(context).FindAsync(key, default));
                Assert.Equal(ProjectTestJobState.Pending, pending.State);
                Assert.Null(pending.Result);
                Assert.Equal(ProjectTestRetainedResultState.Unavailable, pending.RetainedResultState);
                Assert.Null(pending.RetainedResult!.RecoverCanonicalBytes());
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                    "UPDATE sessions SET \"RevokedAt\" = NULL WHERE \"Id\" = @session", ("session", fixture.Viewer.SessionId));
                Assert.Equal(ProjectTestJobState.Completed, Assert.IsType<ProjectTestJobView>(await runtime.ExecuteAsync(key)).State);
                Assert.Equal(ProjectTestRetainedResultState.Available,
                    Assert.IsType<ProjectTestJob>(await new ProjectTestJobStore(context).FindAsync(key, default)).RetainedResultState);
            }
        });

    [PostgreSqlFact]
    public Task SummaryOnlyHistoricalCompletionStaysUnavailableAfterExplicitSchemaUpgradeAndTerminalRetry() =>
        WithFixtureAsync(async fixture =>
        {
            fixture.Policy.Allowed.UnionWith([ProjectTestRuntime.StartOperation, ProjectTestRuntime.ReadOperation,
                ProjectTestRuntime.ExportOperation]);
            await using var context = fixture.Open();
            var runtime = fixture.Runtime(context);
            Assert.NotNull(await runtime.StartAsync(fixture.Request));
            var run = fixture.Evaluate();
            var summary = Summary(run);

            // Recreate the previous Development relation shape and write its original summary-only completion.
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE projectide_development.project_ide_test_jobs DROP COLUMN result_body");
            var serialized = JsonSerializer.Serialize(summary);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE projectide_development.project_ide_test_jobs
                SET state = 1, terminal_at = {Now.AddTicks(1)}, result = CAST({serialized} AS jsonb)
                WHERE job_id = {fixture.Key.JobId}
                """);
            await ProjectTestJobDevelopmentProvisioning.UpgradeRetainedResultsAsync(context);
            await ProjectTestJobDevelopmentProvisioning.UpgradeRetainedResultsAsync(context);

            fixture.Inputs.BeforeResolve = _ => throw new InvalidOperationException("Historical result bodies must not be reconstructed.");
            var original = Assert.IsType<ProjectTestJob>(await new ProjectTestJobStore(context).FindAsync(fixture.Key, default));
            Assert.Equal(ProjectTestRetainedResultState.Unavailable, original.RetainedResultState);
            Assert.Null(original.RetainedResult!.RecoverCanonicalBytes());
            Assert.Equal(summary, original.Result);
            Assert.Equal(ProjectTestJobState.Completed, Assert.IsType<ProjectTestJobView>(await runtime.ExecuteAsync(fixture.Key)).State);
            Assert.Equal(summary, Assert.IsType<ProjectTestJobView>(await runtime.FindAsync(fixture.Key)).Result);
            Assert.NotNull(await runtime.ExportAsync(fixture.Key));
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(fixture.Database,
                "SELECT result_body IS NULL FROM projectide_development.project_ide_test_jobs WHERE job_id = @job", ("job", fixture.Key.JobId)));
        });

    [PostgreSqlFact]
    public Task MismatchedResultBodyIsRefusedBeforeCommitAndTheWinningBodyIsImmutable() =>
        WithFixtureAsync(async fixture =>
        {
            fixture.Policy.Allowed.Add(ProjectTestRuntime.StartOperation);
            await using var context = fixture.Open();
            Assert.NotNull(await fixture.Runtime(context).StartAsync(fixture.Request));
            var store = new ProjectTestJobStore(context);
            var pending = Assert.IsType<ProjectTestJob>(await store.FindAsync(fixture.Key, default));
            var run = fixture.Evaluate();
            var summary = Summary(run);
            var completed = pending with
            {
                State = ProjectTestJobState.Completed, TerminalAtUtc = Now.AddTicks(1), Result = summary,
                RetainedResult = ProjectTestRetainedResult.Capture(run)
            };
            var wrong = completed with { Result = summary with { ResultDigest = fixture.Request.SourceDigest.Value } };
            Assert.Null(await store.TransitionAsync(pending, wrong, _ => Task.FromResult(true), default));
            Assert.Null(await store.TransitionAsync(pending, completed with { RetainedResult = null }, _ => Task.FromResult(true), default));
            var refused = Assert.IsType<ProjectTestJob>(await store.FindAsync(fixture.Key, default));
            Assert.Equal(ProjectTestJobState.Pending, refused.State);
            Assert.Equal(ProjectTestRetainedResultState.Unavailable, refused.RetainedResultState);

            var saved = Assert.IsType<ProjectTestJob>(await store.TransitionAsync(pending, completed, _ => Task.FromResult(true), default));
            Assert.Equal(run.Data.ToCanonicalBytes(), saved.RetainedResult!.RecoverCanonicalBytes());
            var replay = Assert.IsType<ProjectTestJob>(await store.TransitionAsync(pending,
                completed with { TerminalAtUtc = Now.AddSeconds(1) }, _ => Task.FromResult(true), default));
            Assert.Equal(saved.TerminalAtUtc, replay.TerminalAtUtc);
            Assert.Equal(saved.RetainedResult.RecoverCanonicalBytes(), replay.RetainedResult!.RecoverCanonicalBytes());
            var mutation = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE projectide_development.project_ide_test_jobs SET result_body = {new byte[] { 1 }} WHERE job_id = {fixture.Key.JobId}
                """));
            Assert.Equal("23514", mutation.SqlState);
            Assert.Equal(run.Data.ToCanonicalBytes(), Assert.IsType<ProjectTestJob>(await store.FindAsync(fixture.Key, default))
                .RetainedResult!.RecoverCanonicalBytes());
        });

    [PostgreSqlFact]
    public Task InvalidPresentBodyCannotBorrowTheSummaryVerdictOrBeReplacedByAnAuthorizedRetry() =>
        WithFixtureAsync(async fixture =>
        {
            fixture.Policy.Allowed.UnionWith([ProjectTestRuntime.StartOperation, ProjectTestRuntime.ReadOperation,
                ProjectTestRuntime.ExportOperation, ProjectTestRuntime.CancelOperation]);
            await using var context = fixture.Open();
            var runtime = fixture.Runtime(context);
            Assert.NotNull(await runtime.StartAsync(fixture.Request));
            var summary = Summary(fixture.Evaluate());
            Assert.Equal(CompilerVerdict.Pass, summary.Verdict);
            var serialized = JsonSerializer.Serialize(summary);
            var corruptBytes = new byte[] { 1 };
            // The isolated fixture owner models invalid persisted input; no product writer or guard is bypassed.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE projectide_development.project_ide_test_jobs
                SET state = 1, terminal_at = {Now.AddTicks(1)}, result = CAST({serialized} AS jsonb), result_body = {corruptBytes}
                WHERE job_id = {fixture.Key.JobId}
                """);

            var corrupt = Assert.IsType<ProjectTestJob>(await new ProjectTestJobStore(context).FindAsync(fixture.Key, default));
            Assert.Equal(ProjectTestRetainedResultState.Mismatch, corrupt.RetainedResultState);
            Assert.Null(corrupt.RetainedResult!.RecoverCanonicalBytes());
            Assert.Null(await runtime.FindAsync(fixture.Key));
            Assert.Null(await runtime.ExportAsync(fixture.Key));
            Assert.Null(await runtime.ExecuteAsync(fixture.Key));
            Assert.Null(await runtime.CancelAsync(fixture.Key));
            Assert.Null(await runtime.StartAsync(fixture.Request));
            Assert.Equal(ProjectTestRetainedResultState.Mismatch,
                Assert.IsType<ProjectTestJob>(await new ProjectTestJobStore(context).FindAsync(fixture.Key, default)).RetainedResultState);
        });

    private static ProjectTestResultSummary Summary(ProjectTestRun run) => new(run.Execution, run.Coverage, run.Verdict,
        run.Freshness, run.EvidenceKind, run.DiscoveredTests, run.ExecutedAssertions,
        run.Data.Value.GetProperty("planDigest").GetProperty("value").GetString()!, run.Digest.Value);
}
