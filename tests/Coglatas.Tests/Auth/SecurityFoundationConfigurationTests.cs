using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Files;
using Coglatas.Web.Configuration;
using Coglatas.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coglatas.Tests.Auth;

public sealed class SecurityFoundationConfigurationTests
{
    [Fact]
    public void UnconfiguredFoundationIsDisabledAndExistingValidatorRemainsRegistered()
    {
        var configuration = new ConfigurationBuilder().Build();
        var options = new SecurityOptions();
        Assert.Equal(SecurityEnforcementMode.Disabled, options.EvaluationMode);
        Assert.False(options.EnforcementAllowed);
        var services = new ServiceCollection();
        services.AddWebServices(configuration);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(HttpSecurityConfigurationValidator));
    }

    [Theory]
    [InlineData("Development", "Disabled")]
    [InlineData("Development", "Shadow")]
    [InlineData("Test", "Disabled")]
    [InlineData("Test", "Shadow")]
    [InlineData("Production", "Disabled")]
    [InlineData("Production", "Shadow")]
    public async Task ActualStartupAcceptsOnlyExplicitSupportedModes(string environment, string mode)
    {
        using var host = CreateHost(environment, mode, enforcementAllowed: false);
        await host.StartAsync();
        Assert.Equal(Enum.Parse<SecurityEnforcementMode>(mode),
            host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityOptions>>().Value.EvaluationMode);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Development", true)]
    [InlineData("Test", false)]
    [InlineData("Test", true)]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    public async Task ActualStartupRejectsEnforceEvenWhenCompatibilityFlagIsOverridden(string environment, bool enforcementAllowed)
    {
        using var host = CreateHost(environment, "Enforce", enforcementAllowed);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Contains("Security:EnforcementAllowed must remain false", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("unrecognized-mode")]
    public async Task ActualStartupRejectsUnknownModeConfiguration(string mode)
    {
        using var host = CreateHost("Test", mode, enforcementAllowed: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("Shadow")]
    public async Task EnforcementApprovalFlagCannotBePreenabled(string mode)
    {
        using var host = CreateHost("Test", mode, enforcementAllowed: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }

    private static IHost CreateHost(string environment, string mode, bool enforcementAllowed)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            DisableDefaults = true
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:EvaluationMode"] = mode,
            ["Security:EnforcementAllowed"] = enforcementAllowed.ToString(),
            ["AllowedHosts"] = "portal.example.test"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IWebHostEnvironment>(new TestWebEnvironment(environment));
        builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
        builder.Services.Configure<FileStorageOptions>(_ => { });
        builder.Services.AddHostedService<HttpSecurityConfigurationValidator>();
        return builder.Build();
    }

    private sealed class TestWebEnvironment(string environment) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "SecurityFoundationTestHost";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
