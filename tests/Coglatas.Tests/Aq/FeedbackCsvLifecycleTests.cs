using System.Text;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Application.Security.Redaction;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Aq;

public sealed class FeedbackCsvLifecycleTests
{
    [Fact]
    public async Task RequestGrantAndDownloadReauthorizeAndReplayOnlyOriginalReceipts()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var input = new FeedbackCsvRequest("detail", ["questionLabel", "answerValue"]);

        var first = await service.RequestAsync(fixture.Scope.Id, input, "request-key", "request-1", default);
        var replay = await service.RequestAsync(fixture.Scope.Id, input, "request-key", "request-2", default);
        var conflict = await service.RequestAsync(fixture.Scope.Id,
            new FeedbackCsvRequest("detail", ["questionLabel"]), "request-key", "request-3", default);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal("IdempotencyConflict", conflict.ErrorDetail!.Code);
        Assert.Single(fixture.Requests);
        Assert.Equal("queued", first.Value!.Data.State);

        var id = first.Value.Data.ExportRequestId;
        Assert.Equal("ready", (await service.StatusAsync(id, default)).Value!.State);
        var grant = await service.GrantAsync(id, "download-key", "grant-1", default);
        var grantReplay = await service.GrantAsync(id, "download-key", "grant-2", default);
        Assert.True(grant.IsSuccess);
        Assert.Equal(grant.Value, grantReplay.Value);
        Assert.Single(fixture.Grants);

        var file = await service.DownloadAsync(id, grant.Value!.Data.GrantId, default);
        Assert.True(file.IsSuccess);
        Assert.Contains("visible feedback", Encoding.UTF8.GetString(file.Value!.Content));
        Assert.Contains(fixture.Audits, item => item.Action == "SurveyFeedbackCsvDeliveryAuthorized");

