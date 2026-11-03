using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: HostingStartup(typeof(Coglatas.Tests.PostgreSql.StartupSeedHostLifetimeStartup))]

namespace Coglatas.Tests.PostgreSql;

// Loaded only by the test-owned process; no product service or HTTP route is replaced.
public sealed class StartupSeedHostLifetimeStartup : IHostingStartup
{
    internal const string EnabledKey = "COGLATAS_STARTUP_SEED_TEST_LIFETIME";
    internal const string StopCommand = "COGLATAS_STARTUP_SEED_TEST_STOP";
    internal const string StopAcknowledgement = "COGLATAS_STARTUP_SEED_TEST_STOP_ACCEPTED";

    public void Configure(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices((context, services) =>
        {
            if (!context.Configuration.GetValue<bool>(EnabledKey)) return;
            if (!context.HostingEnvironment.IsEnvironment("Test") && !context.HostingEnvironment.IsDevelopment())
                throw new InvalidOperationException("The owned startup lifetime requires Test or Development.");
            services.AddHostedService<OwnedStartupLifetime>();
        });
    }

    private sealed class OwnedStartupLifetime(IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var command = await Console.In.ReadLineAsync(stoppingToken);
            if (command != StopCommand)
                throw new InvalidOperationException("The owned startup lifetime requires its explicit stop command.");
            await Console.Out.WriteLineAsync(StopAcknowledgement.AsMemory(), stoppingToken);
            await Console.Out.FlushAsync(stoppingToken);
            lifetime.StopApplication();
        }
    }
}
