using System.Text.Json.Nodes;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.ProjectIde;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.PostgreSql;

public sealed class ProjectScenarioRuntimePostgreSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Project = Guid.Parse(Id(2));

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task SavedHypothesisSurvivesNewRequestAndKeepsPrivateValuesSeparate() =>
        WithScenarioDatabase(async fixture =>
        {
            var input = Basis(fixture.Tenant.Id);
            fixture.Policy.ExactBasis = ProjectScenarioSource.Capture(input);
            var model = ProjectSemanticBinder.Bind(input);
            var change = Override(input, "20");
            await using (var dirty = fixture.OpenContext())
            {
                var stagedSession = Assert.IsType<Session>(await dirty.Sessions.FindAsync(fixture.Session.Id));
                stagedSession.LastSeenAt = Now;
                Assert.Null(await fixture.Runtime(dirty).SaveAsync(model, input.InputDigest, [change], "dirty-request", default));
                Assert.Equal(EntityState.Modified, dirty.Entry(stagedSession).State);
                Assert.Null(await fixture.Writer.Sessions.AsNoTracking()
                    .Where(row => row.Id == fixture.Session.Id).Select(row => row.LastSeenAt).SingleAsync());
            }
            ProjectScenarioSummary saved;
            await using (var initial = fixture.OpenContext())
            {
                var runtime = fixture.Runtime(initial);
                Assert.Null(await runtime.SaveAsync(model, input.InputDigest, [change], "short", default));
                Assert.Null(await runtime.SaveAsync(model, input.InputDigest, [change], "invalid\nkey", default));
                saved = Assert.IsType<ProjectScenarioSummary>(await runtime.SaveAsync(model, input.InputDigest, [change], "save-request", default));
                Assert.Equal(saved, await runtime.SaveAsync(model, input.InputDigest, [change], "save-request", default));
                Assert.Null(await runtime.SaveAsync(model, input.InputDigest, [Override(input, "30")], "save-request", default));
            }

            await using var fresh = fixture.OpenContext();
            var reopened = fixture.Runtime(fresh);
            Assert.Equal(saved, await reopened.FindAsync(input, saved.Id, default));
            Assert.Null(await reopened.FindAsync(Basis(fixture.Tenant.Id, revision: 15), saved.Id, default));
            Assert.Null(await reopened.FindAsync(Basis(Guid.NewGuid()), saved.Id, default));
            var substituted = Basis(fixture.Tenant.Id, title: "unadmitted-basis-title");
            Assert.Null(await reopened.FindAsync(substituted, saved.Id, default));
            Assert.Null(await reopened.SaveAsync(ProjectSemanticBinder.Bind(substituted), substituted.InputDigest,
                [Override(substituted, "20")], "substituted", default));

            fixture.Policy.DeniedOperation = ProjectScenarioOperations.ReadAssumptions;
            Assert.Equal(saved, await reopened.FindAsync(input, saved.Id, default));
            Assert.Null(await reopened.ReadAssumptionsAsync(input, saved.Id, default));
            fixture.Policy.DeniedOperation = null;
            var overlay = Assert.IsType<SourceJson>(await reopened.ReadAssumptionsAsync(input, saved.Id, default));
            Assert.Single(overlay.Value.GetProperty("overrides").EnumerateArray());
            Assert.Equal(change.Value?.CanonicalText, overlay.Value.GetProperty("overrides")[0].GetProperty("value").GetRawText());
            Assert.DoesNotContain("private-basis-title", overlay.CanonicalText, StringComparison.Ordinal);

            var audit = Assert.Single(await fresh.AuditLogs.AsNoTracking().ToListAsync());
            Assert.Equal("Security.ProjectIde.ScenarioSaved", audit.Action);
            Assert.Equal(saved.Id.Value, audit.EntityId);
            Assert.Null(audit.Summary);
            Assert.Null(audit.MetadataJson);
            Assert.Single(await fresh.IdempotencyRecords.AsNoTracking().ToListAsync());
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => fresh.Database.ExecuteSqlRawAsync(
                "UPDATE projectide_scenarios SET \"CanonicalOverlay\" = \"CanonicalOverlay\""));
            Assert.Equal(PostgresErrorCodes.RaiseException, immutable.SqlState);
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ExactSavedInputsCompareAndPersistedSessionRevocationWithdrawsBothReads() =>
        WithScenarioDatabase(async fixture =>
        {
            var input = Basis(fixture.Tenant.Id);
            fixture.Policy.ExactBasis = ProjectScenarioSource.Capture(input);
            var model = ProjectSemanticBinder.Bind(input);
            var change = Override(input, "20");
            await using var db = fixture.OpenContext();
            var runtime = fixture.Runtime(db);
            var saved = Assert.IsType<ProjectScenarioSummary>(await runtime.SaveAsync(model, input.InputDigest, [change], "compare-request", default));
            var restored = Assert.IsType<CompilerInputManifest>(await runtime.MaterializeAsync(model, saved.Id, default));
            var original = Run(input);
            var modified = Run(restored);
            var comparison = Assert.IsType<SavedScenarioComparison>(
                await runtime.CompareAsync(saved.Id, original, original.Digest, modified, modified.Digest, default));
            Assert.True(comparison.SourceChanged);
            Assert.Equal(SimulationRunComparisonState.Complete, comparison.RunState);
            Assert.Equal(15, comparison.ObservedElapsedDelta);
            Assert.Null(await runtime.CompareAsync(saved.Id, original, original.Manifest.Digest, modified, modified.Digest, default));

            fixture.Policy.DenyScenario = true;
            Assert.Null(await runtime.CompareAsync(saved.Id, original, original.Digest, modified, modified.Digest, default));
            fixture.Policy.DenyScenario = false;
            fixture.Policy.BeforeDecision = async (operation, source, token) =>
            {
                if (operation == ProjectScenarioOperations.ReadAssumptions && source.ScenarioId == saved.Id.Value)
                {
                    fixture.Session.RevokedAt = Now;
                    await fixture.Writer.SaveChangesAsync(token);
                }
            };
            Assert.Null(await runtime.ReadAssumptionsAsync(input, saved.Id, default));
            Assert.Null(await runtime.MaterializeAsync(model, saved.Id, default));
            Assert.Null(await runtime.FindAsync(input, saved.Id, default));
            Assert.Null(await runtime.CompareAsync(saved.Id, original, original.Digest, modified, modified.Digest, default));
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task RevocationDuringSaveRollsBackIdempotencyAndAudit() =>
        WithScenarioDatabase(async fixture =>
        {
            var input = Basis(fixture.Tenant.Id);
            fixture.Policy.ExactBasis = ProjectScenarioSource.Capture(input);
            var model = ProjectSemanticBinder.Bind(input);
            await using var db = fixture.OpenContext();
            var runtime = fixture.Runtime(db);
            var decisions = 0;
            fixture.Policy.BeforeDecision = async (operation, _, token) =>
            {
                if (operation == ProjectScenarioOperations.Save && ++decisions == 3)
                {
                    fixture.Session.RevokedAt = Now;
                    await fixture.Writer.SaveChangesAsync(token);
                }
            };

            var change = Override(input, "20");
            Assert.Null(await runtime.SaveAsync(model, input.InputDigest, [change], "staged-request", default));
            Assert.Empty(await db.IdempotencyRecords.AsNoTracking().ToListAsync());
            Assert.Empty(await db.AuditLogs.AsNoTracking().ToListAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM projectide_scenarios").SingleAsync());
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ConcurrentExactRetriesConvergeOnOneSavedVersion() => WithScenarioDatabase(async fixture =>
    {
        var input = Basis(fixture.Tenant.Id);
        fixture.Policy.ExactBasis = ProjectScenarioSource.Capture(input);
        var model = ProjectSemanticBinder.Bind(input);
        var change = Override(input, "20");
        await using var first = fixture.OpenContext();
        await using var second = fixture.OpenContext();
        var saves = await Task.WhenAll(
            fixture.Runtime(first).SaveAsync(model, input.InputDigest, [change], "concurrent", default),
            fixture.Runtime(second).SaveAsync(model, input.InputDigest, [change], "concurrent", default));

        Assert.IsType<ProjectScenarioSummary>(saves[0]);
        Assert.Equal(saves[0], saves[1]);
        Assert.Single(await first.IdempotencyRecords.AsNoTracking().ToListAsync());
        Assert.Single(await first.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(1, await first.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM projectide_scenarios").SingleAsync());
    });

    private static CompilerInputManifest Basis(Guid tenantId, int revision = 5, string title = "private-basis-title")
    {
        var context = SourceRevisionContext.Committed(new(new(new(tenantId), new(Project), BranchId.Parse(Id(3))), RevisionId.Parse(Id(revision))));
        var document = SourceDocument.Create(DocumentId.Parse(Id(4)), title,
            Json(Obj(new { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, payload = new { } })));
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
            payload = new { duration = Duration("5").Value, ports = Array.Empty<object>() } });
        var source = ProjectSource.Create(context, [document], Json(new JsonArray(task)), Json(new JsonArray()));
        return CompilerExecutionPlanTests.Request(Input(source: source));
    }

    private static SourceJson Duration(string value) =>
        Json(Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
            state = "coglatas.known", value }));

    private static ProjectScenarioOverride Override(CompilerInputManifest input, string value) =>
        new(ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context, entityId: EntityId.Parse(Id(7))),
            ProjectScenarioOverrideKind.TaskDuration, Duration(value));

    private static ProjectSimulationRun Run(CompilerInputManifest input)
    {
        var publication = PureCoreCompiler.Compile(input, input.Compiler).Publication;
        var plan = Assert.IsType<CompilerExecutionPlan>(publication?.ExecutionPlan);
        return ProjectSimulation.Execute(new(plan, Guid.NewGuid()), input.InputDigest);
    }

    private static Task WithScenarioDatabase(Func<Fixture, Task> execute) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
        {
            await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            var tenant = new Tenant { Slug = "scenario-test", Name = "Scenario", DisplayName = "Scenario" };
            var user = new User { Email = "scenario@example.invalid", NormalizedEmail = "SCENARIO@EXAMPLE.INVALID" };
            var session = new Session { UserId = user.Id, SessionKeyHash = "synthetic-scenario-hash", ExpiresAt = Now.AddHours(1) };
            var workspace = new Workspace { TenantId = tenant.Id, Name = "Scenario", Slug = "scenario-test", CreatedByUserId = user.Id };
            var project = new Project { Id = Project, TenantId = tenant.Id, WorkspaceId = workspace.Id, Name = "Scenario",
                Slug = "scenario-test", OwnerUserId = user.Id, CreatedByUserId = user.Id };
            writer.AddRange(tenant, user, session, workspace, project, new TenantUser
            {
                TenantId = tenant.Id, UserId = user.Id, JoinedAt = Now, Status = TenantUserStatus.Active
            });
            await writer.SaveChangesAsync();
            await execute(new(connection, writer, tenant, user, session));
        });

    private sealed class Fixture(string connection, AppDbContext writer, Tenant tenant, User user, Session session)
    {
        public AppDbContext Writer { get; } = writer;
        public Tenant Tenant { get; } = tenant;
        public Session Session { get; } = session;
        public ScenarioPolicy Policy { get; } = new();
        public AppDbContext OpenContext() => PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
        public ProjectScenarioRuntime Runtime(AppDbContext db)
        {
            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(Tenant.Id, Tenant.Slug);
            var viewer = new Viewer(user.Id, Session.Id);
            var clock = new Clock();
            var access = new ProjectIdeOperationAccess(
                new ProjectIdeViewerContextReader(viewer, currentTenant, new SessionRepository(db), new TenantRepository(db), clock), Policy);
            return new(access, new ProjectScenarioStore(db), new EfCreateIdempotencyCoordinator(db),
                new DbAuditLogger(db, clock, viewer, currentTenant), clock);
        }
    }

    private sealed class ScenarioPolicy : IProjectIdeOperationAuthorization
    {
        public ProjectScenarioSource? ExactBasis { get; set; }
        public SecurityOperationRef? DeniedOperation { get; set; }
        public bool DenyScenario { get; set; }
        public Func<SecurityOperationRef, SecuritySourceIdentitySnapshot, CancellationToken, Task>? BeforeDecision { get; set; }

        public async ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
            SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken)
        {
            if (BeforeDecision is { } observe)
                await observe(operation, source, cancellationToken);
            return viewer.TenantId == source.TenantId && source.ProjectId == Project && operation != DeniedOperation &&
                (source.ScenarioId is not null ? !DenyScenario : ExactBasis is { } basis &&
                    source.ContextDigest == basis.Identity.ContextDigest && sourceDigest == basis.Digest);
        }
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
        public DateTimeOffset UtcNow => Now;
    }
}