        fixture.Scope = fixture.Scope with { ResultsStopped = true };
        var stopped = await service.DownloadAsync(id, grant.Value.Data.GrantId, default);
        Assert.Equal("NotFound", stopped.ErrorDetail!.Code);
        Assert.Null(stopped.Value);
    }

    [Fact]
    public async Task DeniesNamesGrantOnlyActorsAndSessionLossBeforePersisting()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var identity = await service.RequestAsync(fixture.Scope.Id, new("detail", ["respondentName"]),
            "identity-key", "request-1", default);
        Assert.Equal("RestrictedFieldDenied", identity.ErrorDetail!.Code);
        Assert.Equal(0, fixture.SourceReads);

        fixture.Reviewer = false;
        fixture.GrantOnly = true;
        var grantOnly = await service.RequestAsync(fixture.Scope.Id, new("detail", ["answerValue"]),
            "grant-only-key", "request-2", default);
        Assert.Equal("CapabilityDenied", grantOnly.ErrorDetail!.Code);
        Assert.Equal(0, fixture.SourceReads);

        fixture.GrantOnly = false;
        fixture.Reviewer = true;
        fixture.OnRead = () => fixture.Session.RevokedAt = fixture.UtcNow;
        var revoked = await service.RequestAsync(fixture.Scope.Id, new("detail", ["answerValue"]),
            "revoked-key", "request-3", default);
        Assert.Equal("AuthenticationRequired", revoked.ErrorDetail!.Code);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task StaleVisibilityAndWrongGrantDenyBytes()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var admitted = await service.RequestAsync(fixture.Scope.Id, new("detail", ["questionKey"]),
            "request-key", "request-1", default);
        var id = admitted.Value!.Data.ExportRequestId;
        var grant = await service.GrantAsync(id, "download-key", "grant-1", default);
        Assert.True(grant.IsSuccess);

        var wrongGrant = await service.DownloadAsync(id, Guid.NewGuid(), default);
        Assert.Equal("NotFound", wrongGrant.ErrorDetail!.Code);
        var current = Assert.Single(fixture.Snapshot.Answers);
        fixture.Snapshot = fixture.Snapshot with { Answers = [current with { VisibilityVersionNo = 3 }] };

        var status = await service.StatusAsync(id, default);
        Assert.Equal("revoked", status.Value!.State);
        var stale = await service.DownloadAsync(id, grant.Value!.Data.GrantId, default);
        Assert.Equal("ExportExpired", stale.ErrorDetail!.Code);
        Assert.Null(stale.Value);
    }

    private sealed class Fixture : ICurrentTenant, ICurrentUser, IClock, IProjectAuthorizationService,
        ITenantRepository, IWorkspaceRepository, ICapabilityGrantEvaluator, ISurveyFeedbackRepository, IFeedbackCsvRepository,
        ISessionRepository, ICreateIdempotencyCoordinator, IUnitOfWork, IAuditLogger
    {
        private readonly Dictionary<(string Operation, string Key), CreateIdempotencyContext> claims = [];
        private readonly Guid requester = Guid.NewGuid();
        private readonly Guid issuer = Guid.NewGuid();

        public Fixture()
        {
            Scope = new PresentationFeedbackScope(TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), "Synthetic presentation", UtcNow, UtcNow.AddHours(1), null, false);
            Session = new Session { UserId = requester, ExpiresAt = UtcNow.AddDays(1), User = new User() };
            var submission = new FeedbackCsvSubmission(Guid.NewGuid(), UtcNow, 1);
            Snapshot = new FeedbackCsvSnapshot(1, 1, 1, [], [submission],
                [new FeedbackCsvAnswer(Guid.NewGuid(), submission.Id, Guid.NewGuid(), "comment", "Comment",
                    SurveyFieldType.Text, "[]", "\"visible feedback\"", 1, false)]);
        }

        public PresentationFeedbackScope Scope { get; set; }
        public FeedbackCsvSnapshot Snapshot { get; set; }
        public Session Session { get; }
        public bool Reviewer { get; set; } = true;
        public bool GrantOnly { get; set; }
        public int SourceReads { get; private set; }
        public Action? OnRead { get; set; }
        public List<AuditLogEntry> Audits { get; } = [];
        public List<SurveyFeedbackExportRequest> Requests { get; } = [];
        public List<SurveyFeedbackExportGrant> Grants { get; } = [];

        public Guid TenantId { get; } = Guid.NewGuid();
        public bool IsAvailable => true;
        public bool IsPlatformScope => false;
        public string TenantSlug => "fixture";
        public Guid? UserId => requester;
        public Guid? SessionId => Session.Id;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UnixEpoch.AddYears(50);

        public FeedbackCsvService CreateService() =>
            new(new SurveyFeedbackAuthorization(this, this, this, this, this, this, this, this),
                this, this, this, this, this, this, new CanonicalRedactionService(), this);

        public Task<TenantUser?> GetTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<TenantUser?>(tenantId == TenantId
                ? new TenantUser
                {
                    TenantId = TenantId,
                    UserId = userId,
                    Tenant = new Tenant(TenantId),
                    User = new User(),
                    Status = TenantUserStatus.Active
                }
                : null);
        public Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(projectId == Scope.ProjectId);
        public Task<bool> HasActiveGrantAsync(Guid subjectUserId, Guid tenantId, string capabilityKey,
            CapabilityScopeType scopeType, Guid? scopeId, CancellationToken cancellationToken) =>
            Task.FromResult(capabilityKey == "feedback.export.detail" && subjectUserId == requester ||
                capabilityKey == "feedback.invite.create" && subjectUserId == issuer);
        public Task<PresentationFeedbackScope?> GetScopeAsync(Guid tenantId, Guid presentationId, CancellationToken cancellationToken) =>
            Task.FromResult(tenantId == TenantId && presentationId == Scope.Id ? Scope : null);
        public Task<(bool Presenter, bool Reviewer)> GetAssignmentsAsync(
            Guid tenantId, Guid presentationId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult((userId == issuer, userId == requester && Reviewer));
        public Task<IReadOnlyList<Guid>> GetActiveGrantIssuersAsync(
            Guid tenantId, Guid presentationId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(GrantOnly ? [issuer] : []);
        public Task<Session?> GetByIdWithUserAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<Session?>(sessionId == Session.Id ? Session : null);

        public Task<Result<FeedbackCsvSnapshot>> ReadVisibleAsync(PresentationFeedbackScope scope, int maximum,
            CancellationToken cancellationToken)
        {
            SourceReads++;
            OnRead?.Invoke();
            return Task.FromResult(Result<FeedbackCsvSnapshot>.Success(Snapshot));
        }

        public Task<SurveyFeedbackExportRequest?> FindRequestAsync(Guid tenantId, Guid requesterId, Guid requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Requests.SingleOrDefault(item => item.TenantId == tenantId &&
                item.RequesterId == requesterId && item.Id == requestId));

        public Task<SurveyFeedbackExportGrant?> FindGrantAsync(Guid tenantId, Guid requesterId, Guid exportRequestId,
            Guid grantId, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.SingleOrDefault(item => item.TenantId == tenantId &&
                item.RecipientUserId == requesterId && item.ExportRequestId == exportRequestId && item.Id == grantId));

        public void AddRequest(SurveyFeedbackExportRequest request) => Requests.Add(request);
        public void AddGrant(SurveyFeedbackExportGrant grant) => Grants.Add(grant);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);

        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Audits.Add(entry);
            return Task.CompletedTask;
        }

        public async Task<IdempotentCreateResult<T>> ExecuteAsync<T>(CreateIdempotencyContext context,
            Func<CancellationToken, Task<T>> stageCreation, Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
            CancellationToken cancellationToken = default) where T : class
        {
            var identity = (context.Operation, context.ClientRequestIdentity);
            if (claims.TryGetValue(identity, out var previous))
            {
                if (context.RequestHash != previous.RequestHash)
                    return new(IdempotentCreateDisposition.RequestMismatch, null);

                return new(IdempotentCreateDisposition.Replayed,
                    await loadCommittedResource(previous.ResourceId, cancellationToken));
            }

            var created = await stageCreation(cancellationToken);
            claims.Add(identity, context);
            return new(IdempotentCreateDisposition.Created, created);
        }

        public Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Tenant?> GetTenantByPrimaryDomainAsync(string primaryDomain, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddTenantAsync(Tenant tenant, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantUser>> ListTenantUsersAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantUser>> ListUserTenantMembershipsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddTenantUserAsync(TenantUser tenantUser, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Workspace>> ListForUserAsync(Guid userId, bool includeAll, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Workspace?> GetByIdAsync(Guid workspaceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<WorkspaceMember?> GetMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorkspaceMember>> ListMembersAsync(Guid workspaceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task AddAsync(Workspace workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddMemberAsync(WorkspaceMember member, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsInvitationEnabledAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<FeedbackEventScope?> GetEventScopeAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PresentationFeedbackScope>> GetCandidateScopesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SurveyFeedbackSnapshot>> ReadAsync(Guid tenantId, Guid presentationId, string? fieldKey,
            string visibility, FeedbackPagePosition? after, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FeedbackSummaryInput> ReadSummaryAsync(Guid tenantId, Guid presentationId, string? fieldKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(Session session, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RevokeAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RevokeUserSessionsAsync(Guid userId, DateTimeOffset revokedAt, Guid? exceptSessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
