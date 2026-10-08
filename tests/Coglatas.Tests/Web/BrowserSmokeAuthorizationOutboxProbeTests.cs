using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.Web;

public sealed class BrowserSmokeAuthorizationOutboxProbeTests
{
    [Theory]
    [InlineData("Test", true, true)]
    [InlineData("Test", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Development", true, false)]
    [InlineData("Staging", true, false)]
    public async Task ProbeIsMappedOnlyWithExplicitTestOptInAndRequiresAuthorization(
        string environmentName, bool requested, bool mapped)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        builder.Services.AddSingleton<ICurrentTenant>(new CurrentTenantService());
        builder.Services.AddSingleton<ICurrentUser>(new Actor(Guid.NewGuid()));
        await using var app = builder.Build();
        BrowserSmokeAuthorizationOutboxProbe.MapEndpoint(app, environmentName, requested);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
        if (!mapped)
        {
            Assert.Empty(endpoints);
            return;
        }
        var endpoint = Assert.IsType<RouteEndpoint>(Assert.Single(endpoints));
        Assert.Equal(BrowserSmokeAuthorizationOutboxProbe.Path, endpoint.RoutePattern.RawText);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
    }

    [Theory]
    [InlineData(OutboxEventStatus.Pending, false)]
    [InlineData(OutboxEventStatus.Processing, false)]
    [InlineData(OutboxEventStatus.RetryScheduled, false)]
    [InlineData(OutboxEventStatus.DeadLetter, false)]
    [InlineData(OutboxEventStatus.Cancelled, false)]
    [InlineData(OutboxEventStatus.Delivered, true)]
    public async Task OnlyCompletedDispatchQualifiesIncludingNoConnectedRecipient(
        OutboxEventStatus status, bool expected)
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        var tenantId = Guid.NewGuid();
        var actor = new Actor(Guid.NewGuid());
        await using var db = CreateDb(tenant);
        var row = Event(tenantId, actor.UserId!.Value, status);
        row.LastErrorCode = "NoAuthorizedRecipient";
        db.OutboxEvents.Add(row);
        await db.SaveChangesAsync();
        tenant.SetTenant(tenantId, "default");
        var snapshot = await BrowserSmokeAuthorizationOutboxProbe.GetSnapshotAsync(db, tenant, actor);
        Assert.NotNull(snapshot);
        Assert.Equal(expected, snapshot.IsSettled);
        Assert.DoesNotContain("must-not-return", JsonSerializer.Serialize(snapshot));
        Assert.Equal(status, row.Status);
    }

    [Fact]
    public async Task ProbeIgnoresForeignTenantsActorsAndUnrelatedEventsWithoutReturningEvidence()
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        var tenantId = Guid.NewGuid();
        var actor = new Actor(Guid.NewGuid());
        await using var db = CreateDb(tenant);
        var foreignActor = Event(tenantId, Guid.NewGuid(), OutboxEventStatus.Pending);
        var foreignTenant = Event(Guid.NewGuid(), actor.UserId!.Value, OutboxEventStatus.Pending);
        var unrelated = Event(tenantId, actor.UserId.Value, OutboxEventStatus.Pending);
        unrelated.EventType = "Messaging.MessageCreated.v1";
        var wrongAggregate = Event(tenantId, actor.UserId.Value, OutboxEventStatus.Pending);
        wrongAggregate.AggregateType = "Notification";
        db.OutboxEvents.AddRange(foreignActor, foreignTenant, unrelated, wrongAggregate);
        await db.SaveChangesAsync();
        tenant.SetTenant(tenantId, "default");
        var snapshot = await BrowserSmokeAuthorizationOutboxProbe.GetSnapshotAsync(db, tenant, actor);
        Assert.Equal(new BrowserSmokeAuthorizationOutboxSnapshot(true), snapshot);
        Assert.Equal("{\"IsSettled\":true}", JsonSerializer.Serialize(snapshot));
        Assert.All(db.ChangeTracker.Entries<OutboxEvent>(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Theory]
    [InlineData(false, "owner@example.test", "tenant")]
    [InlineData(true, "owner@example.com", "tenant")]
    [InlineData(true, null, "tenant")]
    [InlineData(true, "owner@example.test", "missing")]
    [InlineData(true, "owner@example.test", "platform")]
    public async Task ProbeRejectsUnauthenticatedNonsyntheticAndUnavailableOrPlatformContexts(
        bool authenticated, string? email, string scope)
    {
        var tenant = new CurrentTenantService();
        if (scope == "tenant") tenant.SetTenant(Guid.NewGuid(), "default");
        if (scope == "platform") tenant.SetPlatformScope();
        await using var db = CreateDb(tenant);
        Assert.Null(await BrowserSmokeAuthorizationOutboxProbe.GetSnapshotAsync(
            db, tenant, new Actor(Guid.NewGuid(), authenticated, email)));
        Assert.Null(await BrowserSmokeAuthorizationOutboxProbe.GetSnapshotAsync(
            db, tenant, new Actor(Guid.Empty, authenticated, email)));
    }

    private static AppDbContext CreateDb(CurrentTenantService tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);

    private static OutboxEvent Event(Guid tenantId, Guid actorUserId, OutboxEventStatus status) => new(Guid.NewGuid())
    {
        TenantId = tenantId, AggregateId = actorUserId, AggregateType = "AuthorizationState",
        EventType = "Security.AuthorizationStateChanged.v1", PayloadSchemaVersion = 1,
        Status = status, PayloadJson = "{\"syntheticPrivate\":\"must-not-return\"}", RoutingJson = "[]"
    };

    private sealed record Actor(Guid? UserId, bool IsAuthenticated = true, string? Email = "owner@example.test") : ICurrentUser
    {
        Guid? ICurrentUser.SessionId => null;
        SystemRole? ICurrentUser.SystemRole => null;
    }
}
