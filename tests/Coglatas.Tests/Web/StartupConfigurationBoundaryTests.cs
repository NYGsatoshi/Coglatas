using System.Data.Common;
using System.Security.Cryptography;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Files;
using Coglatas.Web.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Web;

public sealed class StartupConfigurationBoundaryTests
{
    [Theory]
    [InlineData("ObjectStorage")]
    [InlineData("S3Compatible")]
    [InlineData("OCIObjectStorage")]
    public async Task ValidProductionProfileStartsWithoutOpeningStorageOrDatabase(string provider)
    {
        using var fixture = new ValidationFixture();
        fixture.Files.Provider = provider;

        await fixture.Validator.StartAsync(CancellationToken.None);
        await fixture.Validator.StopAsync(CancellationToken.None);

        Assert.Empty(fixture.Log.Messages);
    }

    [Theory]
    [InlineData("cookie", "Security:CookieSecurePolicy must be Always in production.")]
    [InlineData("https", "Security:RequireHttps must be true in production.")]
    [InlineData("hsts", "Security:EnableHsts must be true in production.")]
    [InlineData("setup", "Platform:PlatformAdminSetupMode must not be enabled in production.")]
    [InlineData("keys", "DataProtection:KeysPath must be configured in production so auth cookies survive restarts and multi-instance deployments.")]
    [InlineData("connection-missing", "ConnectionStrings:DefaultConnection is required in production.")]
    [InlineData("connection-invalid", "ConnectionStrings:DefaultConnection is not a valid connection string.")]
    [InlineData("connection-no-secret", "ConnectionStrings:DefaultConnection must include a database password or equivalent secret in production.")]
    [InlineData("connection-short", "ConnectionStrings:DefaultConnection:Password appears to contain a weak or placeholder secret.")]
    [InlineData("storage-no-secret", "FileStorage:SecretKey is required in production.")]
    [InlineData("storage-short", "FileStorage:SecretKey appears to contain a weak or placeholder secret.")]
    public async Task ProductionRejectsAnUnsafeSettingWithoutDisclosingGeneratedSecrets(string scenario, string expected)
    {
        using var fixture = new ValidationFixture();
        var suppliedDatabaseSecret = fixture.DatabaseSecret;
        switch (scenario)
        {
            case "cookie": fixture.Security.CookieSecurePolicy = CookieSecurePolicy.SameAsRequest; break;
            case "https": fixture.Security.RequireHttps = false; break;
            case "hsts": fixture.Security.EnableHsts = false; break;
            case "setup": fixture.Platform.PlatformAdminSetupMode = true; break;
            case "keys": fixture.Configuration["DataProtection:KeysPath"] = " "; break;
            case "connection-missing": fixture.Configuration["ConnectionStrings:DefaultConnection"] = null; break;
            case "connection-invalid": fixture.Configuration["ConnectionStrings:DefaultConnection"] = "malformed"; break;
            case "connection-no-secret": fixture.Configuration["ConnectionStrings:DefaultConnection"] = "Host=synthetic.invalid"; break;
            case "connection-short":
                suppliedDatabaseSecret = fixture.DatabaseSecret[..11];
                fixture.SetDatabaseSecret(suppliedDatabaseSecret);
                break;
            case "storage-no-secret": fixture.Files.SecretKey = null; break;
            case "storage-short": fixture.Files.SecretKey = fixture.StorageSecret[..11]; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Validator.StartAsync(CancellationToken.None));

        Assert.Equal("Coglatas Portal configuration validation failed: " + expected, exception.Message);
        var entry = Assert.Single(fixture.Log.Entries);
        Assert.Equal("Configuration validation failed: " + expected, entry.Message);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        var properties = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(entry.State).ToArray();
        Assert.Equal(2, properties.Length);
        Assert.Contains(properties, property => property.Key == "Error" && Equals(property.Value, expected));
        Assert.Contains(properties, property => property.Key == "{OriginalFormat}" &&
            Equals(property.Value, "Configuration validation failed: {Error}"));
        foreach (var secret in new[] { fixture.DatabaseSecret, fixture.StorageSecret, suppliedDatabaseSecret, fixture.Files.SecretKey }.OfType<string>())
        {
            Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.State?.ToString() ?? string.Empty, StringComparison.Ordinal);
            foreach (var property in properties)
            {
                Assert.DoesNotContain(secret, property.Key, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, property.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("mode", "Tenancy:AppMode must be one of SaaS, OnPremSingleTenant, or OnPremMultiTenant.")]
    [InlineData("resolution", "Tenancy:TenantResolutionStrategy is not valid.")]
    [InlineData("single-tenant-slug", "Tenancy:DefaultTenantSlug is required for OnPremSingleTenant.")]
    [InlineData("header-disabled", "Tenancy:AllowDevelopmentHeaderTenantResolution must be true when using HeaderForDevelopmentOnly.")]
    [InlineData("file-size", "FileStorage:MaxFileSizeBytes must be positive.")]
    [InlineData("extensions-empty", "FileStorage:AllowedExtensions must contain at least one extension.")]
    [InlineData("extension-invalid", "FileStorage:AllowedExtensions contains invalid extension 'txt'.")]
    [InlineData("types-empty", "FileStorage:AllowedContentTypes must contain at least one MIME type.")]
    [InlineData("type-invalid", "FileStorage:AllowedContentTypes contains invalid MIME type 'plain'.")]
    [InlineData("provider", "FileStorage:Provider 'Unsupported' is not supported.")]
    [InlineData("local-root", "FileStorage:RootPath is required for LocalFileSystem.")]
    [InlineData("bucket", "FileStorage:BucketName is required for object storage providers.")]
    [InlineData("region", "FileStorage:Region is required for ObjectStorage.")]
    [InlineData("endpoint", "FileStorage:Endpoint is required for S3Compatible storage.")]
    [InlineData("lockout-attempts", "Security:MaxFailedLoginAttempts must be positive when lockout is enabled.")]
    [InlineData("lockout-duration", "Security:LoginLockoutDurationMinutes must be positive when lockout is enabled.")]
    public async Task DevelopmentStillRejectsInvalidTenantFileAndLockoutConfiguration(string scenario, string expected)
    {
        using var fixture = new ValidationFixture(Environments.Development);
        switch (scenario)
        {
            case "mode": fixture.Tenancy.AppMode = (AppMode)int.MaxValue; break;
            case "resolution": fixture.Tenancy.TenantResolutionStrategy = (TenantResolutionStrategy)int.MaxValue; break;
            case "single-tenant-slug": fixture.Tenancy.AppMode = AppMode.OnPremSingleTenant; fixture.Tenancy.DefaultTenantSlug = " "; break;
            case "header-disabled": fixture.Tenancy.TenantResolutionStrategy = TenantResolutionStrategy.HeaderForDevelopmentOnly; break;
            case "file-size": fixture.Files.MaxFileSizeBytes = 0; break;
            case "extensions-empty": fixture.Files.AllowedExtensions = []; break;
            case "extension-invalid": fixture.Files.AllowedExtensions = ["txt"]; break;
            case "types-empty": fixture.Files.AllowedContentTypes = []; break;
            case "type-invalid": fixture.Files.AllowedContentTypes = ["plain"]; break;
            case "provider": fixture.Files.Provider = "Unsupported"; break;
            case "local-root": fixture.Files.Provider = "LocalFileSystem"; fixture.Files.RootPath = " "; break;
            case "bucket": fixture.Files.BucketName = " "; break;
            case "region": fixture.Files.Region = " "; break;
            case "endpoint": fixture.Files.Provider = "S3Compatible"; fixture.Files.Endpoint = " "; break;
            case "lockout-attempts": fixture.Security.MaxFailedLoginAttempts = 0; break;
            case "lockout-duration": fixture.Security.LoginLockoutDurationMinutes = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Validator.StartAsync(CancellationToken.None));

        Assert.Equal("Coglatas Portal configuration validation failed: " + expected, exception.Message);
        Assert.Equal("Configuration validation failed: " + expected, Assert.Single(fixture.Log.Messages));
    }

    [Theory]
    [InlineData(false, true, "Security:EnableCsrfProtection is true, but CSRF middleware is not active.")]
    [InlineData(true, false, "Security:EnableCsrfProtection is true, but antiforgery services are not registered.")]
    public async Task CsrfRequiresBothRegisteredServicesAndActiveMiddleware(bool middleware, bool services, string expected)
    {
        using var fixture = new ValidationFixture(middleware: middleware, antiforgery: services);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Validator.StartAsync(CancellationToken.None));

        Assert.Equal("Coglatas Portal configuration validation failed: " + expected, exception.Message);
        Assert.Equal("Configuration validation failed: " + expected, Assert.Single(fixture.Log.Messages));
    }

    [Theory]
    [InlineData(false, "Development header tenant resolution is disabled in production unless explicitly allowed.")]
    [InlineData(true, "Tenancy:AllowDevelopmentHeaderInProduction must be false for production deployments.")]
    public async Task ProductionNeverAdmitsTheDevelopmentHeaderStrategy(bool overrideFlag, string expected)
    {
        using var fixture = new ValidationFixture();
        fixture.Tenancy.TenantResolutionStrategy = TenantResolutionStrategy.HeaderForDevelopmentOnly;
        fixture.Tenancy.AllowDevelopmentHeaderTenantResolution = true;
        fixture.Tenancy.AllowDevelopmentHeaderInProduction = overrideFlag;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Validator.StartAsync(CancellationToken.None));

        Assert.Equal("Coglatas Portal configuration validation failed: " + expected, exception.Message);
        Assert.Equal("Configuration validation failed: " + expected, Assert.Single(fixture.Log.Messages));
    }

