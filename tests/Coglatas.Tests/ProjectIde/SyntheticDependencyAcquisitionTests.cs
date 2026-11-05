using System.Text.Json;
using Coglatas.Application.ProjectIde.DependencyAcquisition;
using Coglatas.Domain.ProjectIde;
using Coglatas.Domain.ProjectIde.VersionedArtifacts;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticDependencyAcquisitionTests
{
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static ArtifactSourcePin Pin(int revision = 5) => ArtifactSourcePin.FromSource(CompilerContractFixtures.Source(revision));
    private static SyntheticUnitBody Body(int revision = 5, int target = 9, string unitId = "synthetic.hours") =>
        new(Pin(revision), Guid.Parse(CompilerContractFixtures.Id(target)), Json(new { unitId, version = 1, dimension = new Dictionary<string, int> { ["synthetic.time"] = 1 } }));
    private static PrototypeArtifact Manifest(SyntheticUnitBody body) => PrototypeArtifact.Create(PrototypeArtifactRegistry.DependencyManifest, body.Source,
        Json(new { entries = new[] { new { entryKey = "unit-1", dependencyKind = "unit", expected = body.Identity.Data.Value,
            scope = new { kind = "project", context = body.Source.Context.Data.Value }, target = new { kind = "unit", id = body.TargetId.ToString("D") },
            coverageClaim = "complete", sourceBinding = new { referenceSchemaVersion = 2, referenceKey = "unit-origin" } } } }));
    private static SyntheticAcquisitionContext Context(int revision = 5, int actor = 7, string stamp = "policy-1", bool capability = true, bool allowed = true) =>
        new(Guid.Parse(CompilerContractFixtures.Id(actor)), Pin(revision), stamp,
            capability ? [SyntheticDependencyResolver.Capability] : [], allowed ? [SyntheticDependencyResolver.AllowedSource] : []);
    private sealed class Contexts(Func<CancellationToken, ValueTask<SyntheticAcquisitionContext?>> read) : ISyntheticAcquisitionContextProvider
    {
        public ValueTask<SyntheticAcquisitionContext?> ReadCurrentAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
    private sealed class Bodies(Func<string, ArtifactIdentity, CancellationToken, ValueTask<SyntheticUnitBody?>> read) : ISyntheticUnitBodyProvider
    {
        public ValueTask<SyntheticUnitBody?> ReadAsync(string sourceId, ArtifactIdentity expectedIdentity, CancellationToken cancellationToken) => read(sourceId, expectedIdentity, cancellationToken);
    }
    private static SyntheticDependencyResolver Resolver(Func<SyntheticAcquisitionContext?>? context = null, Func<SyntheticUnitBody?>? body = null) =>
        new(new Contexts(_ => ValueTask.FromResult(context is null ? Context() : context())),
            new Bodies((_, _, _) => ValueTask.FromResult(body is null ? Body() : body())));

    [Fact]
    public async Task Authorized_exact_synthetic_Unit_is_present_but_never_formal_evidence()
    {
        var body = Body(); var manifest = Manifest(body); var reads = 0;
        var resolver = new SyntheticDependencyResolver(new Contexts(_ => { reads++; return ValueTask.FromResult<SyntheticAcquisitionContext?>(Context()); }),
            new Bodies((source, expected, _) =>
            {
                Assert.Equal(SyntheticDependencyResolver.AllowedSource, source); Assert.Equal(body.Identity, expected);
                return ValueTask.FromResult<SyntheticUnitBody?>(body);
            }));
        var result = await resolver.AcquireAsync(manifest, "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Present, result.State); Assert.Same(body, result.Body);
        Assert.False(result.FormalEvidenceEligible); Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData("https://example.com/private")]
    [InlineData("file:///private/source")]
    [InlineData("coglatas.synthetic.other")]
    public async Task Arbitrary_URL_or_unlisted_source_is_denied_before_any_provider_is_opened(string source)
    {
        var resolver = new SyntheticDependencyResolver(new Contexts(_ => throw new InvalidOperationException("must not open authority")),
            new Bodies((_, _, _) => throw new InvalidOperationException("must not open body")));
        var result = await resolver.AcquireAsync(Manifest(Body()), "unit-1", source);
        Assert.Equal(SyntheticAcquisitionState.Denied, result.State); Assert.Null(result.Body);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Explicit_capability_and_source_allowlist_are_both_required(bool capability, bool allowed)
    {
        var calls = 0;
        var resolver = Resolver(() => Context(capability: capability, allowed: allowed), () => { calls++; return Body(); });
        Assert.Equal(SyntheticAcquisitionState.Denied, (await resolver.AcquireAsync(Manifest(Body()), "unit-1", SyntheticDependencyResolver.AllowedSource)).State);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Wrong_exact_scope_or_unavailable_current_authority_is_denied()
    {
        var manifest = Manifest(Body());
        Assert.Equal(SyntheticAcquisitionState.Denied, (await Resolver(() => Context(7)).AcquireAsync(manifest, "unit-1", SyntheticDependencyResolver.AllowedSource)).State);
        Assert.Equal(SyntheticAcquisitionState.Denied, (await Resolver(() => null).AcquireAsync(manifest, "unit-1", SyntheticDependencyResolver.AllowedSource)).State);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("actor")]
    [InlineData("policy")]
    [InlineData("branch-revision")]
    [InlineData("capability")]
    public async Task Authority_drift_after_body_read_discards_the_body(string drift)
    {
        var reads = 0;
        var resolver = Resolver(() => ++reads == 1 ? Context() : drift switch
        {
            "revoke" => null, "actor" => Context(actor: 11), "policy" => Context(stamp: "policy-2"),
            "branch-revision" => Context(7), _ => Context(capability: false)
        });
        var result = await resolver.AcquireAsync(Manifest(Body()), "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Denied, result.State); Assert.Null(result.Body); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task A_missing_body_is_not_disclosed_after_permission_revocation()
    {
        var reads = 0; var resolver = Resolver(() => ++reads == 1 ? Context() : null, () => null);
        var result = await resolver.AcquireAsync(Manifest(Body()), "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Denied, result.State); Assert.Null(result.Body);
        var missing = await Resolver(body: () => null).AcquireAsync(Manifest(Body()), "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Missing, missing.State); Assert.Null(missing.Body);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("target")]
    [InlineData("revision")]
    public async Task Full_pin_target_and_scope_substitution_is_invalid(string change)
    {
        var substitute = change switch { "target" => Body(target: 10), "revision" => Body(7), _ => Body(unitId: "synthetic.minutes") };
        var result = await Resolver(body: () => substitute).AcquireAsync(Manifest(Body()), "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Invalid, result.State); Assert.Null(result.Body);
    }

    [Fact]
    public async Task Cancellation_after_provider_work_never_publishes_a_body()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = Resolver(body: () => { cancellation.Cancel(); return Body(); });
        var result = await resolver.AcquireAsync(Manifest(Body()), "unit-1", SyntheticDependencyResolver.AllowedSource, cancellation.Token);
        Assert.Equal(SyntheticAcquisitionState.Cancelled, result.State); Assert.Null(result.Body);
    }

    [Fact]
    public async Task Body_size_exhaustion_failure_and_missing_entry_never_become_empty_success()
    {
        var manifest = Manifest(Body());
        var exhausted = await Resolver().AcquireAsync(manifest, "unit-1", SyntheticDependencyResolver.AllowedSource, maximumBodyBytes: 1);
        Assert.Equal(SyntheticAcquisitionState.Exhausted, exhausted.State); Assert.Null(exhausted.Body);
        var failed = await Resolver(body: () => throw new IOException("synthetic source unavailable")).AcquireAsync(manifest, "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Failed, failed.State); Assert.Null(failed.Body);
        var missing = await Resolver().AcquireAsync(manifest, "wrong-entry", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Invalid, missing.State); Assert.Null(missing.Body);
    }

    [Fact]
    public void Unknown_Unit_semantics_are_preserved_by_SourceJson_but_not_acquired_as_supported_units()
    {
        var data = SourceJson.Parse("{\"unitId\":\"synthetic.hours\",\"version\":1,\"dimension\":{\"synthetic.time\":1},\"futureMeaning\":[3,2,1]}");
        Assert.Throws<UnsupportedSourceException>(() => new SyntheticUnitBody(Pin(), Guid.Parse(CompilerContractFixtures.Id(9)), data));
        Assert.Contains("futureMeaning", data.CanonicalText);
    }

    [Fact]
    public async Task Unsupported_dependency_kind_is_not_reinterpreted_as_a_Unit()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(Manifest(Body()).Data.CanonicalText)!;
        var entry = root["payload"]!["entries"]![0]!;
        entry["dependencyKind"] = "calendar"; entry["target"]!["kind"] = "calendar";
        entry["expected"]!["artifactType"] = "coglatas.prototype.calendar";
        var manifest = PrototypeArtifact.Parse(SourceJson.Parse(root.ToJsonString()));
        var resolver = Resolver(body: () => throw new InvalidOperationException("must not coerce Calendar"));
        var result = await resolver.AcquireAsync(manifest, "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Unsupported, result.State); Assert.Null(result.Body);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("project")]
    [InlineData("branch")]
    public async Task Current_server_scope_cannot_be_replaced_by_request_Tenant_Project_or_Branch(string changed)
    {
        var original = CompilerContractFixtures.Source(); var branch = original.Context.Branch;
        var foreign = new BranchRef(changed == "tenant" ? TenantId.Parse(CompilerContractFixtures.Id(20)) : branch.TenantId,
            changed == "project" ? ProjectId.Parse(CompilerContractFixtures.Id(20)) : branch.ProjectId,
            changed == "branch" ? BranchId.Parse(CompilerContractFixtures.Id(20)) : branch.BranchId);
        var source = ProjectSource.Create(SourceRevisionContext.Committed(new(foreign, RevisionId.Parse(CompilerContractFixtures.Id(5)))), original.Documents);
        var authority = new SyntheticAcquisitionContext(Guid.Parse(CompilerContractFixtures.Id(7)), ArtifactSourcePin.FromSource(source), "policy-1",
            [SyntheticDependencyResolver.Capability], [SyntheticDependencyResolver.AllowedSource]);
        var result = await Resolver(() => authority).AcquireAsync(Manifest(Body()), "unit-1", SyntheticDependencyResolver.AllowedSource);
        Assert.Equal(SyntheticAcquisitionState.Denied, result.State); Assert.Null(result.Body);
    }
}
