using System.Globalization;
using System.Text;
using System.Text.Json;
using Coglatas.SecurityArchitecture;

return await SecurityArchitectureCli.RunAsync(args, Console.Out);

public static class SecurityArchitectureCli
{
    private const int MaximumDocumentBytes = 32 * 1024 * 1024;
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        object result;
        bool valid;
        try
        {
            if (args.Length > 16 || args.Any(argument => argument.Length > 4096))
                throw new ArgumentException("Unsupported command input bounds.");
            result = args switch
            {
                ["validate", var path, var time] =>
                    ContractValidator.Validate(await ReadAsync<ContractDocument>(path), Time(time)),
                ["inventory-diff", var baseline, var candidate, var time] =>
                    InventoryDiff.Compare(await ReadAsync<ContractDocument>(baseline),
                        await ReadAsync<ContractDocument>(candidate), Time(time)),
                ["evidence-check", var contracts, var evidence, var sha, var environment, var time] =>
                    EvidenceValidator.Check(await ReadAsync<ContractDocument>(contracts),
                        await ReadAsync<EvidenceDocument>(evidence), sha, environment, Time(time)),
                ["registry-validate", var registry, var specificationRoot, var specificationSha] =>
                    await SpecRegistryValidator.ValidateAsync(await ReadAsync<SpecRegistryDocument>(registry),
                        specificationSha, new RepositoryArtifactReader(specificationRoot).ReadAsync),
                ["registry-validate", var registry, var specificationRoot, var specificationSha, var baseline] =>
                    await SpecRegistryValidator.ValidateAsync(await ReadAsync<SpecRegistryDocument>(registry),
                        specificationSha, new RepositoryArtifactReader(specificationRoot).ReadAsync,
                        await ReadAsync<SpecRegistryDocument>(baseline)),
                ["traceability-check", var registry, var manifest, var contracts, var specificationRoot,
                    var implementationRoot, var specificationSha, var candidateSha, var time] =>
                    await TraceabilityAsync(registry, manifest, contracts, specificationRoot, implementationRoot,
                        specificationSha, candidateSha, time),
                ["traceability-check", var registry, var manifest, var contracts, var specificationRoot,
                    var implementationRoot, var specificationSha, var candidateSha, var time, var links] =>
                    await TraceabilityAsync(registry, manifest, contracts, specificationRoot, implementationRoot,
                        specificationSha, candidateSha, time, links),
                ["traceability-transition-check", var registry, var manifest, var contracts, var baseline,
                    var specificationRoot, var implementationRoot, var specificationSha, var candidateSha, var time] =>
                    await TraceabilityAsync(registry, manifest, contracts, specificationRoot, implementationRoot,
                        specificationSha, candidateSha, time, baseline: baseline),
                ["traceability-transition-check", var registry, var manifest, var contracts, var baseline,
                    var specificationRoot, var implementationRoot, var specificationSha, var candidateSha, var time, var links] =>
                    await TraceabilityAsync(registry, manifest, contracts, specificationRoot, implementationRoot,
                        specificationSha, candidateSha, time, links, baseline),
                ["registry-review-check", var reference, var registry, var manifest, var owner, var time] =>
                    await ReviewAsync(reference, registry, manifest, owner, Time(time)),
                _ => new ValidationResult(false, [new("CLI_USAGE", "command",
                    "Use validate <contracts> <as-of-UTC>; inventory-diff <baseline> <candidate> <as-of-UTC>; " +
                    "evidence-check <contracts> <evidence> <candidate-SHA> <environment-digest> <as-of-UTC>; " +
                    "registry-validate <registry> <spec-root> <source-SHA> [baseline]; " +
                    "traceability-check <registry> <manifest> <contracts> <spec-root> <implementation-root> <source-SHA> <candidate-SHA> <as-of-UTC> [links]; " +
                    "traceability-transition-check <registry> <manifest> <contracts> <baseline> <spec-root> <implementation-root> <source-SHA> <candidate-SHA> <as-of-UTC> [links]; " +
                    "registry-review-check <reference> <registry> <manifest> <independent-owner-login> <as-of-UTC>.")])
            };
            valid = result switch
            {
                ValidationResult validation => validation.Valid,
                SpecValidationResult specification => specification.Valid,
                OwnerReviewResult review => review.Verified,
                _ => false
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException or ArgumentException or OperationCanceledException)
        {
            // Never echo input, paths, exception text, policy contents or credentials.
            result = new ValidationResult(false, [new("INPUT_ERROR", "document", "Input is unreadable, malformed or unsupported.")]);
            valid = false;
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(PublicResult(result), ContractJson.Options));
        return valid ? 0 : 1;
    }

