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
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Tests.PostgreSql;

public sealed class ProjectDraftRenamePersistencePostgreSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task RefusalRollsBackDraftAndEventsThenTheSameRequestCanSucceedAndReplay() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
            {
                var input = DraftRenameTests.Draft();
                var draft = input.Source.Context.Proposal!;
                await using var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                Assert.DoesNotContain(seed.Model.GetEntityTypes(), entity => entity.GetTableName() == "project_ide_draft_candidates");
                Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(connection,
                    "SELECT to_regclass('public.project_ide_draft_candidates') IS NULL " +
                    "AND to_regclass('projectide_development.project_ide_draft_candidates') IS NULL"));
                await ProjectDraftRenameDevelopmentSchema.CreateAsync(seed);
                var tenant = new Tenant(draft.TargetBranch.TenantId.Value) { Name = "Draft Test", Slug = "draft-test" };
                var user = new User { Email = "draft@example.invalid", NormalizedEmail = "DRAFT@EXAMPLE.INVALID" };
                var workspace = new Workspace { TenantId = tenant.Id, Name = "Draft", Slug = "draft", CreatedByUserId = user.Id };
                var project = new Project
                {
                    Id = draft.TargetBranch.ProjectId.Value, TenantId = tenant.Id, WorkspaceId = workspace.Id,
                    Name = "Draft", Slug = "draft", OwnerUserId = user.Id, CreatedByUserId = user.Id
                };
                var session = new Session { UserId = user.Id, SessionKeyHash = "synthetic-draft", ExpiresAt = Now.AddHours(1) };
                var membership = new TenantUser { TenantId = tenant.Id, UserId = user.Id, Status = TenantUserStatus.Active };
                seed.AddRange(tenant, user, workspace, project, session, membership);
                await seed.SaveChangesAsync();
                await seed.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO projectide_development.project_ide_draft_candidates ("CandidateId", "TenantId", "ProjectId", "BranchId",
                        "ProposalId", "SourceBytes", "InputBytes", "SourceDigest", "InputDigest")
                    VALUES ({draft.CandidateRevisionId.Value}, {tenant.Id}, {project.Id}, {draft.TargetBranch.BranchId.Value},
                        {draft.ProposalId.Value}, {input.Source.Data.ToCanonicalBytes()}, {input.ToEnvelope().ToCanonicalBytes()},
                        {input.Source.Digest.Value}, {input.InputDigest.Value})
                    """);

                var currentTenant = new CurrentTenantService();
                currentTenant.SetTenant(tenant.Id, tenant.Slug);
                var afterFlush = new AfterFlush();
                await using var requestDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(connection).AddInterceptors(afterFlush).Options, currentTenant);
                var viewer = new Viewer(user.Id, session.Id);
                var clock = new Clock();
                var outbox = new FixtureOutbox(requestDb);
                var materializations = new FixtureMaterializations(draft.CapturedTargetHead);
                ProjectDraftRenameService CreateService(AppDbContext context, FixtureOutbox events)
                {
                    var access = new ProjectIdeOperationAccess(new ProjectIdeViewerContextReader(viewer, currentTenant,
                        new SessionRepository(context), new TenantRepository(context), clock), new FixtureAuthority(draft, input.Source.Digest));
                    var store = new ProjectDraftRenameStore(context, materializations,
                        new FixtureAudit(new DbAuditLogger(context, clock, viewer, currentTenant)), events);
                    return new ProjectDraftRenameService(access, store, new EfCreateIdempotencyCoordinator(context));
                }

                var service = CreateService(requestDb, outbox);
                var request = new ProjectDraftRenameRequest(DraftRenameTests.Target(input), input.Source.Digest, input.InputDigest, "Private name");
                var preview = Assert.IsType<ProjectDraftRenamePreview>(await service.PreviewAsync(request));
                var command = new ProjectDraftRenameConfirmation(request, preview.NextCandidate, preview.PreviewDigest, "retryable-draft");

                materializations.CurrentHead = new(draft.TargetBranch, RevisionId.New());
                Assert.Null(await service.PreviewAsync(request));
                Assert.Equal(ProjectDraftRenameSaveState.Unavailable, (await service.SaveAsync(command)).State);
                materializations.CurrentHead = draft.CapturedTargetHead;

                Assert.Equal(ProjectDraftRenameSaveState.Unavailable, (await service.SaveAsync(command)).State);
                Assert.Equal(1, await CandidateCount(connection));
                Assert.Empty(await seed.IdempotencyRecords.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.AuditLogs.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.OutboxEvents.AsNoTracking().ToListAsync());

                outbox.Accept = true;
                using (var cancelled = new CancellationTokenSource())
                {
                    outbox.CancelDuringStage = cancelled;
                    var cancellationToken = cancelled.Token;
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(command, cancellationToken));
                }
                Assert.False(requestDb.ChangeTracker.HasChanges());
                Assert.Equal(1, await CandidateCount(connection));
                Assert.Empty(await seed.IdempotencyRecords.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.AuditLogs.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.OutboxEvents.AsNoTracking().ToListAsync());

                outbox.BeforeReturn = () => SetSessionRevokedAsync(connection, session.Id, Now);
                Assert.Equal(ProjectDraftRenameSaveState.Unavailable, (await service.SaveAsync(command)).State);
                Assert.Equal(1, await CandidateCount(connection));
                Assert.Empty(await seed.IdempotencyRecords.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.AuditLogs.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.OutboxEvents.AsNoTracking().ToListAsync());

                outbox.BeforeReturn = null;
                await SetSessionRevokedAsync(connection, session.Id, null);
                afterFlush.BeforeReturn = () => SetSessionRevokedAsync(connection, session.Id, Now);
                Assert.Equal(ProjectDraftRenameSaveState.Unavailable, (await service.SaveAsync(command)).State);
                Assert.Equal(1, await CandidateCount(connection));
                Assert.Empty(await seed.IdempotencyRecords.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.AuditLogs.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.OutboxEvents.AsNoTracking().ToListAsync());
                await SetSessionRevokedAsync(connection, session.Id, null);

                afterFlush.BeforeReturn = () =>
                {
                    materializations.CurrentHead = new(draft.TargetBranch, RevisionId.New());
                    return Task.CompletedTask;
                };
                Assert.Equal(ProjectDraftRenameSaveState.Unavailable, (await service.SaveAsync(command)).State);
                Assert.Equal(1, await CandidateCount(connection));
                Assert.Empty(await seed.IdempotencyRecords.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.AuditLogs.AsNoTracking().ToListAsync());
                Assert.Empty(await seed.OutboxEvents.AsNoTracking().ToListAsync());
                materializations.CurrentHead = draft.CapturedTargetHead;

                var concurrentClaim = new ClaimAttempt();
                await using var retryDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(connection).AddInterceptors(concurrentClaim).Options, currentTenant);
                var retryService = CreateService(retryDb, new FixtureOutbox(retryDb) { Accept = true });
                var firstWriteStaged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                outbox.BeforeReturn = async () =>
                {
                    firstWriteStaged.SetResult();
                    await concurrentClaim.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
                };

                var first = service.SaveAsync(command);
                var firstCompletion = await Task.WhenAny(first, firstWriteStaged.Task).WaitAsync(TimeSpan.FromSeconds(15));
                if (ReferenceEquals(firstCompletion, first))
                    Assert.Equal(ProjectDraftRenameSaveState.Saved, (await first).State);
                Assert.True(firstWriteStaged.Task.IsCompletedSuccessfully);
                var results = await Task.WhenAll(first, retryService.SaveAsync(command));
                outbox.BeforeReturn = null;

                var saved = results[0];
                Assert.Equal(ProjectDraftRenameSaveState.Saved, saved.State);
                Assert.Equal(saved, results[1]);
                Assert.Equal(saved, await service.SaveAsync(command));
                var changedPayload = command with { Request = request with { DisplayName = "Different private name" } };
                Assert.Equal(ProjectDraftRenameSaveState.RequestMismatch, (await retryService.SaveAsync(changedPayload)).State);
                Assert.Equal(ProjectDraftRenameSaveState.Unavailable,
                    (await service.SaveAsync(command with { RequestIdentity = "fresh-key-stale-draft" })).State);
                Assert.Equal(2, await CandidateCount(connection));
                Assert.Single(await seed.IdempotencyRecords.AsNoTracking().ToListAsync());
                var audit = Assert.Single(await seed.AuditLogs.AsNoTracking().ToListAsync());
                Assert.Equal("ProjectDraftRenamed", audit.Action);
                Assert.DoesNotContain(request.DisplayName, audit.Summary);
                Assert.Null(audit.EntityId);
                Assert.Null(audit.ProjectId);
                Assert.Single(await seed.OutboxEvents.AsNoTracking().ToListAsync());

                await SetSessionRevokedAsync(connection, session.Id, Now);
                Assert.Equal(ProjectDraftRenameSaveState.Unavailable, (await service.SaveAsync(command)).State);
                Assert.Equal(2, await CandidateCount(connection));
            });

    private static Task<long> CandidateCount(string database) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
            "SELECT count(*) FROM projectide_development.project_ide_draft_candidates");

    private static async Task SetSessionRevokedAsync(string database, Guid sessionId, DateTimeOffset? revokedAt)
    {
        await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var session = await writer.Sessions.SingleAsync(item => item.Id == sessionId);
        session.RevokedAt = revokedAt;
        await writer.SaveChangesAsync();
    }

    private sealed class FixtureAuthority(ProposalContext draft, ContentDigest sourceDigest) : IProjectIdeOperationAuthorization
    {
        public ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
            SecuritySourceIdentitySnapshot source, string digest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(source.ProposalId == draft.ProposalId.Value && source.BranchId == draft.TargetBranch.BranchId.Value &&
                digest == sourceDigest.Value && operation.OperationId is "coglatas.draft.rename.preview" or "coglatas.draft.rename.apply");
    }

    private sealed class FixtureMaterializations(RevisionRef head) : IProjectDraftMaterializationReader
    {
        public RevisionRef CurrentHead { get; set; } = head;

        public Task<IReadOnlyList<CompilerMaterializedSnapshot>?> ReadAsync(ProjectIdeViewerContext viewer,
            CompilerInputManifest input, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompilerMaterializedSnapshot>?>([]);

        public Task<RevisionRef?> ReadCurrentHeadAsync(ProjectIdeViewerContext viewer, BranchRef branch,
            CancellationToken cancellationToken) => Task.FromResult<RevisionRef?>(CurrentHead);
    }

    private sealed class AfterFlush : SaveChangesInterceptor
    {
        public Func<Task>? BeforeReturn { get; set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (BeforeReturn is { } callback && eventData.Context?.ChangeTracker.Entries<OutboxEvent>().Any() == true)
            {
                BeforeReturn = null;
                await callback();
            }
            return result;
        }
    }

    private sealed class ClaimAttempt : SaveChangesInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<IdempotencyRecord>()
                .Any(entry => entry.State == EntityState.Added) == true)
                Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixtureOutbox(AppDbContext db) : IProjectDraftRenameOutbox
    {
        public bool Accept { get; set; }
        public CancellationTokenSource? CancelDuringStage { get; set; }
        public Func<Task>? BeforeReturn { get; set; }

        public async Task<bool> StageAsync(ProjectIdeViewerContext viewer, ProposalContext draft, CancellationToken cancellationToken)
        {
            db.OutboxEvents.Add(new(Guid.NewGuid())
            {
                TenantId = viewer.TenantId, EventType = "Fixture.DraftChanged.v1", PayloadSchemaVersion = 1,
                AggregateType = "Fixture", AggregateId = draft.CandidateRevisionId.Value,
                Status = OutboxEventStatus.Pending, OccurredAt = Now, NextAttemptAt = Now,
                PayloadJson = "{}", RoutingJson = "[]"
            });
            var cancellation = CancelDuringStage;
            CancelDuringStage = null;
            cancellation?.Cancel();
            if (BeforeReturn is { } callback)
                await callback();
            return Accept;
        }
    }

    private sealed class FixtureAudit(IAuditLogger audit) : IProjectDraftRenameAudit
    {
        public Task StageAsync(ProjectIdeViewerContext viewer, CancellationToken cancellationToken) =>
            audit.LogAsync(new AuditLogEntry(viewer.UserId, "ProjectDraftRenamed", "ProjectDraft", null,
                "A Project Draft display name changed.", TenantId: viewer.TenantId), cancellationToken);
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
