namespace Coglatas.Domain.ProjectIde;

public enum CompilerExecutionState { Completed, NotRun, Cancelled, TimedOut, ResourceExhausted, InternalError }
public enum CompilerCoverage { Complete, Partial, None }
public enum CompilerVerdict { Pass, Fail, Inconclusive, Error, NotRun }
public enum CompilerFreshness { Current, Stale, Undetermined }
public enum CompilerDiagnosticSeverity { Error, Warning, Suggestion, Info }
public enum CompilerEvidenceKind { ModelProof, Observation, HumanApproval, Heuristic, Missing }

public sealed class CompilerEvidenceBasis
{
    public CompilerEvidenceKind Kind { get; }
    public IReadOnlyList<ContentDigest> Premises { get; }
    public string? MissingReason { get; }
    public SourceJson Data { get; }
    public CompilerEvidenceBasis(CompilerEvidenceKind kind, IEnumerable<ContentDigest> premises, string? missingReason = null)
    {
        ArgumentNullException.ThrowIfNull(premises);
        Kind = kind; MissingReason = missingReason;
        var values = premises.Take(1025).ToArray();
        if (!Enum.IsDefined(kind) || values.Length > 1024 || values.Any(item => item is null)) throw new ArgumentException("Invalid evidence basis.");
        if (kind == CompilerEvidenceKind.Missing ? !ValueFields.IsToken(missingReason ?? "") || values.Length != 0 : values.Length == 0 || missingReason is not null)
            throw new ArgumentException("Evidence requires actual premise digests or an explicit missing reason.");
        Premises = Array.AsReadOnly(values.Distinct().OrderBy(item => item.Value, StringComparer.Ordinal).ToArray());
        Data = SourceJson.FromObject(new { kind = CompilerContractTokens.Evidence(kind), premises = Premises.Select(item => item.Data.Value).ToArray(), missingReason });
    }
}

/// <summary>Typed finding bound to one input; localized prose and current merge policy do not participate in identity.</summary>
public sealed class CompilerDiagnostic
{
    public DiagnosticId DiagnosticId { get; }
    public ContentDigest FindingKey { get; }
    public CompilerInputId InputId { get; }
    public string RuleId { get; }
    public int RuleVersion { get; }
    public string Code { get; }
    public string Discriminator { get; }
    public string MessageKey { get; }
    public int StageOrdinal { get; }
    public CompilerDiagnosticSeverity Severity { get; }
    public ProjectLocation Location { get; }
    public CompilerEvidenceBasis Evidence { get; }
    public SourceJson Data { get; }

    public CompilerDiagnostic(CompilerInputManifest input, string ruleId, int ruleVersion, int stageOrdinal,
        string code, ProjectLocation location, CompilerEvidenceBasis evidence, CompilerDiagnosticSeverity severity = CompilerDiagnosticSeverity.Error,
        string discriminator = "", string? messageKey = null, IEnumerable<TypedProjectValue>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(evidence);
        if (!ValueFields.IsToken(ruleId) || !ValueFields.IsToken(code) || ruleVersion <= 0 || stageOrdinal is < 0 or > 32 || !Enum.IsDefined(severity))
            throw new ArgumentException("Invalid diagnostic identity.");
        if (!location.Context.Equals(input.Source.Context) || !input.Source.Manifest.DocumentIds.Contains(location.DocumentId))
            throw new ArgumentException("Diagnostic location must bind the exact input Source.");
        var rule = input.Rules.SingleOrDefault(item => item.RuleId == ruleId && item.Version == ruleVersion);
        if (rule is null) throw new ArgumentException("Diagnostic rule is absent from the locked ruleset.");
        var args = (arguments ?? []).Take(17).ToArray();
        if (args.Length > 16 || args.Any(item => item is null)) throw new ArgumentException("Diagnostic arguments exceed the typed bound.");
        if (discriminator.Length > 256) throw new ArgumentException("Diagnostic discriminator exceeds bound.");
        MessageKey = messageKey ?? code;
        if (!ValueFields.IsToken(MessageKey)) throw new ArgumentException("Expected invariant message key.");
        InputId = input.InputId; RuleId = ruleId; RuleVersion = ruleVersion; StageOrdinal = stageOrdinal;
        Code = code; Discriminator = discriminator; Severity = severity; Location = location; Evidence = evidence;
        DiagnosticId = new(SourceJson.FromObject(new object[] { InputId.ToString(), ruleId, ruleVersion, location.Data.Value, code, discriminator }));
        var stableLocation = SourceJson.FromObject(location.Data.Value.EnumerateObject().Where(item => item.Name is not ("context" or "textSpan"))
            .ToDictionary(item => item.Name, item => item.Value));
        FindingKey = ContentDigest.Compute("coglatas.finding-key/1", SourceJson.FromObject(new object[]
        { input.Source.TenantId.ToString(), input.Source.ProjectId.ToString(), ruleId, ruleVersion, stableLocation.Value, code, discriminator }).ToCanonicalBytes());
        Data = SourceJson.FromObject(new
        {
            diagnosticId = DiagnosticId.ToString(), derivationDigest = DiagnosticId.DerivationDigest.Data.Value,
            findingKey = FindingKey.Data.Value, ruleId, ruleVersion, rulesetDigest = input.Ruleset.Digest.Data.Value,
            stageOrdinal, code, discriminator, severity = CompilerContractTokens.Severity(severity), messageKey = MessageKey,
            arguments = args.Select(item => item.Data.Value).ToArray(), primaryLocation = location.Data.Value,
            relatedLocations = Array.Empty<object>(), evidence = evidence.Data.Value,
            protection = "coglatas.input-scope", suppression = new { kind = "coglatas.none" }, quickFixes = Array.Empty<object>()
        });
    }
}

