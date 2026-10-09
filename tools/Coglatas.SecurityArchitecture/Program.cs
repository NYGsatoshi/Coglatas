using System.Globalization;
using System.Text.Json;
using Coglatas.SecurityArchitecture;

return await SecurityArchitectureCli.RunAsync(args, Console.Out);

public static class SecurityArchitectureCli
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        object result;
        bool valid;
        try
        {
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
                ["registry-review-check", var reference, var registry, var manifest, var owner, var time] =>
                    await ReviewAsync(reference, registry, manifest, owner, Time(time)),
                _ => new ValidationResult(false, [new("CLI_USAGE", "command",
                    "Use validate <contracts> <as-of-UTC>; inventory-diff <baseline> <candidate> <as-of-UTC>; " +
                    "evidence-check <contracts> <evidence> <candidate-SHA> <environment-digest> <as-of-UTC>; " +
                    "registry-validate <registry> <spec-root> <source-SHA> [baseline]; " +
                    "traceability-check <registry> <manifest> <contracts> <spec-root> <implementation-root> <source-SHA> <candidate-SHA> <as-of-UTC> [links]; " +
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
        catch (Exception ex) when (ex is JsonException or IOException or FormatException or ArgumentException)
        {
            // Never echo input, paths, exception text, policy contents or credentials.
            result = new ValidationResult(false, [new("INPUT_ERROR", "document", "Input is unreadable, malformed or unsupported.")]);
            valid = false;
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(result, ContractJson.Options));
        return valid ? 0 : 1;
    }

    private static async Task<SpecValidationResult> TraceabilityAsync(string registry, string manifest, string contracts,
        string specificationRoot, string implementationRoot, string specificationSha, string candidateSha,
        string time, string? links = null) => await SpecTraceabilityValidator.ValidateAsync(
            await ReadAsync<SpecRegistryDocument>(registry), await ReadAsync<SpecTraceabilityDocument>(manifest),
            await ReadAsync<ContractDocument>(contracts), specificationSha, candidateSha, Time(time),
            new RepositoryArtifactReader(specificationRoot).ReadAsync,
            new RepositoryArtifactReader(implementationRoot).ReadAsync,
            links is null ? null : await ReadAsync<SpecTraceabilityEvidenceDocument>(links));

    private static async Task<OwnerReviewResult> ReviewAsync(string referencePath, string registryPath,
        string manifestPath, string owner, DateTimeOffset asOfUtc)
    {
        var reference = await ReadAsync<GitHubReviewReference>(referencePath);
        var registry = SpecDigest.Bytes(await File.ReadAllBytesAsync(registryPath));
        var manifest = SpecDigest.Bytes(await File.ReadAllBytesAsync(manifestPath));
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

    private static async Task<T> ReadAsync<T>(string path) => ContractJson.Read<T>(await File.ReadAllTextAsync(path));
    private static DateTimeOffset Time(string value)
    {
        var result = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (result.Offset != TimeSpan.Zero) throw new FormatException("UTC required.");
        return result;
    }
}
