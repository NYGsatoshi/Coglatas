using Coglatas.Web.Testing;
using Microsoft.AspNetCore.Http;

namespace Coglatas.Tests.Web;

public sealed class BrowserSmokeResponseGateTests
{
    [Theory]
    [InlineData("Test", true)]
    [InlineData("test", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void BrowserSmokeFeaturesAreRestrictedToTheTestEnvironment(
        string environmentName,
        bool expected)
    {
        Assert.Equal(expected, BrowserSmokeTestBoundary.IsEnabled(environmentName, requested: true));
    }

    [Fact]
    public void BrowserSmokeFeaturesRemainDisabledWithoutExplicitOptIn()
    {
        Assert.False(BrowserSmokeTestBoundary.IsEnabled("Test", requested: false));
    }

    [Fact]
    public void TargetValidationAllowsOnlyCanonicalProjectKanbanAndGanttGets()
    {
        var projectId = Guid.NewGuid();

        Assert.True(BrowserSmokeResponseGateRegistry.IsAllowedTarget(
            HttpMethods.Get,
            $"/api/projects/{projectId}/kanban"));
        Assert.True(BrowserSmokeResponseGateRegistry.IsAllowedTarget(
            HttpMethods.Get,
            $"/api/projects/{projectId}/gantt"));
        Assert.False(BrowserSmokeResponseGateRegistry.IsAllowedTarget(
            HttpMethods.Post,
            $"/api/projects/{projectId}/kanban"));
        Assert.False(BrowserSmokeResponseGateRegistry.IsAllowedTarget(
            HttpMethods.Patch,
            $"/api/projects/{projectId}/gantt"));
        Assert.False(BrowserSmokeResponseGateRegistry.IsAllowedTarget(
            HttpMethods.Get,
            $"/api/projects/{projectId}/kanban/config"));
        Assert.False(BrowserSmokeResponseGateRegistry.IsAllowedTarget(
            HttpMethods.Get,
            "/internal/browser-smoke/response-gates/example"));
    }

    [Fact]
    public async Task GateHoldsOneAuthorizedResponseUntilItsOwnerReleasesIt()
    {
        var registry = new BrowserSmokeResponseGateRegistry();
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var path = $"/api/projects/{projectId}/kanban";
        Assert.True(registry.TryArm(gateId, ownerUserId, HttpMethods.Get, path));
        Assert.False(registry.TryArm(Guid.NewGuid(), ownerUserId, HttpMethods.Get, path));
        Assert.True(registry.TryClaim(
            gateId,
            ownerUserId,
            HttpMethods.Get,
            path,
            out var lease));
        Assert.False(registry.TryClaim(
            gateId,
            ownerUserId,
            HttpMethods.Get,
            path,
            out _));
        lease!.MarkResponseReady(StatusCodes.Status200OK);
        var responseDelivery = lease.WaitForReleaseAsync(CancellationToken.None);
        Assert.Equal(
            new BrowserSmokeResponseGateSnapshot("waiting", StatusCodes.Status200OK),
            registry.GetSnapshot(gateId, ownerUserId));
        Assert.False(responseDelivery.IsCompleted);
        Assert.False(registry.TryRelease(gateId, otherUserId));
        Assert.True(registry.TryRelease(gateId, ownerUserId));
        await responseDelivery.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(registry.GetSnapshot(gateId, ownerUserId));
    }
}