public sealed class CompilerRuleOutcome
{
    public string RuleId { get; }
    public int Version { get; }
    public CompilerExecutionState Execution { get; }
    public CompilerCoverage Coverage { get; }
    public CompilerVerdict Verdict { get; }
    public CompilerEvidenceBasis Evidence { get; }
    public SourceJson Data { get; }
    public CompilerRuleOutcome(string ruleId, int version, CompilerExecutionState execution, CompilerCoverage coverage, CompilerVerdict verdict, CompilerEvidenceBasis evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!ValueFields.IsToken(ruleId) || version <= 0 || !Enum.IsDefined(execution) || !Enum.IsDefined(coverage) || !Enum.IsDefined(verdict))
            throw new ArgumentException("Invalid rule outcome.");
        if (verdict == CompilerVerdict.Pass && (execution != CompilerExecutionState.Completed || coverage != CompilerCoverage.Complete ||
            evidence.Kind is not (CompilerEvidenceKind.ModelProof or CompilerEvidenceKind.Observation)))
            throw new ArgumentException("Pass requires complete executed supported proof; heuristic/approval/missing basis cannot discharge it.");
        if (execution == CompilerExecutionState.NotRun && verdict != CompilerVerdict.NotRun) throw new ArgumentException("An unexecuted rule has no verdict.");
        RuleId = ruleId; Version = version; Execution = execution; Coverage = coverage; Verdict = verdict; Evidence = evidence;
        Data = SourceJson.FromObject(new { ruleId, version, execution = CompilerContractTokens.Execution(execution), coverage = CompilerContractTokens.Coverage(coverage), verdict = CompilerContractTokens.Verdict(verdict), evidence = evidence.Data.Value });
    }
}

public sealed class CompilerStageOutcome
{
    public string StageId { get; }
    public int Version { get; }
    public CompilerExecutionState Execution { get; }
    public CompilerCoverage Coverage { get; }
    public ContentDigest InputDigest { get; }
    public SourceJson Data { get; }
    public CompilerStageOutcome(string stageId, int version, CompilerExecutionState execution, CompilerCoverage coverage, ContentDigest inputDigest,
        IEnumerable<ContentDigest> outputs, string? limitation = null)
    {
        ArgumentNullException.ThrowIfNull(inputDigest); ArgumentNullException.ThrowIfNull(outputs);
        var frozen = outputs.Take(1025).ToArray();
        if (!ValueFields.IsToken(stageId) || version <= 0 || !Enum.IsDefined(execution) || !Enum.IsDefined(coverage) || frozen.Length > 1024 || frozen.Any(item => item is null))
            throw new ArgumentException("Invalid stage outcome.");
        if (limitation is not null && !ValueFields.IsToken(limitation)) throw new ArgumentException("Invalid stage reason.");
        if ((execution != CompilerExecutionState.Completed || coverage != CompilerCoverage.Complete) && limitation is null)
            throw new ArgumentException("Incomplete stage requires an explicit reason.");
        StageId = stageId; Version = version; Execution = execution; Coverage = coverage; InputDigest = inputDigest;
        Data = SourceJson.FromObject(new { stageId, version, execution = CompilerContractTokens.Execution(execution), coverage = CompilerContractTokens.Coverage(coverage), inputDigest = inputDigest.Data.Value,
            outputs = frozen.Select(item => item.Data.Value).ToArray(), limitation });
    }
}

