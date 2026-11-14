using Coglatas.Application.Artifacts;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Enums;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Coglatas.Tests.Artifacts;

internal sealed class WorkspaceArtifactReviewFixture
{
    internal readonly CurrentTenantService Tenant = new();
    internal readonly ReviewUser User = new();
    internal readonly ReviewClock Clock = new();
    internal readonly ReviewRepository Repository = new();
    internal readonly ReviewAuthorization Authorization = new();
    internal readonly EphemeralDataProtectionProvider Keys = new();
    internal WorkspaceArtifactCursorCodec Codec { get; }
    internal WorkspaceArtifactService Service { get; }

    internal WorkspaceArtifactReviewFixture()
    {
        Tenant.SetTenant(Guid.NewGuid(), "artifact-review");
        Codec = new(Keys);
        Service = new(Repository, Codec, Authorization, Tenant, User, Clock);
    }

    internal Task<Result<WorkspaceArtifactPageResponse>> ListAsync(
        WorkspaceArtifactListRequest? request = null) => Service.ListAsync(Authorization.WorkspaceId, request ?? new());

    internal WorkspaceArtifactCursor Cursor() => new(
        1, Tenant.TenantId, User.UserId!.Value, Authorization.WorkspaceId, "", "updatedAt", "desc", 20,
        new(Clock.UtcNow.AddMinutes(-1), Guid.NewGuid()), Clock.UtcNow.AddMinutes(15));

    internal ArtifactListItemResponse Item(int number) => new(
        Guid.Parse($"00000000-0000-0000-0000-{number:x12}"), Guid.NewGuid(), $"Artifact {number}", null,
        ArtifactType.Document, ArtifactStatus.Draft, null, User.UserId!.Value,
        Clock.UtcNow.AddMinutes(-number), null);

    internal sealed class ReviewUser : ICurrentUser
    {
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public Guid? SessionId => null;
        public string Email => "artifact-review@example.test";
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated { get; set; } = true;
    }

    internal sealed class ReviewClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    }

    internal sealed class ReviewRepository : IWorkspaceArtifactRepository
    {
        internal IReadOnlyList<ArtifactListItemResponse> Rows { get; set; } = [];
        internal WorkspaceArtifactReadQuery? LastQuery { get; private set; }
        internal Action? DuringRead { get; set; }
        internal int Calls { get; private set; }
        public Task<IReadOnlyList<ArtifactListItemResponse>> ReadAsync(WorkspaceArtifactReadQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            DuringRead?.Invoke();
            return Task.FromResult<IReadOnlyList<ArtifactListItemResponse>>(Rows.Take(query.PageSize + 1).ToArray());
        }
    }

    internal sealed class ReviewAuthorization : IWorkspaceAuthorizationService
    {
        internal Guid WorkspaceId { get; } = Guid.NewGuid();
        internal bool Allowed { get; set; } = true;
        public Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(Allowed && workspaceId == WorkspaceId);
        public Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