    private sealed class ValidationFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public TenancyOptions Tenancy { get; } = new();
        public FileStorageOptions Files { get; } = new()
        {
            Provider = "ObjectStorage", BucketName = "synthetic-bucket", Region = "synthetic-region",
            Endpoint = "https://storage.invalid", AllowedExtensions = [".txt"], AllowedContentTypes = ["text/plain"]
        };
        public SecurityOptions Security { get; } = new();
        public PlatformOptions Platform { get; } = new();
        public IConfiguration Configuration { get; }
        public RecordingLog Log { get; } = new();
        public StartupConfigurationValidator Validator { get; }
        public string DatabaseSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string StorageSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        public ValidationFixture(string? environment = null, bool middleware = true, bool antiforgery = true)
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeysPath"] = Path.Combine(Path.GetTempPath(), "coglatas-synthetic-unused-keys")
            }).Build();
            SetDatabaseSecret(DatabaseSecret);
            Files.SecretKey = StorageSecret;
            var collection = new ServiceCollection().AddLogging();
            if (antiforgery) collection.AddAntiforgery();
            _services = collection.BuildServiceProvider();
            var csrf = new CsrfProtectionState();
            if (middleware) csrf.MarkMiddlewareActive();
            Validator = new StartupConfigurationValidator(Options.Create(Tenancy), Options.Create(Files),
                Options.Create(Security), Options.Create(Platform), Configuration, _services, csrf,
                new TestEnvironment(environment ?? Environments.Production), Log);
        }

        public void SetDatabaseSecret(string value)
        {
            var connection = new DbConnectionStringBuilder { ["Host"] = "synthetic.invalid", ["Password"] = value };
            Configuration["ConnectionStrings:DefaultConnection"] = connection.ConnectionString;
        }

        public void Dispose() => _services.Dispose();
    }

    private sealed class RecordingLog : ILogger<StartupConfigurationValidator>
    {
        public List<LogEntry> Entries { get; } = [];
        public IEnumerable<string> Messages => Entries.Select(entry => entry.Message);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, state));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, object? State);

    private sealed class TestEnvironment(string environment) : IWebHostEnvironment
    {
        string IHostEnvironment.EnvironmentName { get; set; } = environment;
        string IHostEnvironment.ApplicationName { get; set; } = nameof(StartupConfigurationBoundaryTests);
        string IHostEnvironment.ContentRootPath { get; set; } = Path.GetTempPath();
        IFileProvider IHostEnvironment.ContentRootFileProvider { get; set; } = new NullFileProvider();
        string IWebHostEnvironment.WebRootPath { get; set; } = Path.GetTempPath();
        IFileProvider IWebHostEnvironment.WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