public sealed class CompilerResult
{
    public CompilerInputManifest Input { get; }
    public CompilerResultId ResultId { get; }
    public CompilerExecutionState Execution { get; }
    public CompilerCoverage Coverage { get; }
    public CompilerVerdict Verdict { get; }
    public bool EvidenceComplete { get; }
    public IReadOnlyList<CompilerRuleOutcome> RuleOutcomes { get; }
    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; }
    public IReadOnlyList<CompilerStageOutcome> Stages { get; }
    public ContentDigest ResultDigest { get; }
    public SourceJson Data { get; }

    public CompilerResult(CompilerInputManifest input, CompilerExecutionState execution, CompilerCoverage coverage,
        IEnumerable<CompilerRuleOutcome> rules, IEnumerable<CompilerDiagnostic> diagnostics, IEnumerable<CompilerStageOutcome> stages, bool evidenceComplete)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(rules); ArgumentNullException.ThrowIfNull(diagnostics); ArgumentNullException.ThrowIfNull(stages);
        if (!Enum.IsDefined(execution) || !Enum.IsDefined(coverage)) throw new ArgumentException("Invalid result axes.");
        var outcomes = rules.Take(1025).ToArray(); var findings = diagnostics.Take(20_001).ToArray(); var steps = stages.Take(33).ToArray();
        if (outcomes.Length > 1024 || findings.Length > 20_000 || steps.Length > 32 || outcomes.Any(item => item is null) || findings.Any(item => item is null) || steps.Any(item => item is null))
            throw new ArgumentException("Result processing bound exceeded.");
        if (outcomes.Select(item => item.RuleId).Distinct(StringComparer.Ordinal).Count() != outcomes.Length ||
            steps.Select(item => item.StageId).Distinct(StringComparer.Ordinal).Count() != steps.Length ||
            findings.Select(item => item.DiagnosticId.Value).Distinct().Count() != findings.Length) throw new ArgumentException("Duplicate result identities.");
        if (outcomes.Any(item => !input.Rules.Any(rule => rule.RuleId == item.RuleId && rule.Version == item.Version)) ||
            steps.Any(item => !item.InputDigest.Equals(input.InputDigest)) || findings.Any(item => item.InputId != input.InputId))
            throw new ArgumentException("Result is not bound to the exact manifest.");
        var required = input.Rules.Where(item => item.Required).ToArray();
        var complete = input.SourceCoverageComplete && required.Length > 0 && steps.Length > 0 &&
            required.All(rule => outcomes.Any(item => item.RuleId == rule.RuleId && item.Execution == CompilerExecutionState.Completed && item.Coverage == CompilerCoverage.Complete)) &&
            steps.All(item => item.Execution == CompilerExecutionState.Completed && item.Coverage == CompilerCoverage.Complete);
        if (coverage == CompilerCoverage.Complete && !complete) throw new ArgumentException("Complete coverage requires every declared required evaluation and stage.");
        var allPass = required.Length > 0 && required.All(rule => outcomes.Any(item => item.RuleId == rule.RuleId && item.Verdict == CompilerVerdict.Pass));
        Verdict = execution == CompilerExecutionState.InternalError ? CompilerVerdict.Error
            : outcomes.Any(item => item.Verdict == CompilerVerdict.Fail && required.Any(rule => rule.RuleId == item.RuleId)) ? CompilerVerdict.Fail
            : execution == CompilerExecutionState.NotRun || outcomes.Length == 0 ? CompilerVerdict.NotRun
            : execution == CompilerExecutionState.Completed && coverage == CompilerCoverage.Complete && allPass && evidenceComplete ? CompilerVerdict.Pass
            : CompilerVerdict.Inconclusive;
        Input = input; Execution = execution; Coverage = coverage; EvidenceComplete = evidenceComplete;
        RuleOutcomes = Array.AsReadOnly(outcomes.OrderBy(item => item.RuleId, StringComparer.Ordinal).ToArray());
        Diagnostics = Array.AsReadOnly(findings.OrderBy(item => item.StageOrdinal).ThenBy(item => item.RuleId, StringComparer.Ordinal)
            .ThenBy(item => item.RuleVersion).ThenBy(item => item.Location.Data.CanonicalText, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.Discriminator, StringComparer.Ordinal).ThenBy(item => item.DiagnosticId.ToString(), StringComparer.Ordinal).ToArray());
        Stages = Array.AsReadOnly(steps);
        Data = SourceJson.FromObject(new
        {
            format = "coglatas.compiler-result/1", compilerInputId = input.InputId.ToString(), inputDigest = input.InputDigest.Data.Value,
            compiler = input.Compiler.Data.Value, execution = CompilerContractTokens.Execution(execution), coverage = CompilerContractTokens.Coverage(coverage),
            verdict = CompilerContractTokens.Verdict(Verdict), evidenceComplete,
            rules = RuleOutcomes.Select(item => item.Data.Value).ToArray(), diagnostics = Diagnostics.Select(item => item.Data.Value).ToArray(),
            stages = Stages.Select(item => item.Data.Value).ToArray(), boundInputStamp = input.Data.Value,
            artifacts = new { semanticModel = "coglatas.not-produced", ir = "coglatas.not-produced", sourceMap = "coglatas.not-produced", plans = Array.Empty<object>() },
            planEligibility = "coglatas.not-evaluated"
        });
        ResultDigest = ContentDigest.Compute("coglatas.compiler-result/1", Data.ToCanonicalBytes()); ResultId = new(input.InputId, ResultDigest);
    }
}

