using System.Text.Json.Nodes;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.ProjectIde;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Trait("Category", "PostgreSQLIntegration")]
public sealed class ProjectTestRuntimePostgreSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public Task FixedTestCompletesAcrossRequestsAndOnlyAuthorizedReadsExposeTheSummary() =>
        WithFixtureAsync(async fixture =>
        {
            fixture.Policy.Allowed.Add(ProjectTestRuntime.StartOperation);
            var request = fixture.Request;
            await using (var first = fixture.Open())
            await using (var concurrent = fixture.Open())
            {
                var runtime = fixture.Runtime(first);
                var copies = await Task.WhenAll(runtime.StartAsync(request), fixture.Runtime(concurrent).StartAsync(request));
                var job = Assert.IsType<ProjectTestJobView>(copies[0]);
                Assert.Equal(ProjectTestJobState.Pending, job.State);
                Assert.Equal(job, copies[1]);
                Assert.Equal(job, await runtime.StartAsync(request));
                var wrongInput = request with { InputDigest = request.SourceDigest };
                Assert.Null(await runtime.StartAsync(wrongInput));
                var wrongMaterialization = request with { JobId = Guid.NewGuid(), ExecutionInputDigest = request.SourceDigest };
                Assert.Null(await runtime.StartAsync(wrongMaterialization));
                Assert.Null(await runtime.FindAsync(fixture.Key));
            }

            await using (var second = fixture.Open())
            {
                var finished = Assert.IsType<ProjectTestJobView>(await fixture.Runtime(second).ExecuteAsync(fixture.Key));
                Assert.Equal(ProjectTestJobState.Completed, finished.State);
                Assert.Null(finished.Result);
            }

            await using (var third = fixture.Open())
            {
                var runtime = fixture.Runtime(third);
                Assert.Null(await runtime.ExportAsync(fixture.Key));
                fixture.Policy.Allowed.Add(ProjectTestRuntime.ReadOperation);
                var visible = Assert.IsType<ProjectTestJobView>(await runtime.FindAsync(fixture.Key));
                var result = Assert.IsType<ProjectTestResultSummary>(visible.Result);
                Assert.Equal(CompilerVerdict.Pass, result.Verdict);
                Assert.Equal(CompilerCoverage.Complete, result.Coverage);
                Assert.Equal(CompilerExecutionState.Completed, result.Execution);
                Assert.Equal(1, result.ExecutedAssertions);

                fixture.Policy.Allowed.Add(ProjectTestRuntime.ExportOperation);
                var export = Assert.IsType<SourceJson>(await runtime.ExportAsync(fixture.Key));
                Assert.Equal(result.ResultDigest, export.Value.GetProperty("resultDigest").GetString());
                Assert.Equal(request.ExecutionInputDigest.Value, export.Value.GetProperty("executionInputDigest").GetString());
                Assert.DoesNotContain("Input contract", export.CanonicalText, StringComparison.Ordinal);
                Assert.DoesNotContain("example.check", export.CanonicalText, StringComparison.Ordinal);

                var foreignBranch = new BranchRef(fixture.Key.Branch.TenantId, fixture.Key.Branch.ProjectId, BranchId.New());
                Assert.Null(await runtime.FindAsync(new(foreignBranch, fixture.Key.JobId)));
                var foreignTenant = new BranchRef(TenantId.New(), fixture.Key.Branch.ProjectId, fixture.Key.Branch.BranchId);
                Assert.Null(await runtime.FindAsync(new(foreignTenant, fixture.Key.JobId)));

                var sqlError = await Assert.ThrowsAsync<PostgresException>(() =>
                    third.Database.ExecuteSqlRawAsync("UPDATE project_ide_test_jobs SET state = 2, result = NULL"));
                Assert.Equal("23514", sqlError.SqlState);
                Assert.Equal(visible, await runtime.FindAsync(fixture.Key));
            }
        });

    [PostgreSqlFact]
    public Task CancellationWhileInputResolvesWinsAndLeavesNoPartiallyPublishedResult() =>
        WithFixtureAsync(async fixture =>
        {
            fixture.Policy.Allowed.UnionWith([ProjectTestRuntime.StartOperation, ProjectTestRuntime.CancelOperation,
                ProjectTestRuntime.ReadOperation, ProjectTestRuntime.ExportOperation]);
            await using var producer = fixture.Open();
            await using var consumer = fixture.Open();
            var runtime = fixture.Runtime(producer);
            Assert.NotNull(await runtime.StartAsync(fixture.Request));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            fixture.Inputs.BeforeResolve = async token =>
            {
                start.SetResult();
                await resume.Task.WaitAsync(token);
            };

            var executing = runtime.ExecuteAsync(fixture.Key, bound.Token);
            await start.Task.WaitAsync(bound.Token);
            var cancelled = Assert.IsType<ProjectTestJobView>(
                await fixture.Runtime(consumer).CancelAsync(fixture.Key, bound.Token));
            Assert.Equal(ProjectTestJobState.Cancelled, cancelled.State);
            resume.SetResult();
            var observed = Assert.IsType<ProjectTestJobView>(await executing);
            Assert.Equal(ProjectTestJobState.Cancelled, observed.State);
            Assert.Null(observed.Result);
            Assert.Null(await runtime.ExportAsync(fixture.Key, bound.Token));
        });

    [PostgreSqlFact]
    public Task SessionRevokedDuringSourceResolutionLeavesPendingJobAndDeniesDisclosure() =>
        WithFixtureAsync(async fixture =>
        {
            fixture.Policy.Allowed.UnionWith([ProjectTestRuntime.StartOperation, ProjectTestRuntime.ReadOperation,
                ProjectTestRuntime.CancelOperation, ProjectTestRuntime.ExportOperation]);
            await using var request = fixture.Open();
            var runtime = fixture.Runtime(request);
            Assert.NotNull(await runtime.StartAsync(fixture.Request));
            fixture.Inputs.BeforeResolve = async token =>
            {
                await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(fixture.Database);
                await writer.Sessions.Where(session => session.Id == fixture.Viewer.SessionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, Now), token);
            };

            Assert.Null(await runtime.ExecuteAsync(fixture.Key));
            Assert.Null(await runtime.FindAsync(fixture.Key));
            Assert.Null(await runtime.CancelAsync(fixture.Key));
            Assert.Null(await runtime.ExportAsync(fixture.Key));
            var retained = Assert.IsType<ProjectTestJob>(await new ProjectTestJobStore(request).FindAsync(fixture.Key, default));
            Assert.Equal(ProjectTestJobState.Pending, retained.State);
            Assert.Null(retained.Result);
        });

    private static Task WithFixtureAsync(Func<Fixture, Task> scenario) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
            {
                var evaluator = await ProjectTestRuntime.ReadLoadedEvaluatorAsync();
                var fixture = ProjectTestRunnerTests.Fixture();
                var settings = JsonNode.Parse(fixture.Settings.CanonicalText)!.AsObject();
                settings["evaluator"] = JsonNode.Parse(evaluator.Data.CanonicalText);
                var input = new CompilerInputManifest(fixture.Source, SourceJson.Parse(settings.ToJsonString()));
                var branch = input.Source.Context.Branch;

                await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var tenant = new Tenant(branch.TenantId.Value)
                { Name = "Project Test", Slug = "project-test", DisplayName = "Project Test" };
                var actor = new User { Email = "test-runtime@example.invalid", NormalizedEmail = "TEST-RUNTIME@EXAMPLE.INVALID" };
                var workspace = new Workspace
                { TenantId = tenant.Id, Name = "Project Test", Slug = "project-test", CreatedByUserId = actor.Id };
                var project = new Project
                {
                    Id = branch.ProjectId.Value, TenantId = tenant.Id, WorkspaceId = workspace.Id,
                    Name = "Project Test", Slug = "project-test", OwnerUserId = actor.Id, CreatedByUserId = actor.Id
                };
                var membership = new TenantUser
                { TenantId = tenant.Id, UserId = actor.Id, Status = TenantUserStatus.Active, JoinedAt = Now };
                var session = new Session
                { UserId = actor.Id, SessionKeyHash = "fixture-hash", ExpiresAt = Now.AddHours(1) };
                writer.AddRange(tenant, actor, workspace, project, membership, session);
                await writer.SaveChangesAsync();

                var tenantContext = new CurrentTenantService();
                tenantContext.SetTenant(tenant.Id, tenant.Slug);
                var hostRequest = new CompilerHostRequest(new(input));
                var request = new StartProjectTestJob(Guid.NewGuid(), input.Source.Context, input.Source.Digest,
                    input.InputDigest, hostRequest.ExecutionInputDigest);
                await scenario(new Fixture(connection, tenantContext, new Viewer(actor.Id, session.Id),
                    evaluator, new InputSource(hostRequest), request));
            });

    private sealed class Fixture(string database, CurrentTenantService tenant, Viewer viewer,
        CompilerArtifactIdentity evaluator, InputSource inputs, StartProjectTestJob request)
    {
        public string Database => database;
        public Viewer Viewer => viewer;
        public InputSource Inputs => inputs;
        public StartProjectTestJob Request => request;
        public ProjectTestJobKey Key => new(request.Context.Branch, request.JobId);
        public FixturePolicy Policy { get; } = new();

        public AppDbContext Open() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options, tenant);

        public ProjectTestRuntime Runtime(AppDbContext db)
        {
            var clock = new FixedClock();
            var contexts = new ProjectIdeViewerContextReader(viewer, tenant, new SessionRepository(db),
                new TenantRepository(db), clock);
            return new(new ProjectIdeOperationAccess(contexts, Policy), inputs, new ProjectTestJobStore(db), clock, evaluator);
        }
    }

    private sealed class FixturePolicy : IProjectIdeOperationAuthorization
    {
        public HashSet<SecurityOperationRef> Allowed { get; } = [];

        public ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
            SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Allowed.Contains(operation));
    }

    private sealed class InputSource(CompilerHostRequest input) : IProjectTestInputSource
    {
        public Func<CancellationToken, Task>? BeforeResolve { get; set; }

        public async Task<CompilerHostRequest?> ResolveAsync(ProjectIdeViewerContext viewer, ProjectTestJobBinding binding,
            CancellationToken cancellationToken)
        {
            if (BeforeResolve is { } beforeResolve)
                await beforeResolve(cancellationToken);
            return input;
        }
    }

    private sealed record Viewer(Guid? UserId, Guid? SessionId) : ICurrentUser
    {
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