    // Explicitly retain the public count/diagnostic contract without reflecting future private registry fields.
    private static object PublicResult(object result) => result is SpecValidationResult specification
        ? new { specification.Valid, specification.NormativeReady, specification.ApprovalStatus,
            specification.ExecutionAttestationStatus, specification.Diagnostics, Coverage = PublicCoverage(specification.Coverage) }
        : result;

    private static object? PublicCoverage(SpecCoverageSummary? coverage) => coverage is null ? null
        : new { coverage.CandidateSha, coverage.SchemaVersion, coverage.RegistryVersion,
            ActiveFamilies = PublicCounts(coverage.ActiveFamilies), Severities = PublicCounts(coverage.Severities),
            VerificationClasses = PublicCounts(coverage.VerificationClasses), coverage.ActiveRequirements,
            coverage.DeprecatedRequirements, coverage.RetiredRequirements, coverage.Mappings, coverage.ManualMappings,
            coverage.ExecutedPassingLinks, coverage.UnresolvedLinks, coverage.RequirementsWithKnownLimitations };

    private static object[] PublicCounts(SpecCoverageCount[] counts) => counts
        .Select(count => (object)new { count.Category, count.Count }).ToArray();

    private static async Task<SpecValidationResult> TraceabilityAsync(string registry, string manifest, string contracts,
        string specificationRoot, string implementationRoot, string specificationSha, string candidateSha,
        string time, string? links = null, string? baseline = null) => await SpecTraceabilityValidator.ValidateAsync(
            await ReadAsync<SpecRegistryDocument>(registry), await ReadAsync<SpecTraceabilityDocument>(manifest),
            await ReadAsync<ContractDocument>(contracts), specificationSha, candidateSha, Time(time),
            new RepositoryArtifactReader(specificationRoot).ReadAsync,
            new RepositoryArtifactReader(implementationRoot).ReadAsync,
            links is null ? null : await ReadAsync<SpecTraceabilityEvidenceDocument>(links),
            baseline is null ? null : await ReadAsync<SpecRegistryDocument>(baseline));

    private static async Task<OwnerReviewResult> ReviewAsync(string referencePath, string registryPath,
        string manifestPath, string owner, DateTimeOffset asOfUtc)
    {
        var reference = await ReadAsync<GitHubReviewReference>(referencePath);
        var registry = SpecDigest.Bytes(await ReadBytesAsync(registryPath));
        var manifest = SpecDigest.Bytes(await ReadBytesAsync(manifestPath));
        var registryArtifacts = reference.Artifacts.Where(a => a.Kind == SpecReviewArtifactKind.Registry).ToArray();
        var manifestArtifacts = reference.Artifacts.Where(a => a.Kind == SpecReviewArtifactKind.Traceability).ToArray();
        if (registryArtifacts.Length != 1 || manifestArtifacts.Length != 1 ||
            registryArtifacts[0].Digest != registry || manifestArtifacts[0].Digest != manifest)
            return new(false, "UNVERIFIED", [new("OWNER_REVIEW_PAYLOAD", "owner-review",
                "Review reference must bind to the exact supplied registry and mapping file bytes.")]);
        using var client = GitHubOwnerReviewVerifier.CreateClient(Environment.GetEnvironmentVariable("GH_TOKEN") ??
            Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
        return await GitHubOwnerReviewVerifier.VerifyAsync(reference, owner, asOfUtc, client);
    }

    private static async Task<byte[]> ReadBytesAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!source.CanSeek || source.Length > MaximumDocumentBytes) throw new IOException("Unsupported input bounds.");
        await using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
        {
            if (bytes.Length + count > MaximumDocumentBytes) throw new IOException("Unsupported input bounds.");
            await bytes.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
        }
        return bytes.ToArray();
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var bytes = new MemoryStream(await ReadBytesAsync(path), writable: false);
        using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ContractJson.Read<T>(await reader.ReadToEndAsync());
    }
    private static DateTimeOffset Time(string value)
    {
        var result = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (result.Offset != TimeSpan.Zero) throw new FormatException("UTC required.");
        return result;
    }
}