public sealed record CompilerFreshnessAssessment(CompilerResultId ResultId, ContentDigest? DesiredInputDigest, CompilerFreshness State, IReadOnlyList<string> ChangedDimensions)
{
    public static CompilerFreshnessAssessment Assess(CompilerResult result, CompilerInputManifest? desired)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (desired is null) return new(result.ResultId, null, CompilerFreshness.Undetermined, Array.AsReadOnly(new[] { "coglatas.desired-input-unavailable" }));
        var changed = result.Input.Data.Value.EnumerateObject().Where(item => !SourceJson.FromElement(item.Value).Equals(SourceJson.FromElement(desired.Data.Value.GetProperty(item.Name))))
            .Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        return new(result.ResultId, desired.InputDigest, result.Input.InputDigest.Equals(desired.InputDigest) ? CompilerFreshness.Current : CompilerFreshness.Stale, Array.AsReadOnly(changed));
    }
}

internal static class CompilerContractTokens
{
    internal static string Execution(CompilerExecutionState value) => value switch
    {
        CompilerExecutionState.Completed => "coglatas.completed", CompilerExecutionState.NotRun => "coglatas.not-run", CompilerExecutionState.Cancelled => "coglatas.cancelled",
        CompilerExecutionState.TimedOut => "coglatas.timed-out", CompilerExecutionState.ResourceExhausted => "coglatas.resource-exhausted", CompilerExecutionState.InternalError => "coglatas.internal-error", _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    internal static string Coverage(CompilerCoverage value) => value switch { CompilerCoverage.Complete => "coglatas.complete", CompilerCoverage.Partial => "coglatas.partial", CompilerCoverage.None => "coglatas.none", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    internal static string Verdict(CompilerVerdict value) => value switch { CompilerVerdict.Pass => "coglatas.pass", CompilerVerdict.Fail => "coglatas.fail", CompilerVerdict.Inconclusive => "coglatas.inconclusive", CompilerVerdict.Error => "coglatas.error", CompilerVerdict.NotRun => "coglatas.not-run", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    internal static string Evidence(CompilerEvidenceKind value) => value switch { CompilerEvidenceKind.ModelProof => "coglatas.model-proof", CompilerEvidenceKind.Observation => "coglatas.observation", CompilerEvidenceKind.HumanApproval => "coglatas.human-approval", CompilerEvidenceKind.Heuristic => "coglatas.heuristic", CompilerEvidenceKind.Missing => "coglatas.missing", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    internal static string Severity(CompilerDiagnosticSeverity value) => value switch { CompilerDiagnosticSeverity.Error => "coglatas.error", CompilerDiagnosticSeverity.Warning => "coglatas.warning", CompilerDiagnosticSeverity.Suggestion => "coglatas.suggestion", CompilerDiagnosticSeverity.Info => "coglatas.info", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
}
