using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.SecurityArchitecture;

// Launch the actual Web entry point. No authentication, Hub or dispatcher service is replaced.
internal sealed class SecurityArchitectureSignalRFixture : IAsyncDisposable
{
    private readonly Process _server;
    private readonly string _directory;
    private readonly ServiceProvider _clients;
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, CookieContainer> _cookies = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, byte> _startupTypes = new();
    private readonly bool _sessionTenantResolution;
    private string Database { get; }
    public Uri Address { get; private set; } = null!;

    private SecurityArchitectureSignalRFixture(string database, bool approvedOrigin, bool sessionTenantResolution)
    {
        Database = database;
        _sessionTenantResolution = sessionTenantResolution;
        _directory = Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-realtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var services = new ServiceCollection();
        foreach (var identity in new[] { "member", "owner", "beta", "restricted", "anonymous" })
        {
            var cookies = new CookieContainer();
            _cookies.Add(identity, cookies);
            services.AddHttpClient(identity, client => client.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                { CookieContainer = cookies, AllowAutoRedirect = false });
        }
        _clients = services.BuildServiceProvider();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(AuthController).Assembly.Location);
        // Do not inherit bootstrap credentials, production URLs or developer secret configuration.
        var inherited = new Dictionary<string, string?>();
        foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT" })
            inherited[key] = Environment.GetEnvironmentVariable(key);
        start.Environment.Clear();
        foreach (var (key, value) in inherited)
            if (value is not null) start.Environment[key] = value;
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1",
            ["ASPNETCORE_ENVIRONMENT"] = "Test", ["DOTNET_ENVIRONMENT"] = "Test",
            ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
            ["ConnectionStrings__DefaultConnection"] = database,
            ["Tenancy__AppMode"] = "SaaS", ["Tenancy__TenantResolutionStrategy"] = "HeaderForDevelopmentOnly",
            ["Tenancy__AllowDevelopmentHeaderTenantResolution"] = "true", ["Tenancy__SeedOnStartup"] = "false",
            ["Tenancy__DevelopmentTenantHeaderName"] = "X-Tenant-Slug",
            ["SecurityCiFixture__Enabled"] = "true", ["SecurityCiFixture__Password"] = _password,
            ["Security__RequireHttps"] = "false", ["Security__CookieSecurePolicy"] = "SameAsRequest",
            ["Security__EnableHsts"] = "false", ["Security__EnableRateLimiting"] = "false",
            ["Security__EnableCsrfProtection"] = "true", ["Security__EvaluationMode"] = "Disabled",
            ["FileStorage__RootPath"] = Path.Combine(_directory, "files"),
            ["FileStorage__AllowedExtensions__0"] = ".txt",
            ["FileStorage__AllowedContentTypes__0"] = "text/plain",
            ["DataProtection__KeysPath"] = Path.Combine(_directory, "keys"),
            ["Realtime__DispatcherPollSeconds"] = "1",
            ["Logging__LogLevel__Default"] = "Warning",
            ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information"
        }) start.Environment[key] = value;
        if (sessionTenantResolution)
        {
            start.Environment["Tenancy__TenantResolutionStrategy"] = "Session";
            // The SEC-02 hosted initializer deliberately requires header resolution.
            // Preserve that boundary; seed this disposable cookie-mode scenario
            // directly with the same existing synthetic graph before starting Web.
            start.Environment["SecurityCiFixture__Enabled"] = "false";
        }
        if (approvedOrigin)
        {
            start.Environment["Security__AllowedCorsOrigins__0"] = "https://console.example.test";
            start.Environment["Security__AllowCorsCredentials"] = "true";
        }
        _server = new Process { StartInfo = start, EnableRaisingEvents = true };
        _server.OutputDataReceived += (_, args) =>
        {
            const string marker = "Now listening on: ";
            var line = args.Data;
            var index = line?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (index >= 0 && Uri.TryCreate(line![(index + marker.Length)..].Trim(), UriKind.Absolute, out var address) &&
                address.Scheme == "http" && address.Host == "127.0.0.1" && address.Port > 0)
                _listening.TrySetResult(address);
        };
        // Drain stderr without printing synthetic credentials, request data or exception text.
        _server.ErrorDataReceived += (_, args) =>
        {
            foreach (Match match in Regex.Matches(args.Data ?? "", @"\b(?:System|Coglatas|Microsoft)\.[A-Za-z0-9_.]+(?:Exception|Service|Worker|Options)\b"))
                _startupTypes.TryAdd(match.Value, 0);
            foreach (Match match in Regex.Matches(args.Data ?? "", @"\bat (Coglatas\.[A-Za-z0-9_.]+)\("))
                _startupTypes.TryAdd(match.Groups[1].Value, 0);
            foreach (var message in new[] {
                "SEC-02 fixture hosted service was activated outside its Test-only boundary.",
                "SEC-02 requires multi-tenant mode with explicit Test-only header tenant resolution.",
                "SEC-02 fixture is enabled but SecurityCiFixture:Password/COGLATAS_SECURITY_CI_PASSWORD is missing." })
                if (args.Data?.Contains(message, StringComparison.Ordinal) == true) _startupTypes.TryAdd(message, 0);
        };
        _server.Exited += (_, _) => _listening.TrySetException(new InvalidOperationException(
            "Synthetic Web process exited before readiness; exit=" + _server.ExitCode + "; types=" +
            string.Join(',', _startupTypes.Keys.Order(StringComparer.Ordinal))));
    }

    public static async Task<SecurityArchitectureSignalRFixture> StartAsync(string database, bool approvedOrigin = false,
        bool sessionTenantResolution = false)
    {
        var fixture = new SecurityArchitectureSignalRFixture(database, approvedOrigin, sessionTenantResolution);
        try
        {
            if (sessionTenantResolution)
            {
                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var storage = new LocalFileStorageService(Options.Create(new FileStorageOptions
                    { RootPath = Path.Combine(fixture._directory, "files") }));
                await SecurityCiFixtureSeed.SeedAsync(db, new Pbkdf2PasswordHasher(), storage, fixture._password);
                await SecurityCiAuthorizationMatrixSeed.SeedAsync(db);
            }
            if (!fixture._server.Start()) throw new InvalidOperationException("Synthetic Web process did not start.");
            fixture._server.BeginOutputReadLine();
            fixture._server.BeginErrorReadLine();
            fixture.Address = await fixture._listening.Task.WaitAsync(TimeSpan.FromSeconds(50));
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private HttpClient CreateClient(string identity, string tenant)
    {
        var client = _clients.GetRequiredService<IHttpClientFactory>().CreateClient(identity);
        client.BaseAddress = Address;
        if (_sessionTenantResolution)
            _cookies[identity].Add(Address, new Cookie("coglatas_tenant", tenant));
        else
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", tenant);
        return client;
    }

    public async Task<HttpClient> LoginAsync(string identity, string tenant, string email)
    {
        var client = CreateClient(identity, tenant);
        try
        {
            await RefreshCsrfAsync(client);
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = _password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await RefreshCsrfAsync(client);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    public async Task<HttpClient> CreateAnonymousClientAsync(string tenant)
    {
        var client = CreateClient("anonymous", tenant);
        try { await RefreshCsrfAsync(client); return client; }
        catch { client.Dispose(); throw; }
    }

    private static async Task RefreshCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/security/csrf-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var header = body.RootElement.GetProperty("headerName").GetString()!;
        var token = body.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Remove(header);
        client.DefaultRequestHeaders.Add(header, token);
    }

    public async Task<RealtimeSocket> ConnectAsync(HttpClient client, string identity, string tenant)
    {
        using var response = await client.PostAsync("/hubs/app/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var connectionToken = body.RootElement.GetProperty("connectionToken").GetString()!;
        var socket = CreateSocket(identity, tenant);
        var address = new UriBuilder(Address) { Scheme = "ws", Path = "/hubs/app", Query = "id=" + Uri.EscapeDataString(connectionToken) }.Uri;
        try { await socket.ConnectAsync(address); return socket; }
        catch { await socket.DisposeAsync(); throw; }
    }

    public RealtimeSocket CreateSocket(string identity, string tenant)
    {
        var socket = new RealtimeSocket();
        var snapshot = new CookieContainer();
        foreach (Cookie cookie in _cookies[identity].GetAllCookies()) snapshot.Add(cookie);
        socket.Options.Cookies = snapshot;
        if (!_sessionTenantResolution) socket.Options.SetRequestHeader("X-Tenant-Slug", tenant);
        return socket;
    }

    public async Task WaitDeliveredAsync(Guid eventId, DateTimeOffset? deliveredAfterUtc = null)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(Database);
            var state = await context.OutboxEvents.AsNoTracking().Where(e => e.Id == eventId)
                .Select(e => new { e.Status, e.DeliveredAt }).SingleAsync();
            if (state.Status == OutboxEventStatus.Delivered &&
                (!deliveredAfterUtc.HasValue || state.DeliveredAt >= deliveredAfterUtc.Value)) return;
            Assert.DoesNotContain(state.Status, new[] { OutboxEventStatus.DeadLetter, OutboxEventStatus.Cancelled });
            await Task.Delay(100);
        }
        Assert.Fail("Synthetic Outbox event did not reach a delivered terminal state.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_server.Id != 0 && !_server.HasExited)
            {
                _server.Kill(entireProcessTree: true);
                await _server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException) { /* Process may not have started or may have exited. */ }
        finally
        {
            _server.Dispose();
            await _clients.DisposeAsync();
            var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(_directory).StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(_directory).StartsWith("coglatas-sec-arch-realtime-", StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic cleanup path escaped the temporary root.");
            Directory.Delete(_directory, recursive: true);
        }
    }
}

internal sealed class RealtimeSocket : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _invocations = new();
    private readonly ConcurrentDictionary<Guid, int> _events = new();
    private readonly ConcurrentDictionary<Guid, (string EventType, int SchemaVersion)> _eventMetadata = new();
    private readonly TaskCompletionSource _handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _receiver;
    private int _sequence;
    public ClientWebSocketOptions Options => _socket.Options;
    public HttpStatusCode UpgradeStatusCode => _socket.HttpStatusCode;

    public async Task ConnectAsync(Uri address)
    {
        await _socket.ConnectAsync(address, _stopping.Token).WaitAsync(TimeSpan.FromSeconds(10));
        _receiver = ReceiveAsync();
        await SendAsync(new { protocol = "json", version = 1 });
        await _handshake.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async Task<bool> SubscribeAsync(string method, params object[] arguments)
    {
        var result = await InvokeAsync(method, arguments);
        Assert.True(result.TryGetProperty("allowed", out var allowed));
        return allowed.GetBoolean();
    }

    public async Task<JsonElement> InvokeAsync(string target, params object[] arguments)
    {
        var id = Interlocked.Increment(ref _sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(_invocations.TryAdd(id, pending));
        try
        {
            await SendAsync(new { type = 1, invocationId = id, target, arguments });
            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { _invocations.TryRemove(id, out _); }
    }

    public bool Received(Guid eventId) => _events.ContainsKey(eventId);
    public int DeliveryCount(Guid eventId) => _events.GetValueOrDefault(eventId);
    public bool Received(Guid eventId, string eventType) =>
        _eventMetadata.TryGetValue(eventId, out var metadata) && metadata == (eventType, 1) && Received(eventId);
    public async Task WaitEventAsync(Guid eventId, int minimumCount = 1)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (_events.GetValueOrDefault(eventId) < minimumCount && DateTimeOffset.UtcNow < deadline)
        {
            Assert.True(_receiver is { IsCompleted: false }, "The authenticated real transport must remain connected.");
            await Task.Delay(50);
        }
        Assert.True(_events.GetValueOrDefault(eventId) >= minimumCount,
            "A positive control event was not delivered over the authenticated real transport.");
    }

    private Task SendAsync(object message) => _socket.SendAsync(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + '\u001e'), WebSocketMessageType.Text, true, _stopping.Token);

    private async Task ReceiveAsync()
    {
        var bytes = new byte[16 * 1024];
        var pending = new StringBuilder();
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                using var frame = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(bytes, _stopping.Token);
                    if (received.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Synthetic Hub closed.");
                    frame.Write(bytes, 0, received.Count);
                    if (frame.Length > 1024 * 1024) throw new InvalidOperationException("Synthetic Hub frame exceeded the bounded limit.");
                } while (!received.EndOfMessage);
                pending.Append(Encoding.UTF8.GetString(frame.ToArray()));
                if (pending.Length > 1024 * 1024) throw new InvalidOperationException("Synthetic Hub message exceeded the bounded limit.");
                var records = pending.ToString().Split('\u001e');
                pending.Clear().Append(records[^1]);
                foreach (var record in records[..^1])
                {
                    using var json = JsonDocument.Parse(record);
                    var root = json.RootElement;
                    if (!root.TryGetProperty("type", out var type))
                    {
                        if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("Synthetic Hub handshake denied.");
                        _handshake.TrySetResult();
                    }
                    else if (type.GetInt32() == 3 && root.TryGetProperty("invocationId", out var id) &&
                             _invocations.TryGetValue(id.GetString()!, out var invocation))
                    {
                        if (root.TryGetProperty("error", out _)) invocation.TrySetException(new InvalidOperationException("Synthetic Hub invocation denied."));
                        else invocation.TrySetResult(root.GetProperty("result").Clone());
                    }
                    else if (type.GetInt32() == 1 && root.GetProperty("target").GetString() == "DurableEvent")
                    {
                        var envelope = root.GetProperty("arguments")[0];
                        var eventId = envelope.GetProperty("eventId").GetGuid();
                        var eventType = envelope.GetProperty("eventType").GetString();
                        if (eventType is null || !envelope.TryGetProperty("payloadSchemaVersion", out var schema))
                            throw new InvalidOperationException("Synthetic durable envelope metadata unavailable.");
                        var metadata = (eventType, schema.GetInt32());
                        if (_eventMetadata.TryGetValue(eventId, out var previous) && previous != metadata)
                            throw new InvalidOperationException("Synthetic durable replay changed envelope metadata.");
                        _eventMetadata[eventId] = metadata;
                        _events.AddOrUpdate(eventId, 1, (_, count) => count + 1);
                    }
                    else if (type.GetInt32() == 7) throw new InvalidOperationException("Synthetic Hub connection invalidated.");
                }
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or InvalidOperationException or JsonException)
        {
            _handshake.TrySetException(new InvalidOperationException("Synthetic Hub transport failed."));
            foreach (var invocation in _invocations.Values)
                invocation.TrySetException(new InvalidOperationException("Synthetic Hub transport failed."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _socket.Abort();
        if (_receiver is not null) await _receiver;
        _socket.Dispose();
        _stopping.Dispose();
    }
}
