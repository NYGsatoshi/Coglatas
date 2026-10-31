using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public sealed record CompilerArtifactIdentity
{
    public string ArtifactId { get; }
    public string Version { get; }
    public ContentDigest Digest { get; }
    public SourceJson Data { get; }
    public CompilerArtifactIdentity(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        RelationFields.Require(data, "artifactId", "version", "digest");
        Data = data; ArtifactId = SourceFields.Token(data.Value, "artifactId"); Version = RelationFields.Version(data, "version");
        Digest = RelationFields.Digest(data, "digest");
    }
}

public sealed class CompilerRuleContract
{
    public string RuleId { get; }
    public int Version { get; }
    public bool Required { get; }
    public SourceJson Configuration { get; }
    public SourceJson Data { get; }
    internal CompilerRuleContract(SourceJson data)
    {
        RelationFields.Require(data, "ruleId", "version", "required", "configuration");
        Data = data; RuleId = SourceFields.Token(data.Value, "ruleId"); Version = SourceFields.PositiveInteger(data.Value, "version");
        Required = ValueFields.Boolean(data.Value, "required"); Configuration = RelationFields.Child(data, "configuration");
    }
}

public sealed class CompilerExecutionProfile
{
    public string Purpose { get; }
    public int MaximumOperations { get; }
    public int MaximumDeclarations { get; }
    public int MaximumRelations { get; }
    public ExactNumberLimits Numbers { get; }
    public SourceJson Data { get; }
    internal CompilerExecutionProfile(SourceJson data)
    {
        RelationFields.Require(data, "purpose", "maximumOperations", "maximumDeclarations", "maximumRelations", "maximumDigits", "maximumScale", "rounding");
        Data = data; Purpose = SourceFields.Token(data.Value, "purpose");
        if (Purpose is not ("coglatas.interactive" or "coglatas.normal" or "coglatas.deep")) throw new UnsupportedSourceException("Unsupported execution purpose.");
        MaximumOperations = SourceFields.PositiveInteger(data.Value, "maximumOperations");
        MaximumDeclarations = SourceFields.PositiveInteger(data.Value, "maximumDeclarations");
        MaximumRelations = SourceFields.PositiveInteger(data.Value, "maximumRelations");
        if (MaximumOperations > 10_000_000 || MaximumDeclarations > 100_000 || MaximumRelations > 100_000)
            throw new UnsupportedSourceException("Profile exceeds provisional Core processing caps.");
        Numbers = new(SourceFields.PositiveInteger(data.Value, "maximumDigits"), ValueFields.Integer(data.Value, "maximumScale")); Numbers.Validate();
        if (SourceFields.Token(data.Value, "rounding") != "coglatas.ties-to-even") throw new UnsupportedSourceException("Unsupported numeric profile.");
    }
}

/// <summary>Immutable authorized input claims, not an authorization grant. Every applicable dependency participates in identity.</summary>
public sealed class CompilerInputManifest
{
    public const string Format = "coglatas.compiler-input/1";
    public ProjectSource Source { get; }
    public CompilerArtifactIdentity Compiler { get; }
    public CompilerArtifactIdentity Ruleset { get; }
    public IReadOnlyList<CompilerRuleContract> Rules { get; }
    public CompilerExecutionProfile ExecutionProfile { get; }
    public bool SourceCoverageComplete { get; }
    public SourceJson Settings { get; }
    public SourceJson Data { get; }
    public ContentDigest InputDigest { get; }
    public CompilerInputId InputId { get; }
    private static readonly string[] ArtifactFields = ["schema", "compiler", "ruleset", "semantics", "ir", "evaluator"];
    private static readonly string[] SnapshotFields = ["authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations", "evidence"];
    private static readonly string[] LockFields = ["descriptors", "calendars", "timeZones"];
    private static readonly string[] NoneFields = ["scheduler", "solver", "stochastic", "importer"];

