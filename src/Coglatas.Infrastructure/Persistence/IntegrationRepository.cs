using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class IntegrationRepository(AppDbContext dbContext) : IIntegrationRepository
{
    public async Task<IReadOnlyList<IntegrationAccount>> ListIntegrationAccountsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.IntegrationAccounts
            .OrderBy(account => account.Provider)
            .ThenBy(account => account.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public Task<IntegrationAccount?> GetIntegrationAccountAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        return dbContext.IntegrationAccounts.FirstOrDefaultAsync(account => account.Id == integrationId, cancellationToken);
    }

    public async Task AddIntegrationAccountAsync(IntegrationAccount integrationAccount, CancellationToken cancellationToken = default)
    {
        await dbContext.IntegrationAccounts.AddAsync(integrationAccount, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListWebhookEndpointsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.WebhookEndpoints
            .OrderBy(webhook => webhook.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<WebhookEndpoint?> GetWebhookEndpointAsync(Guid webhookId, CancellationToken cancellationToken = default)
    {
        return dbContext.WebhookEndpoints.FirstOrDefaultAsync(webhook => webhook.Id == webhookId, cancellationToken);
    }

    public async Task AddWebhookEndpointAsync(WebhookEndpoint webhookEndpoint, CancellationToken cancellationToken = default)
    {
        await dbContext.WebhookEndpoints.AddAsync(webhookEndpoint, cancellationToken);
    }

    public async Task<IReadOnlyList<ApiToken>> ListApiTokensAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ApiTokens
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<ApiToken?> GetApiTokenAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        return dbContext.ApiTokens.FirstOrDefaultAsync(token => token.Id == tokenId, cancellationToken);
    }

    public Task<ApiToken?> GetApiTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return dbContext.ApiTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
    }

    public async Task AddApiTokenAsync(ApiToken token, CancellationToken cancellationToken = default)
    {
        await dbContext.ApiTokens.AddAsync(token, cancellationToken);
    }
}
