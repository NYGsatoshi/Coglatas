using System.Text.Json;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Workspaces;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Coglatas.Tests.Artifacts;

public sealed class WorkspaceArtifactWave2Tests
{
    [Theory]
    [InlineData("actor", 1)]
    [InlineData("tenant", 1)]
    [InlineData("anonymous", 1)]
    [InlineData("platform", 1)]
    [InlineData("actor", 2)]
    [InlineData("tenant", 2)]
    [InlineData("anonymous", 2)]
    [InlineData("platform", 2)]
    public async Task ContextChangeInsideAwaitedAuthorizationNeverPublishesMetadata(string change, int atCall)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        fixture.Repository.Rows = [fixture.Item(1), fixture.Item(2)];
        var authorization = new AwaitedAuthorization(call =>
        {
            if (call != atCall) return;
            switch (change)
            {
                case "actor": fixture.User.UserId = Guid.NewGuid(); break;
                case "tenant": fixture.Tenant.SetTenant(Guid.NewGuid(), "changed-synthetic"); break;
                case "anonymous": fixture.User.IsAuthenticated = false; break;
                case "platform": fixture.Tenant.SetPlatformScope(); break;
            }
        });
        var service = new WorkspaceArtifactService(fixture.Repository, fixture.Codec, authorization,
            fixture.Tenant, fixture.User, fixture.Clock);
        var result = await service.ListAsync(fixture.Authorization.WorkspaceId, new(PageSize: 1));
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("WorkspaceNotFound", result.ErrorDetail!.Code);
        Assert.Equal(atCall == 1 ? 0 : 1, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticatedCursorWithNullOrMissingPositionIsRejected(bool omitPosition)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var node = JsonSerializer.SerializeToNode(fixture.Cursor())!.AsObject();
        if (omitPosition) node.Remove("After"); else node["After"] = null;
        var token = fixture.Keys.CreateProtector("Coglatas.WorkspaceArtifacts.Cursor.v1").Protect(node.ToJsonString());
        var result = await fixture.ListAsync(new(Cursor: token));
        Assert.Equal("ArtifactCursorInvalid", result.ErrorDetail!.Code);
        Assert.Null(result.Value);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public void SameKeyProviderMaintainsCursorContinuityAcrossCodecInstances()
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var token = fixture.Codec.Encode(fixture.Cursor());
        var replacement = new WorkspaceArtifactCursorCodec(fixture.Keys);
        Assert.True(replacement.TryDecode(token, out var decoded));
        Assert.Equal(fixture.Cursor().TenantId, decoded!.TenantId);
    }

    private sealed class AwaitedAuthorization(Action<int> duringAuthorization) : IWorkspaceAuthorizationService
    {
        private int _calls;
        public async Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            duringAuthorization(++_calls);
            return true;
        }
        public Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
