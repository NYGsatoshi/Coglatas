using Coglatas.Application.Common;

namespace Coglatas.Application.Integrations;

public interface IIntegrationService
{
    Task<Result<IReadOnlyList<IntegrationAccountResponse>>> ListIntegrationsAsync(CancellationToken cancellationToken = default);
    Task<Result<IntegrationAccountResponse>> CreateIntegrationAsync(CreateIntegrationAccountRequest request, CancellationToken cancellationToken = default);
    Task<Result<IntegrationAccountResponse>> GetIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default);
    Task<Result<IntegrationAccountResponse>> UpdateIntegrationAsync(Guid integrationId, UpdateIntegrationAccountRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WebhookEndpointResponse>>> ListWebhooksAsync(CancellationToken cancellationToken = default);
    Task<Result<WebhookEndpointResponse>> CreateWebhookAsync(CreateWebhookEndpointRequest request, CancellationToken cancellationToken = default);
    Task<Result<WebhookEndpointResponse>> GetWebhookAsync(Guid webhookId, CancellationToken cancellationToken = default);
    Task<Result<WebhookEndpointResponse>> UpdateWebhookAsync(Guid webhookId, UpdateWebhookEndpointRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteWebhookAsync(Guid webhookId, CancellationToken cancellationToken = default);
    Task<Result<WebhookEndpointResponse>> ValidateWebhookAsync(Guid webhookId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ApiTokenResponse>>> ListApiTokensAsync(CancellationToken cancellationToken = default);
    Task<Result<CreateApiTokenResponse>> CreateApiTokenAsync(CreateApiTokenRequest request, CancellationToken cancellationToken = default);
    Task<Result> RevokeApiTokenAsync(Guid tokenId, CancellationToken cancellationToken = default);
}

public interface IApiTokenValidator
{
    Task<ApiTokenValidationResult> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);
}