    public CompilerInputManifest(ProjectSource source, SourceJson settings)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(settings);
        Source = source;
        PlanEntityReference.RequireKnownContext(source.Context);
        RelationFields.Require(settings, [.. ArtifactFields, .. SnapshotFields, .. LockFields, .. NoneFields,
            "rules", "featureSet", "targets", "analysisScope", "executionProfile", "clock", "sourceCoverage", "scenario"]);
        var fields = settings.Value.EnumerateObject().ToDictionary(item => item.Name, item => (object?)item.Value);
        foreach (var field in ArtifactFields) _ = new CompilerArtifactIdentity(RelationFields.Child(settings, field));
        Compiler = new(RelationFields.Child(settings, "compiler")); Ruleset = new(RelationFields.Child(settings, "ruleset"));
        ExecutionProfile = new(RelationFields.Child(settings, "executionProfile"));
        SourceCoverageComplete = Coverage(SourceFields.Token(settings.Value, "sourceCoverage"));
        foreach (var field in NoneFields) RequireNone(RelationFields.Child(settings, field));
        var scenario = RelationFields.Child(settings, "scenario");
        if (source.Context.Scenario is { } overlay)
        {
            RelationFields.Require(scenario, "baseContext", "overlayDigest", "assumptions");
            if (!RelationFields.Child(scenario, "baseContext").Equals(overlay.BaseContext.Data) || !RelationFields.Digest(scenario, "overlayDigest").Equals(overlay.OverlayDigest))
                throw new FormatException("Scenario binding mismatch.");
            _ = SourceFields.Array(scenario.Value, "assumptions");
        }
        else RequireNone(scenario);
        var clock = RelationFields.Child(settings, "clock");
        if (SourceFields.Token(clock.Value, "kind") == "coglatas.not-applicable") RelationFields.Require(clock, "kind");
        else
        {
            RelationFields.Require(clock, "kind", "instant");
            if (SourceFields.Token(clock.Value, "kind") != "coglatas.known") throw new UnsupportedSourceException("Unsupported clock context.");
            _ = ProjectInstant.Parse(SourceFields.String(clock.Value, "instant"));
        }
        foreach (var field in SnapshotFields)
        {
            var values = SourceFields.Array(settings.Value, field).EnumerateArray().Select(item => SourceJson.FromElement(item, settings.Limits)).ToArray();
            foreach (var snapshot in values)
            {
                RelationFields.Require(snapshot, "snapshotId", "version", "digest", "context", "coverage", "asOf");
                _ = SourceFields.Identity<ExternalSnapshotId>(snapshot.Value, "snapshotId"); _ = RelationFields.Version(snapshot, "version");
                _ = RelationFields.Digest(snapshot, "digest");
                var context = SourceRevisionContext.Parse(RelationFields.Child(snapshot, "context")); PlanEntityReference.RequireKnownContext(context);
                if (context.Branch.TenantId != source.TenantId) throw new FormatException("Cross-Tenant snapshot is outside the supported authorized scope.");
                _ = Coverage(SourceFields.Token(snapshot.Value, "coverage"));
                var asOf = snapshot.Value.GetProperty("asOf");
                if (asOf.ValueKind != JsonValueKind.Null) _ = ProjectInstant.Parse(ValueFields.String(asOf));
            }
            fields[field] = OrderUnique(values, "snapshotId");
        }
        foreach (var field in LockFields)
        {
            var values = SourceFields.Array(settings.Value, field).EnumerateArray().Select(item => new CompilerArtifactIdentity(SourceJson.FromElement(item, settings.Limits))).ToArray();
            fields[field] = OrderUnique(values.Select(item => item.Data).ToArray(), "artifactId");
        }
        var rules = SourceFields.Array(settings.Value, "rules").EnumerateArray().Select(item => new CompilerRuleContract(SourceJson.FromElement(item, settings.Limits))).ToArray();
        fields["rules"] = OrderUnique(rules.Select(item => item.Data).ToArray(), "ruleId");
        Rules = Array.AsReadOnly(rules.OrderBy(item => item.RuleId, StringComparer.Ordinal).ToArray());
        foreach (var field in new[] { "featureSet", "targets", "analysisScope" })
        {
            var values = SourceFields.Array(settings.Value, field).EnumerateArray().Select(ValueFields.String).ToArray();
            if (values.Any(item => !ValueFields.IsToken(item)) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new FormatException("Capabilities/targets/scope must be unique namespaced tokens.");
            fields[field] = values.Order(StringComparer.Ordinal).ToArray();
        }
        Settings = SourceJson.FromObject(fields, settings.Limits);
        Data = SourceJson.FromObject(new
        {
            format = Format, canonicalEncodingVersion = 1,
            scope = source.Context.Data.Value.GetProperty("branch"), sourceContext = source.Context.Data.Value,
            sourceDigest = source.Digest.Data.Value, sourceManifest = source.Manifest.Data.Value,
            packageLock = source.PackageLock.Value, settings = Settings.Value
        });
        InputDigest = ContentDigest.Compute(Format, Data.ToCanonicalBytes()); InputId = new(InputDigest);
    }

    public SourceJson ToEnvelope() => SourceJson.FromObject(new { compilerInputId = InputId.ToString(), inputDigest = InputDigest.Data.Value, manifest = Data.Value });
    public static CompilerInputManifest ReadEnvelope(ProjectSource source, SourceJson envelope)
    {
        RelationFields.Require(envelope, "compilerInputId", "inputDigest", "manifest");
        var manifest = RelationFields.Child(envelope, "manifest");
        var input = new CompilerInputManifest(source, RelationFields.Child(manifest, "settings"));
        if (!input.Data.Equals(manifest) || SourceFields.String(envelope.Value, "compilerInputId") != input.InputId.ToString() ||
            !RelationFields.Digest(envelope, "inputDigest").Equals(input.InputDigest)) throw new FormatException("Compiler input identity or manifest integrity mismatch.");
        return input;
    }

    private static object[] OrderUnique(SourceJson[] values, string key)
    {
        if (values.Select(item => SourceFields.String(item.Value, key)).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new FormatException("Duplicate locked manifest identity.");
        return values.OrderBy(item => SourceFields.String(item.Value, key), StringComparer.Ordinal).Select(item => (object)item.Value).ToArray();
    }
    private static void RequireNone(SourceJson data)
    {
        RelationFields.Require(data, "kind");
        if (SourceFields.Token(data.Value, "kind") != "coglatas.none") throw new UnsupportedSourceException("Unsupported non-None engine/import context.");
    }
    private static bool Coverage(string token) => token switch
    {
        "coglatas.complete" => true, "coglatas.partial" or "coglatas.none" => false,
        _ => throw new UnsupportedSourceException("Unsupported coverage contract.")
    };
}
