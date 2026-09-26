using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IIntegrationRepository
{
    Task<IReadOnlyList<IntegrationAccount>> ListIntegrationAccountsAsync(CancellationToken cancellationToken = default);
    Task<IntegrationAccount?> GetIntegrationAccountAsync(Guid integrationId, CancellationToken cancellationToken = default);
    Task AddIntegrationAccountAsync(IntegrationAccount integrationAccount, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookEndpoint>> ListWebhookEndpointsAsync(CancellationToken cancellationToken = default);
    Task<WebhookEndpoint?> GetWebhookEndpointAsync(Guid webhookId, CancellationToken cancellationToken = default);
    Task AddWebhookEndpointAsync(WebhookEndpoint webhookEndpoint, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiToken>> ListApiTokensAsync(CancellationToken cancellationToken = default);
    Task<ApiToken?> GetApiTokenAsync(Guid tokenId, CancellationToken cancellationToken = default);
    Task<ApiToken?> GetApiTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task AddApiTokenAsync(ApiToken token, CancellationToken cancellationToken = default);
}
