using System.Text.Json;
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
public sealed class ProjectIdeInspectionRuntimePostgreSqlTests
{
    [PostgreSqlFact]
    public Task ProfileChecksOperationPolicyExactRecordingAndRevocationBeforeDisclosure() => WithRuntimeAsync(async fixture =>
    {
        var input = fixture.Plan.Input;
        var run = ProjectSimulation.Execute(new(fixture.Plan, Guid.NewGuid()), input.InputDigest);
        fixture.Recordings.Simulation = run;
        var reference = Reference(input, run.Manifest.RunId, run.Digest);
        var view = Assert.IsType<ProjectIdeModelProfileView>(await fixture.Runtime.QueryProfileAsync(reference, 6));
        Assert.Equal("SimulatedModel", view.InputKind);
        Assert.Equal(SimulationTraceProfileState.Complete, view.State);
        Assert.Equal(15, view.ElapsedVirtualTicks);
        Assert.Equal(8, view.BusyVirtualTicks);
        Assert.Equal("10", view.TaskVirtualTicks);
        var serialized = JsonSerializer.Serialize(view);
        Assert.DoesNotContain(run.Manifest.RunId.ToString(), serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Source.Context.Branch.ProjectId.ToString(), serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceEntityId", serialized, StringComparison.Ordinal);

        Assert.Null(await fixture.Runtime.QueryProfileAsync(reference, 5));
        Assert.Null(await fixture.Runtime.QueryProfileAsync(reference with { RecordingDigest = run.Manifest.Digest }, 6));
        Assert.Null(await fixture.Runtime.QueryProfileAsync(reference with { Source = reference.Source with { ProjectId = Guid.NewGuid() } }, 6));
        fixture.Authorization.Denied = ProjectIdeInspectionRuntime.ProfileExport;
        Assert.Null(await fixture.Runtime.ExportProfileAsync(reference, 6));
        fixture.Authorization.Denied = null;
        Assert.Equal(view, await fixture.Runtime.ExportProfileAsync(reference, 6));

        fixture.Retention.Allowed = false;
        Assert.Null(await fixture.Runtime.QueryProfileAsync(reference, 6));
        fixture.Retention.Allowed = true;
        fixture.Recordings.BeforeRead = fixture.RevokeSessionAsync;
        Assert.Null(await fixture.Runtime.QueryProfileAsync(reference, 6));
        Assert.Null(await fixture.Runtime.ExportProfileAsync(reference, 6));
    });

    [PostgreSqlFact]
    public Task OutputBindsOriginalCaptureAndLimitsReadsExportsAndDeletion() => WithRuntimeAsync(async fixture =>
    {
        var input = fixture.Plan.Input;
        var request = new CompilerHostRequest(new(input));
        using var local = new LocalCompilerRecordingSession(PredicateCompilerHostTests.Profile(input));
        var invocation = await LocalCompilerRecordedInvocation.ExecuteAsync(local,
            local.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs), request);
        var snapshot = Assert.IsType<ProjectCompilerOutputSnapshot>(invocation.Receipt.Snapshot);
        Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, snapshot.State);
        fixture.Recordings.Compiler = invocation;
        var reference = Reference(input, invocation.Receipt.Manifest.RunId, invocation.Receipt.Digest);
        var key = new CompilerOutputCaptureKey(Guid.NewGuid(), reference);
        Assert.Null(await fixture.Runtime.CaptureOutputAsync(key, snapshot.RetainedBodyBytes));
        Assert.Equal(CompilerRecordingWriteOutcome.Recorded, await fixture.Evidence.RecordAsync(invocation, default));

        Assert.Null(await fixture.Runtime.CaptureOutputAsync(key, snapshot.RetainedBodyBytes - 1));
        var view = Assert.IsType<CompilerOutputCaptureView>(await fixture.Runtime.CaptureOutputAsync(key, snapshot.RetainedBodyBytes));
        Assert.Equal(snapshot.State, view.State);
        Assert.True(view.SemanticModelWasCaptured);
        Assert.True(view.IrWasCaptured);
        Assert.True(view.SourceMapWasCaptured);
        Assert.Equal(view, await fixture.Runtime.QueryOutputAsync(key));
        var otherTenant = new CurrentTenantService();
        otherTenant.SetTenant(Guid.NewGuid(), "other-inspection-tenant");
        await using var otherScope = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Database).Options, otherTenant);
        Assert.Null(await new CompilerOutputCaptureStore(otherScope, new Clock()).FindAsync(key, default));
        Assert.Equal(view, await fixture.Runtime.CaptureOutputAsync(key, snapshot.RetainedBodyBytes));
        Assert.Null(await fixture.Runtime.CaptureOutputAsync(key, snapshot.RetainedBodyBytes + 1));
        var serialized = JsonSerializer.Serialize(view);
        Assert.DoesNotContain("Bodies", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Source.Digest.Value, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Source.Context.Branch.BranchId.ToString(), serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(invocation.Receipt.Digest.Value, serialized, StringComparison.Ordinal);
        var payload = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
            "SELECT \"Projection\"::text FROM projectide_development.projectide_compiler_output_captures WHERE \"CaptureId\" = @id", ("id", key.CaptureId));
        Assert.DoesNotContain("Bodies", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Source.Digest.Value, payload, StringComparison.Ordinal);
        var mutation = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
            "UPDATE projectide_development.projectide_compiler_output_captures SET \"MaximumBodyBytes\" = 1 WHERE \"CaptureId\" = @id", ("id", key.CaptureId)));
        Assert.Equal(PostgresErrorCodes.RaiseException, mutation.SqlState);

        fixture.Authorization.Denied = ProjectIdeInspectionRuntime.OutputExport;
        Assert.Null(await fixture.Runtime.ExportOutputAsync(key));
        fixture.Authorization.Denied = ProjectIdeInspectionRuntime.OutputDelete;
        Assert.False(await fixture.Runtime.DeleteOutputAsync(key));
        Assert.Equal(view, await fixture.Runtime.ExportOutputAsync(key));
        fixture.Authorization.Denied = null;
        Assert.True(await fixture.Runtime.DeleteOutputAsync(key));
        Assert.Null(await fixture.Runtime.QueryOutputAsync(key));
        Assert.Null(await fixture.Runtime.ExportOutputAsync(key));
        Assert.Null(await fixture.Runtime.CaptureOutputAsync(key, snapshot.RetainedBodyBytes));
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(fixture.Database,
            """
            SELECT "DeletedAtUtc" IS NOT NULL AND "RunId" IS NULL AND "BindingDigest" IS NULL
                AND "MaximumBodyBytes" IS NULL AND "Projection" IS NULL
            FROM projectide_development.projectide_compiler_output_captures WHERE "CaptureId" = @id
            """, ("id", key.CaptureId)));

        var raceKey = key with { CaptureId = Guid.NewGuid() };
        Assert.Equal(view, await fixture.Runtime.CaptureOutputAsync(raceKey, snapshot.RetainedBodyBytes));
        var queryDecisions = 0;
        fixture.Retention.BeforeDecision = async operation =>
        {
            if (operation == ProjectIdeInspectionRuntime.OutputQuery && ++queryDecisions == 2)
                Assert.True(await fixture.Runtime.DeleteOutputAsync(raceKey));
        };
        Assert.Null(await fixture.Runtime.QueryOutputAsync(raceKey));
        fixture.Retention.BeforeDecision = null;

        fixture.Captures.BeforeWrite = fixture.RevokeSessionAsync;
        var revokedKey = key with { CaptureId = Guid.NewGuid() };
        Assert.Null(await fixture.Runtime.CaptureOutputAsync(revokedKey, snapshot.RetainedBodyBytes));
        Assert.Null(await fixture.Captures.FindAsync(revokedKey, default));
    });

    private static ProjectIdeInspectionReference Reference(CompilerInputManifest input, Guid id, ContentDigest digest) =>
        new(SecuritySourceIdentitySnapshot.Capture(input.Source.Context, input.InputDigest), input.Source.Digest, id, digest);

    private static Task WithRuntimeAsync(Func<RuntimeFixture, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
        {
            var plan = ProjectSimulationTests.Plan();
            var branch = plan.Input.Source.Context.Branch;
            await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(connection, """
                SELECT to_regclass('public.projectide_compiler_output_captures') IS NULL
                    AND to_regclass('projectide_development.projectide_compiler_output_captures') IS NULL
                """));
            Assert.DoesNotContain(writer.Model.GetEntityTypes(), entity => entity.GetTableName() == "projectide_compiler_output_captures");
            await CompilerRecordingDevelopmentSchema.ProvisionAsync(writer);
            await CompilerOutputCaptureDevelopmentSchema.ProvisionAsync(writer);
            await CompilerOutputCaptureDevelopmentSchema.ProvisionAsync(writer);
            var tenant = new Tenant { Id = branch.TenantId.Value, Name = "Inspection", DisplayName = "Inspection", Slug = "inspection" };
            var user = new User { Email = "inspection@example.invalid", NormalizedEmail = "INSPECTION@EXAMPLE.INVALID" };
            var session = new Session { UserId = user.Id, SessionKeyHash = "synthetic-inspection", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
            var workspace = new Workspace { TenantId = tenant.Id, Name = "Inspection", Slug = "inspection", CreatedByUserId = user.Id };
            var project = new Project { Id = branch.ProjectId.Value, TenantId = tenant.Id, WorkspaceId = workspace.Id,
                Name = "Inspection", Slug = "inspection", OwnerUserId = user.Id, CreatedByUserId = user.Id };
            var membership = new TenantUser { TenantId = tenant.Id, UserId = user.Id, Status = TenantUserStatus.Active };
            writer.AddRange(tenant, user, session, workspace, project, membership);
            await writer.SaveChangesAsync();

            var tenantContext = new CurrentTenantService();
            tenantContext.SetTenant(tenant.Id, tenant.Slug);
            await using var request = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options, tenantContext);
            var clock = new Clock();
            var authorization = new TestAuthorization(branch);
            var viewerContext = new ProjectIdeViewerContextReader(new Viewer(user.Id, session.Id), tenantContext,
                new SessionRepository(request), new TenantRepository(request), clock);
            var access = new ProjectIdeOperationAccess(viewerContext, authorization);
            var retention = new TestRetention();
            var recordings = new TestRecordings();
            var evidence = new CompilerRecordingStore(request, access, clock);
            var captures = new TestCaptures(new CompilerOutputCaptureStore(request, clock));
            var runtime = new ProjectIdeInspectionRuntime(access, retention, recordings, evidence, captures);
            await test(new(connection, plan, runtime, authorization, retention, recordings, evidence, captures, async () =>
            {
                session.RevokedAt = clock.UtcNow;
                await writer.SaveChangesAsync();
            }));
        });

    private sealed record RuntimeFixture(string Database, CompilerExecutionPlan Plan, ProjectIdeInspectionRuntime Runtime, TestAuthorization Authorization,
        TestRetention Retention, TestRecordings Recordings, CompilerRecordingStore Evidence, TestCaptures Captures, Func<Task> RevokeSessionAsync);

    private sealed class TestAuthorization(BranchRef branch) : IProjectIdeOperationAuthorization
    {
        public SecurityOperationRef? Denied { get; set; }

        public ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
            SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(operation != Denied && source.ProjectId == branch.ProjectId.Value && source.BranchId == branch.BranchId.Value);
    }

    private sealed class TestRetention : IProjectIdeInspectionPolicy
    {
        public bool Allowed { get; set; } = true;
        public Func<SecurityOperationRef, Task>? BeforeDecision { get; set; }

        public async Task<bool> AllowsAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation, ProjectIdeInspectionReference reference,
            CancellationToken cancellationToken)
        {
            if (BeforeDecision is not null)
                await BeforeDecision(operation);
            return Allowed;
        }
    }

    private sealed class TestRecordings : IProjectIdeInspectionRecordings
    {
        public ProjectSimulationRun? Simulation { get; set; }
        public LocalCompilerRecordedInvocation? Compiler { get; set; }
        public Func<Task>? BeforeRead { get; set; }

        public async Task<ProjectSimulationRun?> FindSimulationAsync(ProjectIdeViewerContext viewer, ProjectIdeInspectionReference reference,
            CancellationToken cancellationToken)
        {
            if (BeforeRead is not null)
                await BeforeRead();
            return Simulation;
        }

        public Task<LocalCompilerRecordedInvocation?> FindCompilerAsync(ProjectIdeViewerContext viewer, ProjectIdeInspectionReference reference,
            CancellationToken cancellationToken) => Task.FromResult(Compiler);
    }

    private sealed class TestCaptures(CompilerOutputCaptureStore store) : ICompilerOutputCaptureStore
    {
        public Func<Task>? BeforeWrite { get; set; }

        public Task<CompilerOutputCapture?> FindAsync(CompilerOutputCaptureKey key, CancellationToken cancellationToken) =>
            store.FindAsync(key, cancellationToken);

        public Task<CompilerOutputCapture?> RetainAsync(CompilerOutputCapture capture, Func<CancellationToken, Task<bool>> reauthorize,
            CancellationToken cancellationToken) => store.RetainAsync(capture, async token =>
            {
                if (BeforeWrite is not null)
                    await BeforeWrite();
                return await reauthorize(token);
            }, cancellationToken);

        public Task<bool> DeleteAsync(CompilerOutputCapture capture, Func<CancellationToken, Task<bool>> reauthorize,
            CancellationToken cancellationToken) => store.DeleteAsync(capture, reauthorize, cancellationToken);
    }

    private sealed class Viewer(Guid userId, Guid sessionId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => sessionId;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
