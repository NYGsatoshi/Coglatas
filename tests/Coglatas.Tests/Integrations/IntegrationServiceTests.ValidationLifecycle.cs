using Coglatas.Application.Integrations;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Integrations;

public sealed partial class IntegrationServiceTests
{
    [Theory]
    [InlineData(" ", null, "Integration display name is required.")]
    [InlineData("Invalid\0name", "{}", "Integration display name must not contain null characters.")]
    [InlineData("Connector", "{", "Integration settings must contain valid JSON.")]
    [InlineData("Connector", "{\"PASSWORD\":null}", "Integration settings must not contain raw secrets, tokens, passwords, or API keys.")]
    [InlineData("Connector", "{\"nested\":[{\"apiKEY\":null}]}", "Integration settings must not contain raw secrets, tokens, passwords, or API keys.")]
    [InlineData("Connector", "[{\"accessToken\":null}]", "Integration settings must not contain raw secrets, tokens, passwords, or API keys.")]
    [InlineData("Connector", "{\"nested\":{\"PRIVATEkey\":false}}", "Integration settings must not contain raw secrets, tokens, passwords, or API keys.")]
    [InlineData("Connector", "{\"nested\":{\"sharedSecret\":[]}}", "Integration settings must not contain raw secrets, tokens, passwords, or API keys.")]
    [InlineData("Connector", "{\"clientSecret\":0}", "Integration settings must not contain raw secrets, tokens, passwords, or API keys.")]
    public async Task InvalidIntegrationCreateAndUpdatePreserveStoredStateAndEffects(string name, string? settings, string error)
    {
        var fixture = IntegrationFixture.Create();
        var rejectedCreate = await fixture.Service.CreateIntegrationAsync(new CreateIntegrationAccountRequest(IntegrationProvider.Other, name, settings));

        Assert.False(rejectedCreate.IsSuccess);
        Assert.Equal(error, rejectedCreate.Error);
        var empty = await fixture.Service.ListIntegrationsAsync();
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value!);
        Assert.Empty(await fixture.Integrations.ListIntegrationAccountsAsync());
        Assert.Equal(0, fixture.SaveCount);
        Assert.Empty(fixture.AuditEntries);

        var created = await fixture.Service.CreateIntegrationAsync(new CreateIntegrationAccountRequest(IntegrationProvider.Other, "Original connector", "{\"region\":\"fixture\"}"));
        Assert.True(created.IsSuccess);
        var original = created.Value!;
        var rejectedUpdate = await fixture.Service.UpdateIntegrationAsync(original.Id, new UpdateIntegrationAccountRequest(name, settings, IntegrationAccountStatus.Active));
        var stored = await fixture.Service.GetIntegrationAsync(original.Id);

