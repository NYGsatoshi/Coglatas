using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectHistoryRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly ProjectHistoryOperations Operations = new(new("fixture.history.query"), new("fixture.history.read"),
        new("fixture.history.compare"), new("fixture.history.navigate"), new("fixture.history.export"), new("fixture.history.revert"));

    [Fact]
    public async Task QueryAndNavigationUseExactAuthorizedLineageWhileExportRequiresItsOwnOperation()
    {
        var history = new HistoryFixture();
        var tenant = new TenantFixture(history.Before.Context.Branch.TenantId.Value);
        await using var db = tenant.CreateMemoryContext();
        var service = await tenant.CreateServiceAsync(db, history);
        var deniedRevision = history.Before.Revision.RevisionId.Value;
        history.Allowed = (_, source) => source.RevisionId != deniedRevision;

        var foreignBranch = new BranchRef(TenantId.New(), history.After.Revision.Branch.ProjectId, history.After.Revision.Branch.BranchId);
        var foreign = new ProjectHistorySource(SourceRevisionContext.Committed(new(foreignBranch, history.After.Revision.RevisionId)),
            history.After.SourceDigest, history.After.InputDigest);
        Assert.Equal(ProjectHistoryAccessState.NotFoundOrDenied, (await service.QueryAsync(foreign)).State);
        Assert.Equal(ProjectHistoryAccessState.NotFoundOrDenied, (await service.QueryAsync(history.After)).State);
        Assert.Equal(0, history.Loads);

        history.Allowed = (operation, _) => operation != Operations.Export;
        var query = await service.QueryAsync(history.After);
        Assert.Equal(ProjectHistoryAccessState.Ready, query.State);
        Assert.Equal(new[] { history.Before.Revision, history.After.Revision }, query.Value);
        Assert.Equal(0, history.Loads);
        var document = await service.NavigateAsync(history.After, history.AfterSource.Documents[0].DocumentId);
        Assert.Equal(history.AfterSource.Documents[0].Data, document.Value?.Data);
        Assert.Equal(ProjectHistoryAccessState.NotFoundOrDenied, (await service.ExportAsync(history.After)).State);

        history.Allowed = (_, _) => true;
        Assert.Equal(history.AfterSource.Data, (await service.ExportAsync(history.After)).Value);
    }

    [Fact]
    public async Task ComparisonUsesCommittedInputDigestAndWithholdsDeletedWindows()
    {
        var history = new HistoryFixture();
        var tenant = new TenantFixture(history.Before.Context.Branch.TenantId.Value);
        await using var db = tenant.CreateMemoryContext();
        var service = await tenant.CreateServiceAsync(db, history);

        var compared = await service.CompareAsync(history.Before, history.After);
        Assert.Equal(ProjectHistoryAccessState.Ready, compared.State);
        Assert.Equal(SemanticDiffState.Complete, compared.Value?.State);
        Assert.NotEmpty(compared.Value!.Changes);

        history.ReplaceInputs = true;
        var corrupt = await service.CompareAsync(history.Before, history.After);
        Assert.Equal(ProjectHistoryAccessState.Unavailable, corrupt.State);
        Assert.Null(corrupt.Value);
        history.ReplaceInputs = false;

        history.AfterLoad = () => { history.Retained = false; return Task.CompletedTask; };
        var tombstoned = await service.MaterializeAsync(history.After);
        Assert.Equal(ProjectHistoryAccessState.NotFoundOrDenied, tombstoned.State);
        Assert.Null(tombstoned.Value);
    }

    [Fact]
    public async Task RevertPreviewsAnInverseAgainstTheExactRetainedHeadAndWithholdsAfterHeadMovement()
    {
        var history = new HistoryFixture();
        var tenant = new TenantFixture(history.Before.Context.Branch.TenantId.Value);
        await using var db = tenant.CreateMemoryContext();
        var service = await tenant.CreateServiceAsync(db, history);
        var proposal = DraftRenameTests.Proposal(Input(source: history.HeadSource), 80);
        var preview = await service.PreviewRevertAsync(history.Before, history.After, history.Head, proposal);
        Assert.Equal(ProjectHistoryAccessState.Ready, preview.State);
        var plan = Assert.IsType<ProjectDraftRevertPlan>(preview.Value?.Plan);
        Assert.Equal(history.Head.Revision, plan.After.Source.Context.Proposal?.CapturedTargetHead);
        Assert.Equal(history.After.Revision, plan.RevertedRevision);
        Assert.Equal(history.BeforeSource.Documents[0].LogicalName, plan.After.Source.Documents[0].LogicalName);

        history.AfterLoad = () => { history.Latest = history.After.Revision; return Task.CompletedTask; };
        var stale = await service.PreviewRevertAsync(history.Before, history.After, history.Head, proposal);
        Assert.Equal(ProjectHistoryAccessState.NotFoundOrDenied, stale.State);
        Assert.Null(stale.Value);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task PersistedSessionRevocationDuringHistoryLoadWithholdsThePendingDisclosure() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
        {
            var history = new HistoryFixture();
            var tenant = new TenantFixture(history.Before.Context.Branch.TenantId.Value);
            await using var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            await using var requestDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options, tenant.CurrentTenant);
            await tenant.SeedAsync(seed);
            var service = tenant.Service(requestDb, history);
            Assert.Equal(ProjectHistoryAccessState.Ready, (await service.MaterializeAsync(history.After)).State);
            history.AfterLoad = async () =>
            {
                tenant.Session.RevokedAt = Now;
                await seed.SaveChangesAsync();
            };

            var result = await service.ExportAsync(history.After);
            Assert.Equal(ProjectHistoryAccessState.NotFoundOrDenied, result.State);
            Assert.Null(result.Value);

            history.AfterLoad = null;
            tenant.Session.RevokedAt = null;
            await seed.SaveChangesAsync();
            var publishCheck = history.CurrentChecks + 2;
            history.DuringCurrentCheck = async check =>
            {
                if (check == publishCheck)
                {
                    tenant.Session.RevokedAt = Now;
                    await seed.SaveChangesAsync();
                }
            };
            var late = await service.ExportAsync(history.After);
            Assert.Equal(ProjectHistoryAccessState.NotFoundOrDenied, late.State);
            Assert.Null(late.Value);
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task PostgreSqlCheckpointsSurviveReconnectAndACommittedTombstoneInvalidatesCapturedViews() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
        {
            var history = new HistoryFixture();
            var tenant = new TenantFixture(history.Before.Context.Branch.TenantId.Value);
            await using var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            await tenant.SeedAsync(seed);
            await InsertCheckpointAsync(seed, history.BeforeSource, 0);
            await InsertCheckpointAsync(seed, history.AfterSource, 1, history.Before.Revision);
            await InsertCheckpointAsync(seed, history.HeadSource, 2, history.After.Revision);

            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
            var heads = new FixtureHeads(history.Head.Revision);
            await using (var first = new AppDbContext(options, tenant.CurrentTenant))
            {
                var service = tenant.Service(first, history, new ProjectHistoryCheckpointStore(first, heads));
                Assert.Equal(history.AfterSource.Data, (await service.ExportAsync(history.After)).Value);
            }

            await using var reconnected = new AppDbContext(options, tenant.CurrentTenant);
            var store = new ProjectHistoryCheckpointStore(reconnected, heads);
            var reader = tenant.Service(reconnected, history, store);
            var compare = await reader.CompareAsync(history.Before, history.After);
            Assert.Equal(ProjectHistoryAccessState.Ready, compare.State);
            Assert.Equal(SemanticDiffState.Complete, compare.Value?.State);

            var proposal = DraftRenameTests.Proposal(Input(source: history.HeadSource), 90);
            var inverse = await reader.PreviewRevertAsync(history.Before, history.After, history.Head, proposal);
            Assert.Equal(DraftRevertState.Ready, inverse.Value?.State);
            var captured = Assert.IsType<ProjectHistoryWindow>(await store.CaptureAsync([history.Before, history.After], null, default));
            Assert.True(await store.IsCurrentAsync(captured, null, default));

            var revisionId = history.After.Revision.RevisionId.Value;
            Assert.Equal(1, await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE projectide_history_checkpoints
                SET "SourceBytes" = NULL, "InputBytes" = NULL, "DeletedAtUtc" = {Now}
                WHERE "RevisionId" = {revisionId}
                """));
            Assert.Null(await store.LoadAsync(captured, default));
            Assert.False(await store.IsCurrentAsync(captured, null, default));
            Assert.Null((await reader.ExportAsync(history.After)).Value);
            Assert.Null((await reader.CompareAsync(history.Before, history.After)).Value);
            Assert.Null((await reader.PreviewRevertAsync(history.Before, history.After, history.Head, proposal)).Value);
            Assert.Equal(1, await seed.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM projectide_history_checkpoints
                WHERE "RevisionId" = {revisionId} AND "DeletedAtUtc" IS NOT NULL
                  AND "SourceBytes" IS NULL AND "InputBytes" IS NULL
                """).SingleAsync());
        });

    private static Task<int> InsertCheckpointAsync(AppDbContext db, ProjectSource source, long sequence, params RevisionRef[] parents)
    {
        var revision = source.Context.CommittedRevision!;
        var input = Input(source: source);
        var parentJson = JsonSerializer.Serialize(parents.Select(parent => new
        {
            BranchId = parent.Branch.BranchId.Value,
            RevisionId = parent.RevisionId.Value
        }));
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projectide_history_checkpoints
                ("TenantId", "ProjectId", "BranchId", "RevisionId", "VersionId", "BranchSequence",
                 "Context", "SourceDigest", "InputDigest", "Parents", "SourceBytes", "InputBytes")
            VALUES ({revision.Branch.TenantId.Value}, {revision.Branch.ProjectId.Value}, {revision.Branch.BranchId.Value},
                {revision.RevisionId.Value}, {Guid.NewGuid()}, {sequence}, {source.Context.Data.CanonicalText},
                {source.Digest.Value}, {input.InputDigest.Value}, CAST({parentJson} AS jsonb),
                {source.Data.ToCanonicalBytes()}, {input.ToEnvelope().ToCanonicalBytes()})
            """);
    }

    private sealed class FixtureHeads(RevisionRef head) : IProjectHistoryHeadReader
    {
        public ValueTask<RevisionRef?> ReadAsync(BranchRef branch, CancellationToken cancellationToken) =>
            ValueTask.FromResult<RevisionRef?>(branch == head.Branch ? head : null);
    }

    private sealed class HistoryFixture : IProjectHistoryRuntimeStore, IProjectIdeOperationAuthorization
    {
        private readonly Guid _version = Guid.NewGuid();
        public ProjectSource BeforeSource { get; } = Revision(60, "Original");
        public ProjectSource AfterSource { get; } = Revision(61, "Renamed");
        public ProjectSource HeadSource { get; } = Revision(62, "Renamed");
        public ProjectHistorySource Before => HistorySource(BeforeSource);
        public ProjectHistorySource After => HistorySource(AfterSource);
        public ProjectHistorySource Head => HistorySource(HeadSource);
        public RevisionRef Latest { get; set; } = Revision(62, "Renamed").Context.CommittedRevision!;
        public bool ReplaceInputs { get; set; }
        public bool Retained { get; set; } = true;
        public int Loads { get; private set; }
        public int CurrentChecks { get; private set; }
        public Func<Task>? AfterLoad { get; set; }
        public Func<int, Task>? DuringCurrentCheck { get; set; }
        public Func<SecurityOperationRef, SecuritySourceIdentitySnapshot, bool> Allowed { get; set; } = (_, _) => true;

        public ValueTask<ProjectHistoryWindow?> CaptureAsync(IReadOnlyList<ProjectHistorySource> sources, RevisionRef? requiredHead,
            CancellationToken cancellationToken) => ValueTask.FromResult<ProjectHistoryWindow?>(Retained && (requiredHead is null || requiredHead == Latest)
                ? new(_version, requiredHead is null ? [Before, After] : [Before, After, Head]) : null);

        public async ValueTask<IReadOnlyList<ProjectHistoryStoredSource>?> LoadAsync(ProjectHistoryWindow window, CancellationToken cancellationToken)
        {
            Loads++;
            if (AfterLoad is { } callback)
                await callback();
            var prior = new ProjectHistoryStoredSource(ProjectHistoryRevision.Checkpoint(BeforeSource, 0), Input(source: BeforeSource).ToEnvelope());
            var after = new ProjectHistoryStoredSource(ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(BeforeSource, AfterSource), 1),
                Input(source: ReplaceInputs ? BeforeSource : AfterSource).ToEnvelope());
            return window.Sources.Count == 2 ? [prior, after] : [prior, after,
                new(ProjectHistoryRevision.Checkpoint(HeadSource, 2, [After.Revision]), Input(source: HeadSource).ToEnvelope())];
        }

        public async ValueTask<bool> IsCurrentAsync(ProjectHistoryWindow window, RevisionRef? requiredHead, CancellationToken cancellationToken)
        {
            CurrentChecks++;
            if (DuringCurrentCheck is { } observed)
                await observed(CurrentChecks);
            return window.Version == _version && Retained && (requiredHead is null || requiredHead == Latest);
        }

        public ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation, SecuritySourceIdentitySnapshot source,
            string sourceDigest, CancellationToken cancellationToken) => ValueTask.FromResult(Allowed(operation, source));

        private static ProjectHistorySource HistorySource(ProjectSource source) => new(source.Context, source.Digest, Input(source: source).InputDigest);

        private static ProjectSource Revision(int revision, string logicalName)
        {
            var basis = Source(revision);
            var document = basis.Documents[0];
            var renamed = SourceDocument.Create(document.DocumentId, logicalName, document.Root.Data);
            return ProjectSource.Create(basis.Context, [renamed], basis.Declarations, basis.Relations);
        }
    }

    private sealed class TenantFixture(Guid tenantId)
    {
        private readonly Tenant _tenant = new(tenantId) { Name = "History Test", Slug = "history-test" };
        private readonly User _user = new() { Email = "history@example.invalid", NormalizedEmail = "HISTORY@EXAMPLE.INVALID" };
        public Session Session { get; } = new() { SessionKeyHash = "synthetic-history", ExpiresAt = Now.AddHours(1) };
        public CurrentTenantService CurrentTenant { get; } = new();

        public AppDbContext CreateMemoryContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, CurrentTenant);

        public async Task SeedAsync(AppDbContext db)
        {
            db.Tenants.Add(_tenant);
            await db.SaveChangesAsync();
            CurrentTenant.SetTenant(_tenant.Id, _tenant.Slug);
            Session.UserId = _user.Id;
            db.AddRange(_user, Session, new TenantUser { TenantId = _tenant.Id, UserId = _user.Id, Status = TenantUserStatus.Active });
            await db.SaveChangesAsync();
        }

        public async Task<ProjectHistoryService> CreateServiceAsync(AppDbContext db, HistoryFixture history)
        {
            await SeedAsync(db);
            return Service(db, history);
        }

        public ProjectHistoryService Service(AppDbContext db, HistoryFixture history, IProjectHistoryRuntimeStore? store = null)
        {
            var viewers = new ProjectIdeViewerContextReader(new Viewer(_user.Id, Session.Id), CurrentTenant,
                new SessionRepository(db), new TenantRepository(db), new Clock());
            return new(new ProjectIdeOperationAccess(viewers, history), store ?? history, Operations);
        }

    }

    private sealed record Viewer(Guid? UserId, Guid? SessionId) : ICurrentUser
    {
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
