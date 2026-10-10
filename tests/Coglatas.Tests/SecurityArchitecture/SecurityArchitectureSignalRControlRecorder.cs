using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Infrastructure.Persistence;
using Coglatas.SecurityArchitecture;
using Coglatas.Web.Controllers;

namespace Coglatas.Tests.SecurityArchitecture;

// Records only explicit live assertions; identities and protected frames stay in memory.
internal sealed class SecurityArchitectureSignalRControlRecorder
{
    private readonly string _method;
    private readonly string _source;
    private readonly string _sourceDigest;
    private readonly List<object> _observations = [];
    private readonly List<object> _hubInvocations = [];
    private readonly List<object> _originBoundaries = [];
    private readonly HashSet<(string Method, bool Allowed, string Code)> _recordedInvocations = [];

    private SecurityArchitectureSignalRControlRecorder(Type test, string member, string source)
    {
        _method = test.FullName + "." + member;
        var directory = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coglatas.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("SignalR verifier source root unavailable.");
        _source = Path.GetRelativePath(directory.FullName, source).Replace('\\', '/');
        _sourceDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
    }

    public static SecurityArchitectureSignalRControlRecorder Create(Type test,
        [CallerMemberName] string member = "", [CallerFilePath] string source = "") => new(test, member, source);

    public void ObserveIsolation(string eventType, RealtimeSubscriptionType target, string control,
        RealtimeSocket livePeer, Guid positive, RealtimeSocket excluded, Guid denied)
    {
        Assert.Contains(eventType, RealtimeEventCatalog.EventTypes);
        Assert.True(livePeer.Received(positive, eventType), "A live delivery with the expected type and schema must precede each isolation assertion.");
        Assert.False(excluded.Received(denied));
        _observations.Add(new { eventType, subscriptionType = target.ToString(), control,
            positiveDelivery = "OBSERVED", excludedDelivery = "NOT_OBSERVED", observedAtUtc = DateTimeOffset.UtcNow });
    }

    public void ObservePositive(string eventType, RealtimeSubscriptionType target, string control,
        RealtimeSocket recipient, Guid positive)
    {
        Assert.Contains(eventType, RealtimeEventCatalog.EventTypes);
        Assert.True(recipient.Received(positive, eventType), "The expected event type and schema must be received over the real transport.");
        _observations.Add(new { eventType, subscriptionType = target.ToString(), control,
            positiveDelivery = "OBSERVED", excludedDelivery = (string?)null, observedAtUtc = DateTimeOffset.UtcNow });
    }

    public void ObserveInvocation(string method, JsonElement result, bool expectedAllowed, string expectedCode)
    {
        Assert.Contains(method, new[] { "SubscribeUser", "SubscribeTenant", "SubscribeWorkspace", "SubscribeProject", "SubscribeConversation",
            "UnsubscribeWorkspace", "UnsubscribeProject", "UnsubscribeConversation" });
        Assert.Equal(expectedAllowed, result.GetProperty("allowed").GetBoolean());
        Assert.Equal(expectedCode, result.GetProperty("code").GetString());
        Assert.DoesNotContain(result.EnumerateObject(), property => property.Name is not ("allowed" or "code"));
        // Repeated actual calls remain asserted but cannot multiply method coverage.
        if (_recordedInvocations.Add((method, expectedAllowed, expectedCode)))
            _hubInvocations.Add(new { hubMethod = method, allowed = expectedAllowed, code = expectedCode, observedAtUtc = DateTimeOffset.UtcNow });
    }

    public void ObserveOriginNegotiation(HttpResponseMessage response, string control, HttpStatusCode expected,
        RealtimeSocket livePeer, Guid positive)
    {
        Assert.Equal(HttpMethod.Post, response.RequestMessage?.Method);
        Assert.Equal("/hubs/app/negotiate", response.RequestMessage?.RequestUri?.AbsolutePath);
        ObserveOriginBoundary("HUB_NEGOTIATE", control, response.StatusCode, expected, livePeer, positive);
    }

    public void ObserveOriginUpgrade(RealtimeSocket rejected, string control, HttpStatusCode expected,
        RealtimeSocket livePeer, Guid positive) =>
        ObserveOriginBoundary("HUB_WEBSOCKET_UPGRADE", control, rejected.UpgradeStatusCode, expected, livePeer, positive);

    private void ObserveOriginBoundary(string surface, string control, HttpStatusCode observed, HttpStatusCode expected,
        RealtimeSocket livePeer, Guid positive)
    {
        Assert.True(livePeer.Received(positive, "Messaging.MessageUpdated.v1"), "Every Origin boundary needs a received authenticated live control.");
        Assert.Equal(expected, observed);
        _originBoundaries.Add(new { surface, control, observedStatus = (int)observed, expectedStatus = (int)expected,
            positiveDelivery = "OBSERVED", observedAtUtc = DateTimeOffset.UtcNow });
    }

    public Task SaveAsync() => SecurityArchitectureInventoryTests.WritePrivateInventoryAsync(
        "signalr-controls-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_method))) + ".json", new
        {
            schemaVersion = 2, assemblyBindingScope = "SIX_ASSEMBLIES_WITH_LOADED_COPIES",
            verifierMethod = _method, sourcePath = _source, sourceDigest = _sourceDigest,
            environment = "ACTUAL_TEST_WEB_ENTRY_POINT_MIGRATED_POSTGRESQL_AND_REAL_WEBSOCKET",
            assemblyDigests = new[] { typeof(SecurityArchitectureSignalRControlRecorder).Assembly,
                    typeof(AuthController).Assembly, typeof(DurableEventEnvelope).Assembly,
                    typeof(AppDbContext).Assembly, typeof(CapabilityGrant).Assembly, typeof(SpecRegistryValidator).Assembly }
                .ToDictionary(assembly => assembly.GetName().Name!, assembly =>
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)))),
            observations = _observations, hubInvocations = _hubInvocations, originBoundaries = _originBoundaries,
            contractCompletion = "UNVERIFIED", ownerApproval = (string?)null,
            limits = new[] { "Explicit assertions require independent passed TRX and exact source/build/candidate reconciliation.",
                "Synthetic durable envelopes do not establish every business producer, role or capability contract.",
                "Canonical SPEC approval, Tenant event applicability and product RLS authority remain UNVERIFIED.",
                "No payloads, event/resource/session identities, cookies, group names or database credentials are recorded." }
        });
}