        Assert.False(rejectedUpdate.IsSuccess);
        Assert.Equal(error, rejectedUpdate.Error);
        Assert.True(stored.IsSuccess);
        Assert.Equal(original, stored.Value);
        Assert.Equal(1, fixture.SaveCount);
        var audit = Assert.Single(fixture.AuditEntries);
        Assert.Equal("IntegrationCreated", audit.Action);
        Assert.Equal("IntegrationAccount", audit.EntityType);
        Assert.Equal(original.Id, audit.EntityId);
        Assert.Equal(fixture.UserId, audit.ActorUserId);
    }

    [Theory]
    [InlineData(null, "{}")]
    [InlineData("   ", "{}")]
    [InlineData("{\"nested\":[0,false,null,{\"region\":\"fixture\"}]}", "{\"nested\":[0,false,null,{\"region\":\"fixture\"}]}")]
    public async Task IntegrationLifecycleNormalizesDefaultsAndPreservesNullUpdates(string? settings, string expected)
    {
        var fixture = IntegrationFixture.Create();
        var request = new CreateIntegrationAccountRequest(IntegrationProvider.Other, "  Connector  ", settings);
        if (settings is not null)
        {
            request = request with { Status = IntegrationAccountStatus.Suspended };
        }
        var created = await fixture.Service.CreateIntegrationAsync(request);
        Assert.True(created.IsSuccess);
        var original = created.Value!;
        Assert.Equal("Connector", original.DisplayName);
        Assert.Equal(expected, original.SettingsJson);
        Assert.Equal(fixture.UserId, original.CreatedByUserId);
        Assert.Equal(settings is null ? IntegrationAccountStatus.Draft : IntegrationAccountStatus.Suspended, original.Status);

        var unchanged = await fixture.Service.UpdateIntegrationAsync(original.Id, new UpdateIntegrationAccountRequest(null, null, null));
        Assert.True(unchanged.IsSuccess);
        Assert.Equal(original, unchanged.Value);
        var updated = await fixture.Service.UpdateIntegrationAsync(original.Id, new UpdateIntegrationAccountRequest("  Renamed  ", " ", IntegrationAccountStatus.Active));
        Assert.True(updated.IsSuccess);
        Assert.Equal(original.Id, updated.Value!.Id);
        Assert.Equal("Renamed", updated.Value.DisplayName);
        Assert.Equal("{}", updated.Value.SettingsJson);
        Assert.Equal(IntegrationAccountStatus.Active, updated.Value.Status);
        var listed = await fixture.Service.ListIntegrationsAsync();
        Assert.True(listed.IsSuccess);
        Assert.Equal(updated.Value, Assert.Single(listed.Value!));

        Assert.True((await fixture.Service.DeleteIntegrationAsync(original.Id)).IsSuccess);
        Assert.False((await fixture.Service.GetIntegrationAsync(original.Id)).IsSuccess);
        Assert.False((await fixture.Service.UpdateIntegrationAsync(original.Id, new UpdateIntegrationAccountRequest("Revive", "{}", IntegrationAccountStatus.Active))).IsSuccess);
        Assert.False((await fixture.Service.DeleteIntegrationAsync(original.Id)).IsSuccess);
        var afterDelete = await fixture.Service.ListIntegrationsAsync();
        Assert.True(afterDelete.IsSuccess);
        Assert.Empty(afterDelete.Value!);
        Assert.Equal(4, fixture.SaveCount);
        Assert.Equal(new[] { "IntegrationCreated", "IntegrationUpdated", "IntegrationUpdated", "IntegrationDeleted" }, fixture.AuditEntries.Select(entry => entry.Action));
        Assert.All(fixture.AuditEntries, entry => { Assert.Equal("IntegrationAccount", entry.EntityType); Assert.Equal(original.Id, entry.EntityId); Assert.Equal(fixture.UserId, entry.ActorUserId); });
    }

    [Theory]
    [InlineData(" ", "https://example.test/hook", "[]", "Webhook name is required.")]
    [InlineData("Hook", "relative/path", "[]", "Webhook URL must be absolute.")]
    [InlineData("Hook", "ftp://example.test/hook", "[]", "Webhook URL must use HTTPS.")]
    [InlineData("Hook", "http://example.test/hook", "[]", "Webhook URL must use HTTPS.")]
    [InlineData("Hook", "https://example.test/hook", "{", "Webhook events must contain valid JSON.")]
    public async Task InvalidWebhookCreateAndUpdateDoNotMutateOrPublish(string name, string url, string events, string error)
    {
        var fixture = IntegrationFixture.Create();
        var rejectedCreate = await fixture.Service.CreateWebhookAsync(new CreateWebhookEndpointRequest(name, url, null, events));
        Assert.False(rejectedCreate.IsSuccess);
        Assert.Equal(error, rejectedCreate.Error);
        var empty = await fixture.Service.ListWebhooksAsync();
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value!);
        Assert.Empty(await fixture.Integrations.ListWebhookEndpointsAsync());
        Assert.Equal(0, fixture.SaveCount);
        Assert.Empty(fixture.AuditEntries);

        var created = await fixture.Service.CreateWebhookAsync(new CreateWebhookEndpointRequest("Original hook", "https://example.test/original", null, "[]"));
        Assert.True(created.IsSuccess);
        var original = created.Value!;
        var rejectedUpdate = await fixture.Service.UpdateWebhookAsync(original.Id, new UpdateWebhookEndpointRequest(name, url, null, events, WebhookEndpointStatus.Disabled));
        Assert.False(rejectedUpdate.IsSuccess);
        Assert.Equal(error, rejectedUpdate.Error);
        var stored = await fixture.Service.GetWebhookAsync(original.Id);
        Assert.True(stored.IsSuccess);
        Assert.Equal(original, stored.Value);
        Assert.Equal(1, fixture.SaveCount);
        var audit = Assert.Single(fixture.AuditEntries);
        Assert.Equal("WebhookCreated", audit.Action);
        Assert.Equal("WebhookEndpoint", audit.EntityType);
        Assert.Equal(original.Id, audit.EntityId);
        Assert.Equal(fixture.UserId, audit.ActorUserId);
    }

    [Fact]
    public async Task WebhookLifecycleRetainsNullFieldsAndDeletesWithoutFurtherEffects()
    {
        var fixture = IntegrationFixture.Create();
        var syntheticSecret = Guid.NewGuid().ToString("N");
        var created = await fixture.Service.CreateWebhookAsync(new CreateWebhookEndpointRequest("  Hook  ", "https://example.test/original", syntheticSecret, "[\"fixture.changed\"]", WebhookEndpointStatus.Error));
        Assert.True(created.IsSuccess);
        var original = created.Value!;
        Assert.Equal("Hook", original.Name);
        Assert.Equal("[\"fixture.changed\"]", original.EnabledEventsJson);
        Assert.Equal(WebhookEndpointStatus.Error, original.Status);
        var stored = await fixture.Integrations.GetWebhookEndpointAsync(original.Id);
        Assert.NotNull(stored);
        var originalSecretHash = stored.SecretHash;
        Assert.Equal(fixture.TokenHasher.HashToken(syntheticSecret), originalSecretHash);
        Assert.Equal(fixture.UserId, original.CreatedByUserId);
        var unchanged = await fixture.Service.UpdateWebhookAsync(original.Id, new UpdateWebhookEndpointRequest(null, null, null, null, null));
        Assert.True(unchanged.IsSuccess);
        Assert.Equal(original, unchanged.Value);
        Assert.Equal(originalSecretHash, stored.SecretHash);
        var updated = await fixture.Service.UpdateWebhookAsync(original.Id, new UpdateWebhookEndpointRequest("  Renamed hook  ", "https://example.test/updated", null, " ", WebhookEndpointStatus.Disabled));
        Assert.True(updated.IsSuccess);
        Assert.Equal("Renamed hook", updated.Value!.Name);
        Assert.Equal("https://example.test/updated", updated.Value.Url);
        Assert.Equal("[]", updated.Value.EnabledEventsJson);
        Assert.Equal(WebhookEndpointStatus.Disabled, updated.Value.Status);
        var validated = await fixture.Service.ValidateWebhookAsync(original.Id);
        Assert.True(validated.IsSuccess);
        Assert.Equal(updated.Value, validated.Value);
        var listed = await fixture.Service.ListWebhooksAsync();
        Assert.True(listed.IsSuccess);
        Assert.Equal(updated.Value, Assert.Single(listed.Value!));

        Assert.True((await fixture.Service.DeleteWebhookAsync(original.Id)).IsSuccess);
        Assert.False((await fixture.Service.GetWebhookAsync(original.Id)).IsSuccess);
        Assert.False((await fixture.Service.UpdateWebhookAsync(original.Id, new UpdateWebhookEndpointRequest("Revive", null, null, null, null))).IsSuccess);
        Assert.False((await fixture.Service.DeleteWebhookAsync(original.Id)).IsSuccess);
        Assert.False((await fixture.Service.ValidateWebhookAsync(original.Id)).IsSuccess);
        var afterDelete = await fixture.Service.ListWebhooksAsync();
        Assert.True(afterDelete.IsSuccess);
        Assert.Empty(afterDelete.Value!);
        Assert.Equal(5, fixture.SaveCount);
        Assert.Equal(new[] { "WebhookCreated", "WebhookUpdated", "WebhookUpdated", "WebhookTestValidated", "WebhookDeleted" }, fixture.AuditEntries.Select(entry => entry.Action));
        Assert.All(fixture.AuditEntries, entry => { Assert.Equal("WebhookEndpoint", entry.EntityType); Assert.Equal(original.Id, entry.EntityId); Assert.Equal(fixture.UserId, entry.ActorUserId); });
    }

    [Theory]
    [InlineData("empty-name")]
    [InlineData("long-name")]
    [InlineData("object-scopes")]
    [InlineData("non-string-scope")]
    [InlineData("invalid-json")]
    [InlineData("long-scopes")]
    [InlineData("expiry-now")]
    [InlineData("expiry-before")]
    public async Task InvalidTokenRequestsDoNotAllocateTokensOrPublishEffects(string scenario)
    {
        var fixture = IntegrationFixture.Create();
        var name = scenario switch { "empty-name" => " ", "long-name" => new string('n', 161), _ => "Fixture bot" };
        var scopes = scenario switch
        {
            "object-scopes" => "{}",
            "non-string-scope" => "[1]",
            "invalid-json" => "[",
            "long-scopes" => "[\"" + new string('s', 4000) + "\"]",
            _ => "[]"
        };
        var expiry = scenario switch
        {
            "expiry-now" => fixture.Clock.UtcNow,
            "expiry-before" => fixture.Clock.UtcNow.AddTicks(-1),
            _ => fixture.Clock.UtcNow.AddDays(1)
        };
        var result = await fixture.Service.CreateApiTokenAsync(new CreateApiTokenRequest(name, scopes, expiry));
        Assert.False(result.IsSuccess);
        Assert.Equal(scenario switch
        {
            "empty-name" or "long-name" => "API token name must be 1 to 160 characters.",
            "expiry-now" or "expiry-before" => "API token expiry must be in the future.",
            _ => "API token scopes must be a JSON array of strings (at most 4000 characters)."
        }, result.Error);
        Assert.Empty(fixture.Integrations.Tokens);
        Assert.Equal(0, fixture.SaveCount);
        Assert.Empty(fixture.AuditEntries);
    }

    [Fact]
    public async Task ValidTokenUseRecordsScopedIdentityAndLastUseWhileRepeatedRevokeRetainsTheTimestamp()
    {
        var fixture = IntegrationFixture.Create();
        var created = await fixture.Service.CreateApiTokenAsync(new CreateApiTokenRequest("  Fixture bot  ", "[\"read:projects\"]", null));
        Assert.True(created.IsSuccess);
        var token = Assert.Single(fixture.Integrations.Tokens);
        Assert.Equal("Fixture bot", created.Value!.Token.Name);
        Assert.Equal(fixture.TokenHasher.HashToken(created.Value.RawToken), token.TokenHash);
        Assert.NotEqual(created.Value.RawToken, token.TokenHash);
        Assert.Null(token.LastUsedAt);
        var used = await fixture.Validator.ValidateAsync(created.Value.RawToken);
        Assert.True(used.IsValid);
        Assert.Equal(token.Id, used.TokenId);
        Assert.Equal(token.TenantId, used.TenantId);
        Assert.Equal(token.CreatedByUserId, used.CreatedByUserId);
        Assert.Equal(new[] { "read:projects" }, used.Scopes);
        Assert.Equal(fixture.Clock.UtcNow, token.LastUsedAt);
        Assert.True((await fixture.Service.RevokeApiTokenAsync(token.Id)).IsSuccess);
        var revokedAt = token.RevokedAt;
        Assert.Equal(fixture.Clock.UtcNow, revokedAt);
        var earlierRevocation = fixture.Clock.UtcNow.AddMinutes(-1);
        token.RevokedAt = earlierRevocation;
        Assert.NotEqual(fixture.Clock.UtcNow, token.RevokedAt);
        Assert.True((await fixture.Service.RevokeApiTokenAsync(token.Id)).IsSuccess);
        Assert.Equal(earlierRevocation, token.RevokedAt);
        Assert.False((await fixture.Service.RevokeApiTokenAsync(Guid.NewGuid())).IsSuccess);
        Assert.Equal(4, fixture.SaveCount);
        Assert.Equal(new[] { "ApiTokenCreated", "ApiTokenUsed", "ApiTokenRevoked", "ApiTokenRevoked" }, fixture.AuditEntries.Select(entry => entry.Action));
        Assert.All(fixture.AuditEntries, entry => { Assert.Equal("ApiToken", entry.EntityType); Assert.Equal(token.Id, entry.EntityId); Assert.Equal(fixture.UserId, entry.ActorUserId); });
    }
}
