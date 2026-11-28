using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Coglatas.Application;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Coglatas.Tests.ProjectIde;
using Coglatas.Web;
using Coglatas.Web.Configuration;
using Coglatas.Web.Middleware;
using Coglatas.Web.ProjectIde;
using Coglatas.Web.Security;
using Coglatas.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Web;

public sealed class DevelopmentRuntimeHttpPostgreSqlTests
{
    private const string PrimaryTenantSlug = "runtime-recording-primary";
    private const string OtherTenantSlug = "runtime-recording-other";
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    [Theory]
    [InlineData("Production", true, true, true)]
    [InlineData("Staging", true, true, true)]
    [InlineData("Test", true, true, true)]
    [InlineData("Development", false, true, true)]
    [InlineData("Development", true, false, true)]
    [InlineData("Development", true, true, false)]
    public async Task Inactive_composition_exposes_no_route_or_runtime_store(
        string environment, bool requested, bool hasSourceResolver, bool hasPolicy)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration[ProjectIdeDevelopmentRuntime.EnabledConfigurationKey] = requested.ToString();
        var authority = new RecordingAuthority(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
        if (hasSourceResolver)
            builder.Services.AddSingleton<IProjectIdeRecordingSourceResolver>(authority);
        if (hasPolicy)
            builder.Services.AddSingleton<IProjectIdeOperationAuthorization>(authority);

        var registered = builder.AddProjectIdeDevelopmentRuntime();
        Assert.False(registered);
        await using var app = builder.Build();
        Assert.False(app.MapProjectIdeDevelopmentRuntime(registered));
        Assert.False(app.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ICompilerRecordingStore)));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task Kestrel_discloses_only_authorized_receipt_metadata_and_withdraws_it_on_revocation() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var runId = Guid.NewGuid();
                var tenant = new Tenant { Slug = PrimaryTenantSlug, Name = "Recording", DisplayName = "Recording" };
                var otherTenant = new Tenant { Slug = OtherTenantSlug, Name = "Other", DisplayName = "Other" };
                var user = new User { Email = "recording-runtime@example.invalid", NormalizedEmail = "RECORDING-RUNTIME@EXAMPLE.INVALID" };
                var session = new Session { UserId = user.Id, SessionKeyHash = "private-session-canary",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), LastSeenAt = DateTimeOffset.UtcNow };
                var workspace = new Workspace { TenantId = tenant.Id, Name = "Recording", Slug = "recording", CreatedByUserId = user.Id };
                var project = new Project { TenantId = tenant.Id, WorkspaceId = workspace.Id, Name = "Recording", Slug = "recording",
                    OwnerUserId = user.Id, CreatedByUserId = user.Id };
                await using (var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                        "SELECT to_regnamespace('projectide_development') IS NULL"));
                    await CompilerRecordingDevelopmentSchema.ProvisionAsync(writer);
                    writer.AddRange(tenant, otherTenant, user, session, workspace, project,
                        new TenantUser { TenantId = tenant.Id, UserId = user.Id, Status = TenantUserStatus.Active },
                        new TenantUser { TenantId = otherTenant.Id, UserId = user.Id, Status = TenantUserStatus.Active });
                    await writer.SaveChangesAsync();
                }

                var authority = new RecordingAuthority(tenant.Id, user.Id, project.Id, runId);
                await using var app = CreateHost(database, authority);
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("session_id", session.Id.ToString())], Scheme));
                var expected = await RecordAsync(app.Services, principal, tenant.Id, project.Id, runId);
                authority.Source = new(expected.Source, ContentDigest.Parse(SourceJson.FromObject(new
                {
                    algorithm = ContentDigest.Algorithm, value = expected.SourceDigest
                })));

                await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
                client.BaseAddress = new Uri(address);
                var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme);
                var ticket = new AuthenticationTicket(principal,
                    new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, Scheme);
                var cookie = $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
                var path = $"/api/projectide/runtime/projects/{project.Id}/compiler-recordings/{runId}";

                using var anonymous = await GetAsync(client, path);
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

                using var allowed = await GetAsync(client, path, cookie);
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
                Assert.True(allowed.Headers.CacheControl?.NoStore);
                using var parsed = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
                var receipt = parsed.RootElement;
                Assert.Equal(new[] { "captureMode", "captureState", "hostStatus", "receiptDigest", "result", "runId" },
                    receipt.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                Assert.Equal(runId, receipt.GetProperty("runId").GetGuid());
                Assert.Equal(expected.ReceiptDigest, receipt.GetProperty("receiptDigest").GetString());
                Assert.Equal(expected.HostStatus.ToString(), receipt.GetProperty("hostStatus").GetString());
                Assert.Equal(expected.CaptureMode.ToString(), receipt.GetProperty("captureMode").GetString());
                Assert.Equal(expected.OriginalCaptureState?.ToString(), receipt.GetProperty("captureState").GetString());
                var result = receipt.GetProperty("result");
                Assert.Equal(new[] { "coverage", "evidenceComplete", "execution", "resultDigest", "verdict" },
                    result.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                Assert.Equal(expected.Result!.ResultDigest, result.GetProperty("resultDigest").GetString());
                Assert.Equal(expected.Result.Execution.ToString(), result.GetProperty("execution").GetString());
                Assert.Equal(expected.Result.Coverage.ToString(), result.GetProperty("coverage").GetString());
                Assert.Equal(expected.Result.Verdict.ToString(), result.GetProperty("verdict").GetString());
                Assert.Equal(expected.Result.EvidenceComplete, result.GetProperty("evidenceComplete").GetBoolean());

                await AssertUnavailableAsync(client, path, cookie, OtherTenantSlug);
                await AssertUnavailableAsync(client, $"/api/projectide/runtime/projects/{project.Id}/compiler-recordings/{Guid.NewGuid()}", cookie);
                await AssertUnavailableAsync(client, $"/api/projectide/runtime/projects/{Guid.NewGuid()}/compiler-recordings/{runId}", cookie);
                var admittedSource = authority.Source!;
                authority.Source = admittedSource with { Identity = admittedSource.Identity with { BranchId = Guid.NewGuid() } };
                await AssertUnavailableAsync(client, path, cookie);
                authority.Source = admittedSource;
                authority.AllowRead = false;
                await AssertUnavailableAsync(client, path, cookie);
                authority.AllowRead = true;
                var readChecks = 0;
                authority.BeforeRead = () => Interlocked.Increment(ref readChecks) == 2
                    ? PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                        "UPDATE sessions SET \"RevokedAt\" = now() WHERE \"Id\" = @session", ("session", session.Id))
                    : Task.CompletedTask;
                await AssertUnavailableAsync(client, path, cookie);
                Assert.Equal(2, readChecks);

                using var revoked = await GetAsync(client, path, cookie);
                Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            });

    private static WebApplication CreateHost(string database, RecordingAuthority authority)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["ConnectionStrings:DefaultConnection"] = database;
        builder.Configuration[ProjectIdeDevelopmentRuntime.EnabledConfigurationKey] = "true";
        builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
        builder.Services.Configure<TenancyOptions>(options =>
        {
            options.TenantResolutionStrategy = TenantResolutionStrategy.HeaderForDevelopmentOnly;
            options.AllowDevelopmentHeaderTenantResolution = true;
        });
        builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<TenancyOptions>>().Value);
        builder.Services.AddSingleton(new AuthSecurityOptions());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
        builder.Services.AddScoped<ITenantResolver, HttpTenantResolver>();
        builder.Services.AddScoped<DbSessionCookieAuthenticationEvents>();
        builder.Services.AddAuthentication(Scheme).AddCookie(options =>
        {
            HttpSecurityPolicy.ConfigureAuthenticationCookie(options, new SecurityOptions());
            options.EventsType = typeof(DbSessionCookieAuthenticationEvents);
        });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IProjectIdeRecordingSourceResolver>(authority);
        builder.Services.AddSingleton<IProjectIdeOperationAuthorization>(authority);
        var registered = builder.AddProjectIdeDevelopmentRuntime();
        Assert.True(registered);
        var app = builder.Build();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        Assert.True(app.MapProjectIdeDevelopmentRuntime(registered));
        AngularSpaFallback.MapEndpointFallback(app, app.Environment.ContentRootPath);
        return app;
    }

    private static async Task<CompilerRecordingEvidence> RecordAsync(IServiceProvider services, ClaimsPrincipal principal,
        Guid tenantId, Guid projectId, Guid runId)
    {
        await using var scope = services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = principal };
        try
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantAccessor>().SetTenant(tenantId, PrimaryTenantSlug);
            var context = SourceRevisionContext.Committed(new(new(new TenantId(tenantId), new ProjectId(projectId),
                BranchId.New()), RevisionId.New()));
            var example = CompilerContractFixtures.Source();
            var source = ProjectSource.Create(context, example.Documents,
                extensions: SourceJson.Parse("{\"fixture\":\"private-source-canary\"}"));
            var input = CompilerContractFixtures.Input(source: source);
            var request = new CompilerHostRequest(new(input));
            using var recordingSession = new LocalCompilerRecordingSession(PredicateCompilerHostTests.Profile(input));
            var recording = await LocalCompilerRecordedInvocation.ExecuteAsync(recordingSession,
                recordingSession.Prepare(request, runId, CompilerOutputSnapshotMode.AvailableOutputs), request);
            var store = scope.ServiceProvider.GetRequiredService<ICompilerRecordingStore>();
            Assert.Equal(CompilerRecordingWriteOutcome.Recorded, await store.RecordAsync(recording, default));
            return CompilerRecordingEvidence.FromInvocation(recording);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? cookie = null,
        string tenant = PrimaryTenantSlug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Tenant-Slug", tenant);
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task AssertUnavailableAsync(HttpClient client, string path, string cookie,
        string tenant = PrimaryTenantSlug)
    {
        using var response = await GetAsync(client, path, cookie, tenant);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    private sealed class RecordingAuthority(Guid tenantId, Guid userId, Guid projectId, Guid runId)
        : IProjectIdeRecordingSourceResolver, IProjectIdeOperationAuthorization
    {
        public ProjectIdeRecordingSource? Source { get; set; }
        public bool AllowRead { get; set; } = true;
        public Func<Task>? BeforeRead { get; set; }

        public ValueTask<ProjectIdeRecordingSource?> ResolveAsync(ProjectIdeViewerContext viewer, Guid requestedProjectId,
            Guid requestedRunId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(viewer.TenantId == tenantId && requestedProjectId == projectId && requestedRunId == runId
                ? Source : null);

        public async ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
            SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken)
        {
            if (viewer.TenantId != tenantId || viewer.UserId != userId || source.ProjectId != projectId)
                return false;
            if (operation == CompilerRecordingOperations.Record)
                return true;
            if (operation != CompilerRecordingOperations.Read || !AllowRead ||
                Source is not { } expected || source != expected.Identity || sourceDigest != expected.Digest.Value)
                return false;
            if (BeforeRead is { } beforeRead)
                await beforeRead();
            return true;
        }
    }
}
