using System.Text.Json.Nodes;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectDraftRenameRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyRecomputesTheConfirmedPlanAndBindsTheRetryToTheName()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.Request;
        var preview = Assert.IsType<ProjectDraftRenamePreview>(await fixture.Service.PreviewAsync(request));
        var command = new ProjectDraftRenameConfirmation(request, preview.NextCandidate, preview.PreviewDigest, "sample-operation");

        var unconfirmed = await fixture.Service.SaveAsync(command with { NextCandidate = CandidateRevisionId.New() });
        Assert.Equal(ProjectDraftRenameSaveState.Unavailable, unconfirmed.State);
        Assert.Empty(await fixture.Database.IdempotencyRecords.ToArrayAsync());
        Assert.Null(fixture.Drafts.Staged);

        var result = await fixture.Service.SaveAsync(command);
        Assert.Equal(ProjectDraftRenameSaveState.Saved, result.State);
        Assert.Equal(preview.NextCandidate, result.Saved?.Candidate);
        Assert.Equal(request.DisplayName, fixture.Drafts.Staged?.NewName);
        Assert.Single(await fixture.Database.IdempotencyRecords.ToArrayAsync());

        Assert.Equal(result, await fixture.Service.SaveAsync(command));
        Assert.Equal(1, fixture.Drafts.Stages);
        var modifiedName = command with { Request = request with { DisplayName = "Different display name" } };
        Assert.Equal(ProjectDraftRenameSaveState.RequestMismatch, (await fixture.Service.SaveAsync(modifiedName)).State);
        Assert.Equal(1, fixture.Drafts.Stages);
    }

    [Fact]
    public async Task RevocationBetweenPreviewAndApplyRefusesBeforeLoadingTheDraft()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = Assert.IsType<ProjectDraftRenamePreview>(await fixture.Service.PreviewAsync(fixture.Request));
        fixture.Authority.ApplyAllowed = false;
        var confirmation = new ProjectDraftRenameConfirmation(fixture.Request, preview.NextCandidate, preview.PreviewDigest, "revoked-key");

        var refused = await fixture.Service.SaveAsync(confirmation);

        Assert.Equal(ProjectDraftRenameSaveState.Unavailable, refused.State);
        Assert.Null(refused.Saved);
        Assert.Equal(1, fixture.Drafts.Reads);
        Assert.Null(fixture.Drafts.Staged);
        Assert.Empty(await fixture.Database.IdempotencyRecords.ToArrayAsync());
    }

    [Fact]
    public async Task BoundedLocationAndDisplayNameReturnARefusalWhenTheConfirmedPlanDiffers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.Request with { DisplayName = new string('x', ProjectDraftRename.MaximumNameBytes) };
        var preview = Assert.IsType<ProjectDraftRenamePreview>(await fixture.Service.PreviewAsync(request));
        var location = JsonNode.Parse(request.Target.Data.CanonicalText)!.AsObject();
        location["fixture-extension"] = new string('a', new SourceCodecLimits().MaximumBytes - 2048);
        var changed = request with { Target = ProjectLocation.Parse(SourceJson.Parse(location.ToJsonString())) };
        var confirmation = new ProjectDraftRenameConfirmation(changed, preview.NextCandidate, preview.PreviewDigest, "bounded-location-key");

        Assert.Equal(ProjectDraftRenameSaveState.Unavailable, (await fixture.Service.SaveAsync(confirmation)).State);
        Assert.Null(fixture.Drafts.Staged);
        Assert.Empty(await fixture.Database.IdempotencyRecords.ToArrayAsync());
    }

    private sealed class Fixture(AppDbContext database, MemoryDraft draft, Policy authority,
        ProjectDraftRenameService service, ProjectDraftRenameRequest request) : IAsyncDisposable
    {
        public AppDbContext Database { get; } = database;
        public MemoryDraft Drafts { get; } = draft;
        public Policy Authority { get; } = authority;
        public ProjectDraftRenameService Service { get; } = service;
        public ProjectDraftRenameRequest Request { get; } = request;

        public static async Task<Fixture> CreateAsync()
        {
            var input = DraftRenameTests.Draft();
            var tenant = new Tenant(input.Source.Context.Branch.TenantId.Value);
            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(tenant.Id, "draft-runtime-test");
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new AppDbContext(options, currentTenant);
            var user = new User();
            var session = new Session { UserId = user.Id, ExpiresAt = Now.AddHours(1) };
            var membership = new TenantUser { TenantId = tenant.Id, UserId = user.Id, Status = TenantUserStatus.Active };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            db.AddRange(user, session, membership);
            await db.SaveChangesAsync();
            var viewers = new ProjectIdeViewerContextReader(new Viewer(user.Id, session.Id), currentTenant,
                new SessionRepository(db), new TenantRepository(db), new Clock());
            var policy = new Policy();
            var drafts = new MemoryDraft(ProjectSemanticBinder.Bind(input));
            var service = new ProjectDraftRenameService(new ProjectIdeOperationAccess(viewers, policy),
                drafts, new InMemoryCoordinator(new EfCreateIdempotencyCoordinator(db)));
            return new(db, drafts, policy, service, new(DraftRenameTests.Target(input), input.Source.Digest, input.InputDigest, "Renamed"));
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class MemoryDraft(PartialSemanticModel model) : IProjectDraftRenamePersistence
    {
        public int Reads { get; private set; }
        public int Stages { get; private set; }
        public ProjectDraftRenamePlan? Staged { get; private set; }

        public void EnsureIsolatedWriteScope() { }

        public Task<bool> IsTargetCurrentAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<PartialSemanticModel?> LoadCurrentAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
            bool forUpdate, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<PartialSemanticModel?>(model);
        }

        public Task<bool> StageRenameAsync(ProjectIdeViewerContext viewer, ProjectDraftRenamePlan plan,
            CancellationToken cancellationToken)
        {
            Stages++;
            Staged = plan;
            return Task.FromResult(true);
        }

        public Task<ProjectDraftRenameSaved?> LoadSavedAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
            CandidateRevisionId candidate, CancellationToken cancellationToken) =>
            Task.FromResult<ProjectDraftRenameSaved?>(Staged is { } plan
                ? new(candidate, plan.After.Source.Digest, plan.After.InputDigest) : null);
    }

    private sealed class InMemoryCoordinator(EfCreateIdempotencyCoordinator inner) : ICreateIdempotencyCoordinator
    {
        public Task<IdempotentCreateResult<T>> ExecuteAsync<T>(CreateIdempotencyContext context,
            Func<CancellationToken, Task<T>> stageCreation, Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
            CancellationToken cancellationToken = default) where T : class =>
            inner.ExecuteAsync(context, stageCreation, loadCommittedResource, cancellationToken);

        public Task<IdempotentCreateResult<T>> ExecuteWithCommitCheckAsync<T>(CreateIdempotencyContext context,
            Func<CancellationToken, Task<T>> stageCreation, Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
            Func<CancellationToken, Task> validateBeforeCommit, CancellationToken cancellationToken = default) where T : class =>
            inner.ExecuteAsync(context, async token =>
            {
                var result = await stageCreation(token);
                await validateBeforeCommit(token);
                return result;
            }, loadCommittedResource, cancellationToken);
    }

    private sealed class Policy : IProjectIdeOperationAuthorization
    {
        public bool ApplyAllowed { get; set; } = true;
        public ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
            SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(operation.OperationId == "coglatas.draft.rename.preview" || ApplyAllowed);
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
