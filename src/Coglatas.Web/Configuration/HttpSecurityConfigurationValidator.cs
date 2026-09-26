using Coglatas.Infrastructure.Files;
using Microsoft.Extensions.Options;

namespace Coglatas.Web.Configuration;

public sealed class HttpSecurityConfigurationValidator(
    IOptions<SecurityOptions> securityOptions,
    IOptions<FileStorageOptions> fileStorageOptions,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<HttpSecurityConfigurationValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var errors = HttpSecurityPolicy.GetConfigurationErrors(
            securityOptions.Value,
            fileStorageOptions.Value,
            configuration,
            environment.IsProduction());
        if (errors.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var error in errors)
        {
            logger.LogError("HTTP security configuration validation failed: {Error}", error);
        }

        throw new InvalidOperationException(
            "Coglatas Portal HTTP security configuration validation failed: " + string.Join(" | ", errors));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
