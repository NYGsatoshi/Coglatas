using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Coglatas.SyntheticRecoveryAcceptance;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PrivateConfig(string Mode, string ConnectionString, string Origin, string StorageRoot,
    string Password, Guid PendingEventId, string ReceiptPath);

internal sealed record Scope(Guid Tenant, Guid Workspace, Guid Project, Guid Conversation, Guid File, Guid Task);

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string RecoveryChannelTitle = "Synthetic recovery supported private channel";
    private static readonly HashSet<string> Modes = ["seed", "source-authority", "retained-session", "missing-keys", "restored-keys", "restored-authority", "continuation", "restart", "rollback-authority"];
    private static readonly Dictionary<string, string> Controls = [];
    private static string Phase = "ADMISSION";
    private sealed record HttpObservation(string Operation, int ExpectedStatus, int ActualStatus);
    private sealed record PredicateObservation(string Operation, bool Satisfied);
    private static HttpObservation? LastHttp;
    private static PredicateObservation? LastPredicate;
    private static void ObserveHttp(HttpStatusCode expected, HttpStatusCode actual) => LastHttp = new(Phase, (int)expected, (int)actual);
    private static void Check(string operation, bool satisfied)
    {
        Phase = operation;
        LastPredicate = new(operation, satisfied);
        Require(satisfied);
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("SYNTHETIC_CONTROL_REJECTED"); }
    private static string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string DigestJson(object value) => Digest(JsonSerializer.SerializeToUtf8Bytes(value, Json));

    public static async Task<int> Main(string[] args)
    {
        PrivateConfig? config = null;
        string? receiptPath = null;
        var success = false;
        string? failureKind = null;
        var assemblies = new Dictionary<string, string>();
        try
        {
            Require(args.Length == 1 && Environment.GetEnvironmentVariable("COGLATAS_RECOVERY_RUNNER_ENVIRONMENT") == "github-hosted"
                && Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") == "pull_request"
                && Environment.GetEnvironmentVariable("RUNNER_OS") == "Linux");
            var path = Path.GetFullPath(args[0]);
            var root = Path.GetDirectoryName(path)!;
            var temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
            Require(Path.GetDirectoryName(root) == temporary && Path.GetFileName(root).StartsWith("cga-", StringComparison.Ordinal)
                && new DirectoryInfo(root).LinkTarget is null && new FileInfo(path).LinkTarget is null
                && new FileInfo(path).Length is > 0 and <= 16384);
            config = JsonSerializer.Deserialize<PrivateConfig>(await File.ReadAllBytesAsync(path), Json)!;
            Require(config is not null && Modes.Contains(config.Mode) && config.Password.Length is >= 32 and <= 128
                && config.PendingEventId != Guid.Empty && Path.GetDirectoryName(Path.GetFullPath(config.ReceiptPath)) == root
                && !File.Exists(config.ReceiptPath)
                && config.ReceiptPath == Path.Combine(root, "tool-" + config.Mode + ".json"));
            var connection = new NpgsqlConnectionStringBuilder(config.ConnectionString);
            var sourceMode = config.Mode is "seed" or "source-authority" or "rollback-authority";
            Require(connection.Host == Path.Combine(root, "e", sourceMode ? "source-socket" : "target-socket"));
            Require(connection.Database == "recovery" && connection.Username == "postgres" && string.IsNullOrEmpty(connection.Password)
                && connection.Port == 5432
                && connection.Keys.Cast<string>().Select(key => key.ToLowerInvariant()).ToHashSet().SetEquals(["host", "database", "username"]));
            var expectedState = sourceMode ? "source-state" : config.Mode == "missing-keys" ? "missing-key-state" : "restored-state";
            Require(config.StorageRoot == Path.Combine(root, "e", expectedState, "files"));
            var origin = new Uri(config.Origin, UriKind.Absolute);
            Require(origin.Scheme == "http" && origin.Host == "127.0.0.1" && origin.Port is > 0 and <= 65535
                && origin.AbsolutePath == "/" && origin.UserInfo == "" && origin.Query == "" && origin.Fragment == "");
            receiptPath = config.ReceiptPath;
            await using var db = Context(config.ConnectionString);
            if (config.Mode == "seed")
                await SeedAsync(db, config);
            else if (config.Mode is "retained-session" or "missing-keys" or "restored-keys")
                await KeyBoundaryAsync(db, config);
            else
            {
                await AuthorityAsync(db, config);
                if (config.Mode == "continuation") await ContinuationAsync(db, config);
                if (config.Mode == "restart") await RestartAsync(db, config);
            }
            foreach (var assembly in new[] { typeof(Program).Assembly, typeof(Coglatas.Web.Controllers.AuthController).Assembly,
                typeof(AppDbContext).Assembly, typeof(OutboxReplayService).Assembly, typeof(OutboxEvent).Assembly })
                assemblies[Path.GetFileName(assembly.Location)] = Digest(await File.ReadAllBytesAsync(assembly.Location));
            Phase = "COMPLETE";
            success = true;
        }
        catch (Exception error)
        {
            failureKind = error switch {
                HttpRequestException => "HTTP_REQUEST", TaskCanceledException => "DEADLINE", JsonException => "JSON_DATA",
                DbUpdateException => "DATABASE", NpgsqlException => "DATABASE", IOException => "IO",
                InvalidOperationException => "CONTROL_REJECTED", _ => "UNCLASSIFIED" };
            // Exception values/HTTP bodies/SQL/keys/cookies remain private and are never serialized.
        }
        var result = new { schemaVersion = 1, mode = config?.Mode is string mode && Modes.Contains(mode) ? mode : "REJECTED",
            status = success ? "PASS" : "FAIL", phase = Phase, controls = Controls, assemblies,
            lastHttp = LastHttp, lastPredicate = LastPredicate, failureKind,
            transportObservationSeconds = config?.Mode is "continuation" or "restart" ? 15 : 0,
            webSocketDeliveryGuarantee = "BOUNDED_CONTROL_OBSERVATION_ONLY",
            databaseAuthorityScope = "SYNTHETIC_POSTGRES_BOOTSTRAP_ROLE_ONLY", formalRecoveryAcceptance = "NOT_GRANTED" };
        if (receiptPath is not null)
        {
            try
            {
                using var output = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(output, result, Json);
            }
            catch { return 1; }
        }
        return success ? 0 : 1;
    }

    private static AppDbContext Context(string connection)
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options, tenant);
    }

    private static async Task<Scope> ScopeAsync(AppDbContext db, string slug)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(row => row.Slug == slug);
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(row => row.TenantId == tenant.Id);
        var project = await db.Projects.AsNoTracking().SingleAsync(row => row.TenantId == tenant.Id);
        var conversation = await db.Conversations.AsNoTracking().SingleAsync(row => row.TenantId == tenant.Id
            && row.WorkspaceId == workspace.Id && row.ProjectId == project.Id
            && row.Type == ConversationType.ProjectChannel && row.Title == RecoveryChannelTitle);
        var file = await db.Attachments.AsNoTracking().SingleAsync(row => row.WorkspaceId == workspace.Id);
        var task = await db.TaskItems.AsNoTracking().SingleAsync(row => row.ProjectId == project.Id);
        return new Scope(tenant.Id, workspace.Id, project.Id, conversation.Id, file.FileObjectId, task.Id);
    }

    private static OutboxEvent Event(Guid id, Scope scope, DateTimeOffset? due = null)
    {
        const string type = "Projects.TaskChanged.v1";
        var envelope = new DurableEventEnvelope(id, type, 1, DateTimeOffset.UtcNow, scope.Tenant,
            "Task", scope.Task, 1, RealtimeActor.System(), null, null,
            JsonSerializer.SerializeToElement(new { taskId = scope.Task, projectId = scope.Project, taskVersion = 1, requiresRefetch = true }));
        return new OutboxEvent(id) { TenantId = scope.Tenant, EventType = type, PayloadSchemaVersion = 1,
            AggregateType = "Task", AggregateId = scope.Task, AggregateVersion = 1, OccurredAt = envelope.OccurredAt,
            PayloadJson = JsonSerializer.Serialize(envelope, Json),
            RoutingJson = JsonSerializer.Serialize(new[] { new RealtimeRoutingTarget(RealtimeSubscriptionType.Project, scope.Project) }, Json),
            NextAttemptAt = due };
    }

    private static async Task AddSupportedChannelAsync(AppDbContext db, string tenantSlug)
    {
        var tenant = await db.Tenants.SingleAsync(row => row.Slug == tenantSlug);
        var workspace = await db.Workspaces.SingleAsync(row => row.TenantId == tenant.Id);
        var project = await db.Projects.SingleAsync(row => row.TenantId == tenant.Id && row.WorkspaceId == workspace.Id);
        Require(!await db.Conversations.AnyAsync(row => row.TenantId == tenant.Id && row.Title == RecoveryChannelTitle));
        // Match current Main's supported-channel transport fixture. Preserve all original
        // SEC-02 ProjectLinked canaries, which intentionally exercise a disabled legacy type.
        var alpha = tenantSlug == SecurityCiFixtureSeed.TenantASlug;
        var owner = alpha ? SecurityCiFixtureSeed.TenantAOwnerUserId : SecurityCiFixtureSeed.TenantBOwnerUserId;
        var now = DateTimeOffset.UtcNow;
        var channel = new Conversation {
            TenantId = tenant.Id, WorkspaceId = workspace.Id, ProjectId = project.Id,
            Type = ConversationType.ProjectChannel, Visibility = ConversationVisibility.Private,
            Title = RecoveryChannelTitle, CreatedByUserId = owner };
        db.Conversations.Add(channel);
        db.ConversationMembers.Add(new ConversationMember {
            TenantId = tenant.Id, ConversationId = channel.Id, UserId = owner,
            Role = ConversationMemberRole.Admin, CanRead = true, CanPost = true,
            CanManageMembers = true, CanCreateThread = true, JoinedAt = now });
        if (alpha)
        {
            db.ConversationMembers.Add(new ConversationMember {
                TenantId = tenant.Id, ConversationId = channel.Id, UserId = SecurityCiFixtureSeed.TenantAMemberUserId,
                Role = ConversationMemberRole.Member, CanRead = true, CanPost = true,
                CanCreateThread = true, JoinedAt = now });
            db.ConversationMembers.Add(new ConversationMember {
                TenantId = tenant.Id, ConversationId = channel.Id, UserId = SecurityCiFixtureSeed.TenantARestrictedUserId,
                Role = ConversationMemberRole.ReadOnly, CanRead = true, CanPost = false,
                CanManageMembers = false, CanCreateThread = false, JoinedAt = now });
        }
        db.Messages.Add(new Message {
            TenantId = tenant.Id, WorkspaceId = workspace.Id, ConversationId = channel.Id, AuthorUserId = owner,
            Body = alpha ? "SEC02_ALPHA_MESSAGE_CANARY_DO_NOT_LEAK" : "SEC02_BETA_MESSAGE_CANARY_DO_NOT_LEAK",
            Version = 1, CreatedAt = now });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAsync(AppDbContext db, PrivateConfig config)
    {
        Phase = "SEED";
        var storage = new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = config.StorageRoot }));
        await SecurityCiFixtureSeed.SeedAsync(db, new Pbkdf2PasswordHasher(), storage, config.Password);
        await AddSupportedChannelAsync(db, SecurityCiFixtureSeed.TenantASlug);
        await AddSupportedChannelAsync(db, SecurityCiFixtureSeed.TenantBSlug);
        var alpha = await ScopeAsync(db, SecurityCiFixtureSeed.TenantASlug);
        Require(!await db.OutboxEvents.AnyAsync(row => row.Id == config.PendingEventId));
        db.OutboxEvents.Add(Event(config.PendingEventId, alpha, DateTimeOffset.UtcNow.AddHours(1)));
        await db.SaveChangesAsync();
        var pending = await db.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == config.PendingEventId);
        Require(pending.Status == OutboxEventStatus.Pending && pending.AttemptCount == 0
            && pending.NextAttemptAt > DateTimeOffset.UtcNow.AddMinutes(30));
        Controls["preBackupFixture"] = "PASS";
        Controls["pendingEnvelopeSource"] = "SUPPLIED_CANONICAL_TEST_EVENT";
    }

    private sealed class Client : IDisposable
    {
        public CookieContainer Cookies { get; } = new();
        public HttpClient Http { get; }
        public Guid SessionId { get; set; }
        public string? CsrfHeader { get; private set; }
        public Client(Uri origin, string tenant)
        {
            Cookies.Add(origin, new Cookie("coglatas_tenant", tenant, "/"));
            Http = new HttpClient(new HttpClientHandler { CookieContainer = Cookies, AllowAutoRedirect = false, UseProxy = false })
                { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        }
        public async Task CsrfAsync()
        {
            using var response = await Http.GetAsync("/api/security/csrf-token");
            ObserveHttp(HttpStatusCode.OK, response.StatusCode);
            Require(response.StatusCode == HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var header = body.RootElement.GetProperty("headerName").GetString()!;
            CsrfHeader = header;
            Http.DefaultRequestHeaders.Remove(header);
            Http.DefaultRequestHeaders.Add(header, body.RootElement.GetProperty("token").GetString());
        }
        public void Dispose() => Http.Dispose();
    }

    private static async Task<Client> LoginAsync(PrivateConfig config, string tenant, string email)
    {
        var client = new Client(new Uri(config.Origin), tenant);
        try
        {
            var prefix = Phase;
            Phase = prefix + "_CSRF_BEFORE";
            await client.CsrfAsync();
            Phase = prefix + "_LOGIN";
            using var login = await client.Http.PostAsJsonAsync("/api/auth/login", new { email, password = config.Password }, Json);
            ObserveHttp(HttpStatusCode.OK, login.StatusCode);
            Require(login.StatusCode == HttpStatusCode.OK);
            Phase = prefix + "_CSRF_AFTER";
            await client.CsrfAsync();
            Phase = prefix + "_ME";
            using var me = await client.Http.GetAsync("/api/auth/me");
            ObserveHttp(HttpStatusCode.OK, me.StatusCode);
            Require(me.StatusCode == HttpStatusCode.OK);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task<Response> GetAsync(Client client, string path, HttpStatusCode expected)
    {
        using var response = await client.Http.GetAsync(path);
        ObserveHttp(expected, response.StatusCode);
        Require(response.StatusCode == expected);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Require(bytes.Length <= 2 * 1024 * 1024);
        return new Response(bytes);
    }
    private sealed record Response(byte[] Bytes)
    {
        public bool Has(string value) => Encoding.UTF8.GetString(Bytes).Contains(value, StringComparison.Ordinal);
    }

    private static async Task AuthorityAsync(AppDbContext db, PrivateConfig config)
    {
        Phase = "AUTH_SCOPE";
        var alpha = await ScopeAsync(db, SecurityCiFixtureSeed.TenantASlug);
        var beta = await ScopeAsync(db, SecurityCiFixtureSeed.TenantBSlug);
        Phase = "AUTH_OWNER";
        // SessionId is deliberately not a public LoginResponse wire field. Bind this
        // isolated source login by its exact persisted ID-set delta, never by ordering.
        var sourceSessionsBefore = config.Mode == "source-authority"
            ? await db.Sessions.AsNoTracking().Where(row => row.UserId == SecurityCiFixtureSeed.TenantAOwnerUserId)
                .Select(row => row.Id).ToArrayAsync() : null;
        using var owner = await LoginAsync(config, SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
        if (sourceSessionsBefore is not null)
        {
            var after = await db.Sessions.AsNoTracking().Where(row => row.UserId == SecurityCiFixtureSeed.TenantAOwnerUserId)
                .Select(row => row.Id).ToArrayAsync();
            var added = after.Except(sourceSessionsBefore).ToArray();
            Require(sourceSessionsBefore.All(after.Contains) && added.Length == 1);
            owner.SessionId = added.Single();
        }
        Phase = "AUTH_READER";
        using var reader = await LoginAsync(config, SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantARestrictedEmail);
        Phase = "AUTH_BETA";
        using var foreign = await LoginAsync(config, SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
        Phase = "AUTH_ALPHA_MESSAGES";
        Check("AUTH_ALPHA_MESSAGE_BODY", (await GetAsync(owner, $"/api/conversations/{alpha.Conversation}/messages", HttpStatusCode.OK)).Has("SEC02_ALPHA_MESSAGE_CANARY_DO_NOT_LEAK"));
        Phase = "AUTH_BETA_MESSAGES";
        Check("AUTH_BETA_MESSAGE_BODY", (await GetAsync(foreign, $"/api/conversations/{beta.Conversation}/messages", HttpStatusCode.OK)).Has("SEC02_BETA_MESSAGE_CANARY_DO_NOT_LEAK"));
        Phase = "AUTH_FOREIGN_MESSAGES";
        var denied = await GetAsync(owner, $"/api/conversations/{beta.Conversation}/messages", HttpStatusCode.BadRequest);
        Check("AUTH_FOREIGN_MESSAGE_REDACTION", !denied.Has("SEC02_BETA_MESSAGE_CANARY_DO_NOT_LEAK") && !denied.Has(beta.Conversation.ToString()));
        Phase = "AUTH_ALPHA_FILE";
        var alphaFile = await GetAsync(owner, $"/api/files/{alpha.File}/download", HttpStatusCode.OK);
        Check("AUTH_ALPHA_FILE_BYTES", alphaFile.Bytes.SequenceEqual(Encoding.UTF8.GetBytes("SEC02_ALPHA_FILE_CANARY_DO_NOT_LEAK\n")));
        Phase = "AUTH_BETA_FILE";
        var betaFile = await GetAsync(foreign, $"/api/files/{beta.File}/download", HttpStatusCode.OK);
        Check("AUTH_BETA_FILE_BYTES", betaFile.Bytes.SequenceEqual(Encoding.UTF8.GetBytes("SEC02_BETA_FILE_CANARY_DO_NOT_LEAK\n")));
        Phase = "AUTH_FOREIGN_FILE";
        var deniedFile = await GetAsync(owner, $"/api/files/{beta.File}/download", HttpStatusCode.BadRequest);
        Check("AUTH_FOREIGN_FILE_REDACTION", !deniedFile.Has("SEC02_BETA_FILE_CANARY_DO_NOT_LEAK") && !deniedFile.Has("sec02-beta-private.txt"));
        Phase = "AUTH_READER_MESSAGES";
        await GetAsync(reader, $"/api/conversations/{alpha.Conversation}/messages", HttpStatusCode.OK);
        Phase = "AUTH_READER_ROLE";
        var member = await db.ConversationMembers.AsNoTracking().SingleAsync(row =>
            row.ConversationId == alpha.Conversation && row.UserId == SecurityCiFixtureSeed.TenantARestrictedUserId);
        Check("AUTH_READER_ROLE_BINDING", member.Role == ConversationMemberRole.ReadOnly && !member.CanPost);
        var request = Guid.NewGuid();
        var before = await db.Messages.CountAsync(row => row.ConversationId == alpha.Conversation);
        Phase = "AUTH_READER_POST";
        using var post = await reader.Http.PostAsJsonAsync($"/api/conversations/{alpha.Conversation}/messages",
            new { body = "Synthetic denied recovery write", clientRequestId = request }, Json);
        ObserveHttp(HttpStatusCode.Forbidden, post.StatusCode);
        Require(post.StatusCode == HttpStatusCode.Forbidden);
        Check("AUTH_READER_NO_MESSAGE", !await db.Messages.AnyAsync(row => row.ClientRequestId == request));
        Check("AUTH_READER_COUNT_UNCHANGED", await db.Messages.CountAsync(row => row.ConversationId == alpha.Conversation) == before);
        Controls["authenticatedOwnersPositive"] = "PASS";
        Controls["crossTenantReadDenied"] = "PASS";
        Controls["readOnlyViewPositiveWriteDenied"] = "PASS";
        Controls["filePositiveAndCrossTenantDenial"] = "PASS";
        if (config.Mode == "source-authority") await CaptureOwnerSessionAsync(db, config, owner);
        if (config.Mode == "restored-authority") await CsrfBoundaryAsync(db, config, owner);
    }



    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record CapturedCookie(string Name, string Value, string Path, string Domain,
        bool Secure, bool HttpOnly, DateTime Expires, bool Discard, int Version);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SourceSessionCheckpoint(int SchemaVersion, string SourceSha, string RunId, string RunAttempt,
        Guid UserId, Guid SessionId, DateTimeOffset CapturedAtUtc, IReadOnlyList<CapturedCookie> Cookies);

    private static string CookiePath(PrivateConfig config) =>
        Path.Combine(Path.GetDirectoryName(config.ReceiptPath)!, "owner-session.json");

    private static async Task CaptureOwnerSessionAsync(AppDbContext db, PrivateConfig config, Client owner)
    {
        Phase = "SOURCE_COOKIE_ME";
        using var response = await owner.Http.GetAsync("/api/auth/me");
        ObserveHttp(HttpStatusCode.OK, response.StatusCode);
        Require(response.StatusCode == HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check("SOURCE_COOKIE_SUBJECT", body.RootElement.GetProperty("userId").GetGuid() == SecurityCiFixtureSeed.TenantAOwnerUserId);
        Check("SOURCE_COOKIE_SESSION_ACTIVE", await db.Sessions.AsNoTracking().AnyAsync(row => row.Id == owner.SessionId
            && row.UserId == SecurityCiFixtureSeed.TenantAOwnerUserId && row.RevokedAt == null && row.ExpiresAt > DateTimeOffset.UtcNow));
        var cookies = owner.Cookies.GetAllCookies().Cast<Cookie>().Select(cookie => new CapturedCookie(
            cookie.Name, cookie.Value, cookie.Path, cookie.Domain, cookie.Secure, cookie.HttpOnly,
            cookie.Expires, cookie.Discard, cookie.Version)).ToArray();
        Require(cookies.Length is >= 2 and <= 16);
        var checkpoint = new SourceSessionCheckpoint(1, Environment.GetEnvironmentVariable("GITHUB_SHA")!,
            Environment.GetEnvironmentVariable("GITHUB_RUN_ID")!, Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT")!,
            SecurityCiFixtureSeed.TenantAOwnerUserId, owner.SessionId, DateTimeOffset.UtcNow, cookies);
        using var output = new FileStream(CookiePath(config), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, checkpoint, Json);
        await output.FlushAsync();
        output.Flush(flushToDisk: true);
        Controls["preBackupOwnerCookieCaptured"] = "PASS";
    }

    private static async Task<(Client Client, SourceSessionCheckpoint Checkpoint)> RetainedClientAsync(AppDbContext db, PrivateConfig config)
    {
        Phase = "RETAINED_COOKIE_ADMISSION";
        var path = CookiePath(config);
        Require(new FileInfo(path).LinkTarget is null && new FileInfo(path).Length is > 0 and <= 65536);
        var checkpoint = JsonSerializer.Deserialize<SourceSessionCheckpoint>(await File.ReadAllBytesAsync(path), Json)!;
        Require(checkpoint.SchemaVersion == 1
            && checkpoint.SourceSha == Environment.GetEnvironmentVariable("GITHUB_SHA")
            && checkpoint.RunId == Environment.GetEnvironmentVariable("GITHUB_RUN_ID")
            && checkpoint.RunAttempt == Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT")
            && checkpoint.UserId == SecurityCiFixtureSeed.TenantAOwnerUserId && checkpoint.SessionId != Guid.Empty
            && checkpoint.CapturedAtUtc.Offset == TimeSpan.Zero && checkpoint.Cookies.Count is >= 2 and <= 16);
        Require(await db.Sessions.AsNoTracking().AnyAsync(row => row.Id == checkpoint.SessionId
            && row.UserId == checkpoint.UserId && row.RevokedAt == null && row.ExpiresAt > DateTimeOffset.UtcNow));
        var client = new Client(new Uri(config.Origin), SecurityCiFixtureSeed.TenantASlug);
        try
        {
            foreach (var cookie in checkpoint.Cookies)
            {
                Require(Regex.IsMatch(cookie.Name, @"^[A-Za-z0-9_.-]{1,128}$")
                    && cookie.Value.Length is > 0 and <= 8192
                    && !cookie.Value.Any(character => character is '\r' or '\n' or '\0')
                    && cookie.Domain == "127.0.0.1" && cookie.Path == "/" && cookie.Version == 0
                    && (cookie.Expires == DateTime.MinValue || cookie.Expires.ToUniversalTime() > DateTime.UtcNow));
                client.Cookies.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain) {
                    Secure = cookie.Secure, HttpOnly = cookie.HttpOnly, Expires = cookie.Expires,
                    Discard = cookie.Discard, Version = cookie.Version });
            }
            client.SessionId = checkpoint.SessionId;
            return (client, checkpoint);
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task KeyBoundaryAsync(AppDbContext db, PrivateConfig config)
    {
        var retained = await RetainedClientAsync(db, config);
        using var client = retained.Client;
        if (config.Mode == "missing-keys")
        {
            Phase = "MISSING_KEYS_HEALTH";
            await GetAsync(client, "/health/ready", HttpStatusCode.OK);
            Phase = "MISSING_KEYS_OLD_ME";
            using var denied = await client.Http.GetAsync("/api/auth/me");
            ObserveHttp(HttpStatusCode.Unauthorized, denied.StatusCode);
            Check("MISSING_KEYS_OLD_COOKIE_DENIED", denied.StatusCode == HttpStatusCode.Unauthorized);
            // Use another existing actor: this positive cannot revoke the retained Alpha session.
            Phase = "MISSING_BETA";
            using var fresh = await LoginAsync(config, SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            Phase = "MISSING_KEYS_FRESH_BETA_ME";
            using var response = await fresh.Http.GetAsync("/api/auth/me");
            ObserveHttp(HttpStatusCode.OK, response.StatusCode);
            Require(response.StatusCode == HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check("MISSING_KEYS_FRESH_BETA_SUBJECT", body.RootElement.GetProperty("userId").GetGuid() == SecurityCiFixtureSeed.TenantBOwnerUserId);
            Controls["oldProtectedCookieDeniedWithMissingKeys"] = "PASS";
            Controls["healthyMissingKeyAppPositive"] = "PASS";
            Controls["freshBetaAuthenticationWithNewKeys"] = "PASS";
        }
        else
        {
            Phase = config.Mode == "retained-session" ? "RETAINED_SESSION_ME" : "RESTORED_KEYS_ME";
            using var response = await client.Http.GetAsync("/api/auth/me");
            ObserveHttp(HttpStatusCode.OK, response.StatusCode);
            Require(response.StatusCode == HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check("RETAINED_SESSION_SUBJECT", body.RootElement.GetProperty("userId").GetGuid() == retained.Checkpoint.UserId);
            var alpha = await ScopeAsync(db, SecurityCiFixtureSeed.TenantASlug);
            Phase = config.Mode == "retained-session" ? "RETAINED_SESSION_MESSAGES" : "RESTORED_KEYS_MESSAGES";
            var messages = await GetAsync(client, $"/api/conversations/{alpha.Conversation}/messages", HttpStatusCode.OK);
            Check("RETAINED_SESSION_PROTECTED_READ", messages.Has("SEC02_ALPHA_MESSAGE_CANARY_DO_NOT_LEAK"));
            Controls[config.Mode == "retained-session" ? "preBackupProtectedCookieAfterRestore" : "sameProtectedCookieAfterOriginalKeysRestored"] = "PASS";
        }
    }

    private static async Task CsrfBoundaryAsync(AppDbContext db, PrivateConfig config, Client owner)
    {
        var before = await db.Users.AsNoTracking().Where(row => row.Id == SecurityCiFixtureSeed.TenantAOwnerUserId)
            .Select(row => row.PasswordHash).SingleAsync();
        var request = new { currentPassword = "SyntheticWrongPasswordDifferent!", newPassword = "RecoveryNeverAppliedPassword1!" };
        Phase = "CSRF_VALID_BAD_PASSWORD";
        using (var valid = await owner.Http.PostAsJsonAsync("/api/auth/change-password", request, Json))
        {
            ObserveHttp(HttpStatusCode.BadRequest, valid.StatusCode);
            Require(valid.StatusCode == HttpStatusCode.BadRequest);
            using var body = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
            Check("CSRF_VALID_HANDLER_REACHED", body.RootElement.GetProperty("error").GetString() == "Current password is incorrect.");
        }
        Require(owner.CsrfHeader is not null && owner.Http.DefaultRequestHeaders.Remove(owner.CsrfHeader));
        Phase = "CSRF_MISSING_TOKEN";
        using (var missing = await owner.Http.PostAsJsonAsync("/api/auth/change-password", request, Json))
        {
            ObserveHttp(HttpStatusCode.Forbidden, missing.StatusCode);
            Require(missing.StatusCode == HttpStatusCode.Forbidden);
        }
        var after = await db.Users.AsNoTracking().Where(row => row.Id == SecurityCiFixtureSeed.TenantAOwnerUserId)
            .Select(row => row.PasswordHash).SingleAsync();
        Check("CSRF_CREDENTIAL_UNCHANGED", before == after);
        Controls["sameRouteValidCsrfHandlerAndMissingTokenDenial"] = "PASS";
        Controls["csrfControlsDoNotChangeCredential"] = "PASS";
    }

    private static async Task<RecoverySocket> ConnectAsync(Client client)
    {
        using var response = await client.Http.PostAsync("/hubs/app/negotiate?negotiateVersion=1", null);
        ObserveHttp(HttpStatusCode.OK, response.StatusCode);
        Require(response.StatusCode == HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("connectionToken").GetString()!;
        var snapshot = new CookieContainer();
        foreach (Cookie cookie in client.Cookies.GetAllCookies()) snapshot.Add(cookie);
        var socket = new RecoverySocket();
        socket.Options.Cookies = snapshot;
        var address = new UriBuilder(client.Http.BaseAddress!) { Scheme = "ws", Path = "/hubs/app", Query = "id=" + Uri.EscapeDataString(token) }.Uri;
        try { await socket.ConnectAsync(address); return socket; }
        catch { await socket.DisposeAsync(); throw; }
    }

    private static string ImmutableEventDigest(OutboxEvent item) => DigestJson(new {
        item.Id, item.TenantId, item.EventType, item.PayloadSchemaVersion, item.AggregateType,
        item.AggregateId, item.AggregateVersion, item.OccurredAt, item.PayloadJson, item.RoutingJson });

    private static string DeliveredDigest(OutboxEvent item) => DigestJson(new {
        item.Status, item.AttemptCount, item.DeliveredAt, item.NextAttemptAt, item.LastErrorCode,
        item.LockOwner, item.LockedAt, item.LockToken, immutable = ImmutableEventDigest(item) });

    private static async Task<OutboxEvent> WaitDeliveredAsync(PrivateConfig config, Guid id)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var observe = Context(config.ConnectionString);
            var item = await observe.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == id);
            Require(item.Status is not OutboxEventStatus.DeadLetter and not OutboxEventStatus.Cancelled);
            if (item.Status == OutboxEventStatus.Delivered)
            {
                Require(item.DeliveredAt.HasValue && item.LastErrorCode is null);
                return item;
            }
            await Task.Delay(50);
        }
        throw new InvalidOperationException("SYNTHETIC_DELIVERY_DEADLINE");
    }

    private static async Task<Guid> SentinelAsync(AppDbContext db, Scope scope)
    {
        var id = Guid.NewGuid();
        db.OutboxEvents.Add(Event(id, scope));
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> PostMessageAsync(Client owner, Scope alpha, Guid request, string body)
    {
        using var response = await owner.Http.PostAsJsonAsync($"/api/conversations/{alpha.Conversation}/messages",
            new { body, clientRequestId = request }, Json);
        ObserveHttp(HttpStatusCode.OK, response.StatusCode);
        Require(response.StatusCode == HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = payload.RootElement.GetProperty("id").GetGuid();
        Require(id != Guid.Empty);
        return id;
    }

    private static async Task<OutboxEvent> MessageEffectsAsync(AppDbContext db, Scope alpha, Guid message, Guid request)
    {
        Require(await db.Messages.AsNoTracking().CountAsync(row => row.Id == message && row.ClientRequestId == request
            && row.ConversationId == alpha.Conversation && row.AuthorUserId == SecurityCiFixtureSeed.TenantAOwnerUserId) == 1);
        Require(await db.AuditLogs.AsNoTracking().CountAsync(row => row.TenantId == alpha.Tenant
            && row.ActorUserId == SecurityCiFixtureSeed.TenantAOwnerUserId
            && row.Action == "communication.message_posted" && row.EntityId == message) == 1);
        var item = await db.OutboxEvents.AsNoTracking().SingleAsync(row => row.TenantId == alpha.Tenant
            && row.AggregateId == message && row.EventType == "Messaging.MessageCreated.v1");
        return item;
    }

    private sealed record RowSnapshot(int Rows, string ScalarSha256);

    private static async Task<Dictionary<string, RowSnapshot>> AtomicRowsAsync(string connectionString)
    {
        var result = new Dictionary<string, RowSnapshot>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var table in new[] { "messages", "conversation_members", "outbox_events", "audit_logs" })
        {
            await using var command = connection.CreateCommand();
            // Entire persisted rows, including every immutable/mutable scalar. No values leave memory.
            command.CommandText = $"SELECT to_jsonb(r)::text FROM {table} AS r ORDER BY r.\"Id\"";
            await using var reader = await command.ExecuteReaderAsync();
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var rows = 0;
            var size = 0;
            while (await reader.ReadAsync())
            {
                var bytes = Encoding.UTF8.GetBytes(reader.GetString(0) + "\n");
                rows++;
                size += bytes.Length;
                Require(rows <= 4096 && size <= 8 * 1024 * 1024);
                digest.AppendData(bytes);
            }
            result[table] = new RowSnapshot(rows, Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant());
        }
        return result;
    }

    private static async Task AuditFailureAsync(AppDbContext db, Client owner, Scope alpha)
    {
        Phase = "AUDIT_FAILURE_INJECTION";
        Require(!await db.AuditLogs.AnyAsync(row => row.Action == "communication.message_posted"));
        var denied = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE audit_logs ADD CONSTRAINT synthetic_recovery_required_audit_failure CHECK (\"Action\" <> 'communication.message_posted')");
        try
        {
            var before = await AtomicRowsAsync(db.Database.GetConnectionString()!);
            using var response = await owner.Http.PostAsJsonAsync($"/api/conversations/{alpha.Conversation}/messages",
                new { body = "Synthetic required audit failure", clientRequestId = denied }, Json);
            ObserveHttp(HttpStatusCode.InternalServerError, response.StatusCode);
            Require(response.StatusCode == HttpStatusCode.InternalServerError);
            await using var observe = Context(db.Database.GetConnectionString()!);
            Require(!await observe.Messages.AnyAsync(row => row.ClientRequestId == denied)
                && !await observe.AuditLogs.AnyAsync(row => row.Action == "communication.message_posted"));
            var after = await AtomicRowsAsync(db.Database.GetConnectionString()!);
            Require(before.Count == after.Count && before.All(pair => after[pair.Key] == pair.Value));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_logs DROP CONSTRAINT synthetic_recovery_required_audit_failure");
        }
        await using var verify = db.Database.GetDbConnection().CreateCommand();
        verify.CommandText = "SELECT count(*) FROM pg_constraint WHERE conname = 'synthetic_recovery_required_audit_failure'";
        if (verify.Connection!.State != System.Data.ConnectionState.Open) await verify.Connection.OpenAsync();
        Require(Convert.ToInt64(await verify.ExecuteScalarAsync()) == 0);
        Controls["requiredAuditFailureAtomicRollback"] = "PASS";
    }

    private sealed record ContinuationState(Guid MessageId, Guid MessageEventId, Guid ClientRequestId,
        string PendingDeliveredDigest, string MessageDeliveredDigest);

    private static async Task ContinuationAsync(AppDbContext db, PrivateConfig config)
    {
        Phase = "OUTBOX_CONTINUATION";
        var alpha = await ScopeAsync(db, SecurityCiFixtureSeed.TenantASlug);
        var beta = await ScopeAsync(db, SecurityCiFixtureSeed.TenantBSlug);
        Phase = "CONT_OWNER";
        using var owner = await LoginAsync(config, SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
        Phase = "CONT_MEMBER";
        using var member = await LoginAsync(config, SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
        Phase = "CONT_BETA";
        using var foreign = await LoginAsync(config, SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
        Phase = "CONT_A_NEGOTIATE";
        await using var a = await ConnectAsync(owner);
        Phase = "CONT_B_NEGOTIATE";
        await using var b = await ConnectAsync(member);
        Phase = "CONT_C_NEGOTIATE";
        await using var c = await ConnectAsync(foreign);
        Require(await a.SubscribeAsync("SubscribeProject", alpha.Project)
            && await b.SubscribeAsync("SubscribeProject", alpha.Project)
            && await c.SubscribeAsync("SubscribeProject", beta.Project)
            && await a.SubscribeAsync("SubscribeConversation", alpha.Conversation)
            && await b.SubscribeAsync("SubscribeConversation", alpha.Conversation)
            && await c.SubscribeAsync("SubscribeConversation", beta.Conversation));
        Phase = "CONT_PENDING_ACTIVATION";
        var pending = await db.OutboxEvents.SingleAsync(row => row.Id == config.PendingEventId);
        Require(pending.Status == OutboxEventStatus.Pending && pending.AttemptCount == 0
            && pending.NextAttemptAt > DateTimeOffset.UtcNow && !a.Received(pending.Id) && !b.Received(pending.Id));
        var immutable = ImmutableEventDigest(pending);
        pending.NextAttemptAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Phase = "CONT_PENDING_DELIVERY";
        await a.WaitEventAsync(pending.Id);
        await b.WaitEventAsync(pending.Id);
        var deliveredPending = await WaitDeliveredAsync(config, pending.Id);
        a.AssertEnvelope(deliveredPending);
        b.AssertEnvelope(deliveredPending);
        Require(ImmutableEventDigest(deliveredPending) == immutable);
        var betaPositive = await SentinelAsync(db, beta);
        await c.WaitEventAsync(betaPositive);
        c.AssertEnvelope(await WaitDeliveredAsync(config, betaPositive));
        Phase = "CONT_PENDING_ISOLATION";
        await c.AssertNotReceivedAsync(pending.Id);
        Controls["recoveredPendingEnvelopeDelivered"] = "PASS";
        Controls["crossTenantTransportWithPositivePeer"] = "PASS";

        await AuditFailureAsync(db, owner, alpha);
        Phase = "ACTUAL_MESSAGE_PRODUCER";
        var request = Guid.NewGuid();
        Phase = "CONT_MESSAGE_FIRST";
        var message = await PostMessageAsync(owner, alpha, request, "Synthetic recovered message " + request.ToString("N"));
        var produced = await MessageEffectsAsync(db, alpha, message, request);
        await a.WaitEventAsync(produced.Id);
        await b.WaitEventAsync(produced.Id);
        var delivered = await WaitDeliveredAsync(config, produced.Id);
        a.AssertEnvelope(delivered);
        b.AssertEnvelope(delivered);
        Phase = "CONT_MESSAGE_DUPLICATE";
        var repeated = await PostMessageAsync(owner, alpha, request, "Synthetic recovered message " + request.ToString("N"));
        Require(repeated == message);
        var unchanged = await MessageEffectsAsync(db, alpha, message, request);
        Require(DeliveredDigest(unchanged) == DeliveredDigest(delivered));
        var live = await SentinelAsync(db, alpha);
        await a.WaitEventAsync(live);
        await b.WaitEventAsync(live);
        var deliveredLive = await WaitDeliveredAsync(config, live);
        a.AssertEnvelope(deliveredLive);
        b.AssertEnvelope(deliveredLive);
        await c.AssertNotReceivedAsync(produced.Id);
        Require(a.DeliveryCount(produced.Id) >= 1 && b.DeliveryCount(produced.Id) >= 1);
        Controls["actualHttpMessageAuditOutbox"] = "PASS";
        Controls["clientRequestIdBusinessIdempotence"] = "PASS";

        var state = new ContinuationState(message, produced.Id, request,
            DeliveredDigest(deliveredPending), DeliveredDigest(delivered));
        var path = Path.Combine(Path.GetDirectoryName(config.ReceiptPath)!, "continuation-state.json");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, state, Json);
    }

    private static async Task RestartAsync(AppDbContext db, PrivateConfig config)
    {
        Phase = "RESTART_CONTINUATION";
        var path = Path.Combine(Path.GetDirectoryName(config.ReceiptPath)!, "continuation-state.json");
        Require(new FileInfo(path).LinkTarget is null && new FileInfo(path).Length is > 0 and <= 16384);
        var state = JsonSerializer.Deserialize<ContinuationState>(await File.ReadAllBytesAsync(path), Json)!;
        var pending = await db.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == config.PendingEventId);
        var produced = await db.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == state.MessageEventId);
        Require(pending.Status == OutboxEventStatus.Delivered && produced.Status == OutboxEventStatus.Delivered
            && DeliveredDigest(pending) == state.PendingDeliveredDigest
            && DeliveredDigest(produced) == state.MessageDeliveredDigest);
        var alpha = await ScopeAsync(db, SecurityCiFixtureSeed.TenantASlug);
        var beta = await ScopeAsync(db, SecurityCiFixtureSeed.TenantBSlug);
        Phase = "RESTART_OWNER";
        using var owner = await LoginAsync(config, SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
        Phase = "RESTART_MEMBER";
        using var member = await LoginAsync(config, SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
        Phase = "RESTART_BETA";
        using var foreign = await LoginAsync(config, SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
        Phase = "RESTART_A_NEGOTIATE";
        await using var a = await ConnectAsync(member);
        Phase = "RESTART_B_NEGOTIATE";
        await using var b = await ConnectAsync(foreign);
        Require(await a.SubscribeAsync("SubscribeProject", alpha.Project)
            && await a.SubscribeAsync("SubscribeConversation", alpha.Conversation)
            && await b.SubscribeAsync("SubscribeProject", beta.Project));
        var request = Guid.NewGuid();
        Phase = "RESTART_MESSAGE_FIRST";
        var message = await PostMessageAsync(owner, alpha, request, "Synthetic after restart " + request.ToString("N"));
        var fresh = await MessageEffectsAsync(db, alpha, message, request);
        await a.WaitEventAsync(fresh.Id);
        a.AssertEnvelope(await WaitDeliveredAsync(config, fresh.Id));
        var live = await SentinelAsync(db, beta);
        await b.WaitEventAsync(live);
        b.AssertEnvelope(await WaitDeliveredAsync(config, live));
        Phase = "RESTART_PASSIVE_EXCLUSION";
        await a.AssertNotReceivedAsync(config.PendingEventId);
        await a.AssertNotReceivedAsync(state.MessageEventId);
        await using var observe = Context(config.ConnectionString);
        Require(DeliveredDigest(await observe.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == config.PendingEventId)) == state.PendingDeliveredDigest
            && DeliveredDigest(await observe.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == state.MessageEventId)) == state.MessageDeliveredDigest);
        await MessageEffectsAsync(observe, alpha, state.MessageId, state.ClientRequestId);
        Controls["deliveredBusinessStateSurvivesRestart"] = "PASS";
        Controls["noAutomaticRedeliveryObservedFor15SecondsWithPositiveControls"] = "PASS";
    }
}

internal static class Assert
{
    public static void True(bool value, string? message = null) { if (!value) throw new InvalidOperationException("SYNTHETIC_SOCKET_CONTROL_REJECTED"); }
    public static void False(bool value, string? message = null) => True(!value);
}

internal sealed class RecoverySocket : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _invocations = new();
    private readonly ConcurrentDictionary<Guid, int> _events = new();
    private readonly ConcurrentDictionary<Guid, JsonElement> _envelopes = new();
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

    public void AssertEnvelope(OutboxEvent expected)
    {
        Assert.True(_envelopes.TryGetValue(expected.Id, out var observed));
        using var document = JsonDocument.Parse(expected.PayloadJson);
        var declared = document.RootElement;
        foreach (var key in new[] { "eventId", "eventType", "payloadSchemaVersion", "tenantId",
            "aggregateType", "aggregateId", "aggregateVersion", "actor", "payload" })
            Assert.True(JsonElement.DeepEquals(observed.GetProperty(key), declared.GetProperty(key)));
        Assert.True(observed.GetProperty("occurredAt").GetDateTimeOffset() == declared.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.True(Received(expected.Id, expected.EventType) && expected.PayloadSchemaVersion == 1);
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

    public async Task AssertNotReceivedAsync(Guid eventId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (true)
        {
            Assert.True(_receiver is { IsCompleted: false } && _socket.State == WebSocketState.Open,
                "The passive observation requires an active receiver and open real transport.");
            Assert.False(Received(eventId), "An excluded event arrived over the authenticated real transport.");
            if (DateTimeOffset.UtcNow >= deadline) return;
            await Task.Delay(50);
        }
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
                        Assert.True(_envelopes.ContainsKey(eventId) || _envelopes.Count < 128);
                        if (_envelopes.TryGetValue(eventId, out var previousEnvelope))
                            Assert.True(JsonElement.DeepEquals(previousEnvelope, envelope));
                        _envelopes[eventId] = envelope.Clone();
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
