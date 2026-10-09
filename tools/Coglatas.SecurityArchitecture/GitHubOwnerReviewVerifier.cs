using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coglatas.SecurityArchitecture;

public enum SpecReviewArtifactKind { Registry, Traceability, Contracts }
public sealed record GitHubReviewArtifact(SpecReviewArtifactKind Kind, string Path, string Digest);
public sealed record GitHubReviewReference(string Repository, int PullRequest, long ReviewId,
    string CommitSha, GitHubReviewArtifact[] Artifacts);
public sealed record OwnerReviewResult(bool Verified, string Status, Diagnostic[] Diagnostics);

/// <summary>Checks live GitHub review authority and exact reviewed artifact bytes; never trusts approval JSON fields.</summary>
public static partial class GitHubOwnerReviewVerifier
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex RepositoryPattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]*$")]
    private static partial Regex LoginPattern();

    public static async Task<OwnerReviewResult> VerifyAsync(GitHubReviewReference reference,
        string independentlySuppliedOwner, DateTimeOffset asOfUtc, HttpClient client)
    {
        var diagnostics = new List<Diagnostic>();
        void Add(string rule, string reason) => diagnostics.Add(new(rule, "owner-review", reason));
        if (!RepositoryPattern().IsMatch(reference.Repository) || !LoginPattern().IsMatch(independentlySuppliedOwner) ||
            reference.PullRequest < 1 || reference.ReviewId < 1 ||
            !RepositoryArtifactReader.RevisionPattern().IsMatch(reference.CommitSha) ||
            reference.Artifacts.Length < 2 ||
            !reference.Artifacts.Any(a => a.Kind == SpecReviewArtifactKind.Registry) ||
            !reference.Artifacts.Any(a => a.Kind == SpecReviewArtifactKind.Traceability) ||
            reference.Artifacts.Select(a => a.Kind).Distinct().Count() != reference.Artifacts.Length ||
            reference.Artifacts.Select(a => a.Path).Distinct(StringComparer.Ordinal).Count() != reference.Artifacts.Length ||
            reference.Artifacts.Any(a => !Enum.IsDefined(a.Kind) || !RepositoryArtifactReader.IsSafePath(a.Path) || !ContractValidator.DigestPattern().IsMatch(a.Digest)))
            Add("OWNER_REVIEW_REFERENCE", "Exact repository/PR/review/revision and at least registry and mapping artifact digests are required.");
        if (diagnostics.Count != 0) return Result(diagnostics);
        try
        {
            var prefix = "repos/" + reference.Repository + "/pulls/" + reference.PullRequest.ToString(CultureInfo.InvariantCulture);
            using var pullRequest = await ReadAsync(client, prefix);
            if (!CurrentHead(pullRequest.RootElement, reference.CommitSha))
                Add("OWNER_REVIEW_HEAD", "The current review target is Draft, closed or differs from the exact requested head revision.");
            using var selected = await ReadAsync(client, prefix + "/reviews/" + reference.ReviewId.ToString(CultureInfo.InvariantCulture));
            if (!Approved(selected.RootElement, reference, independentlySuppliedOwner, asOfUtc))
                Add("OWNER_REVIEW_AUTHORITY", "GitHub does not report an exact-head APPROVED review by the independently supplied personal owner.");
            if (!ScopedApproval(selected.RootElement, reference))
                Add("OWNER_REVIEW_SCOPE", "Ordinary code review is insufficient; the owner review must contain the exact dedicated SPEC-registry/mapping approval statement and artifact digests.");
            if (await LatestOwnerDecisionAsync(client, prefix, independentlySuppliedOwner) != reference.ReviewId)
                Add("OWNER_REVIEW_SUPERSEDED", "The specified approval is absent, dismissed or superseded by a later owner decision.");
            foreach (var artifact in reference.Artifacts)
            {
                var path = string.Join('/', artifact.Path.Split('/').Select(Uri.EscapeDataString));
                using var contents = await ReadAsync(client, "repos/" + reference.Repository + "/contents/" + path + "?ref=" + reference.CommitSha);
                var root = contents.RootElement;
                if (root.GetProperty("type").GetString() != "file" || root.GetProperty("encoding").GetString() != "base64")
                { Add("OWNER_REVIEW_ARTIFACT", "Reviewed artifact is unavailable as bounded file bytes at the exact revision."); continue; }
                var bytes = Convert.FromBase64String(root.GetProperty("content").GetString() ?? "");
                if (SpecDigest.Bytes(bytes) != artifact.Digest)
                    Add("OWNER_REVIEW_ARTIFACT", "Independent GitHub file bytes differ from the exact reviewed registry/mapping payload.");
            }
            // Recheck the review target after resolving the private payloads.
            using var finalPullRequest = await ReadAsync(client, prefix);
            if (!CurrentHead(finalPullRequest.RootElement, reference.CommitSha))
                Add("OWNER_REVIEW_HEAD_CHANGED", "The review target changed while resolving the approved artifact bytes.");
            using var finalSelected = await ReadAsync(client, prefix + "/reviews/" + reference.ReviewId.ToString(CultureInfo.InvariantCulture));
            if (!Approved(finalSelected.RootElement, reference, independentlySuppliedOwner, asOfUtc) ||
                !ScopedApproval(finalSelected.RootElement, reference) ||
                await LatestOwnerDecisionAsync(client, prefix, independentlySuppliedOwner) != reference.ReviewId)
                Add("OWNER_REVIEW_CHANGED", "The scoped owner approval was dismissed, changed or superseded while resolving its artifact bytes.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or
            FormatException or KeyNotFoundException or TaskCanceledException)
        {
            // Never emit API bodies, paths, tokens, private content or transport error text.
            Add("OWNER_REVIEW_UNAVAILABLE", "Live authenticated review/artifact authority could not be completely resolved.");
        }
        return Result(diagnostics);
    }

    public static HttpClient CreateClient(string? token)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Coglatas-Spec-Registry/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync("https://api.github.com/" + path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength is > 2_000_000)
            throw new HttpRequestException();
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
        {
            if (bytes.Length + count > 2_000_000) throw new HttpRequestException();
            await bytes.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
        }
        return JsonDocument.Parse(bytes.ToArray());
    }

    private static string Login(JsonElement review) => review.GetProperty("user").GetProperty("login").GetString() ?? "";
    public static string RequiredApprovalBody(GitHubReviewReference reference) =>
        "I personally approve the SEC-ARCH canonical SPEC registry and security-contract mappings at the exact reviewed revision below. " +
        "This is SA-02 mapping approval only; it does not approve RLS identity classification, concrete policy/role changes, product activation, exceptions, or gate promotion.\n" +
        "reviewed-commit: " + reference.CommitSha + "\n" +
        string.Join('\n', reference.Artifacts.OrderBy(a => a.Kind).Select(a => a.Kind.ToString().ToLowerInvariant() + "-sha256: " + a.Digest));
    private static bool ScopedApproval(JsonElement review, GitHubReviewReference reference) =>
        review.GetProperty("body").GetString()?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() == RequiredApprovalBody(reference);
    private static async Task<long> LatestOwnerDecisionAsync(HttpClient client, string prefix, string owner)
    {
        long latest = 0;
        for (var page = 1; page <= 100; page++)
        {
            using var reviews = await ReadAsync(client, prefix + "/reviews?per_page=100&page=" + page.ToString(CultureInfo.InvariantCulture));
            if (reviews.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException();
            foreach (var review in reviews.RootElement.EnumerateArray())
                if (Login(review).Equals(owner, StringComparison.OrdinalIgnoreCase) &&
                    review.GetProperty("state").GetString() is "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED")
                    latest = Math.Max(latest, review.GetProperty("id").GetInt64());
            if (reviews.RootElement.GetArrayLength() < 100) return latest;
        }
        throw new JsonException(); // Incomplete review history cannot qualify authority.
    }
    private static bool CurrentHead(JsonElement pullRequest, string sha) =>
        !pullRequest.GetProperty("draft").GetBoolean() && pullRequest.GetProperty("state").GetString() == "open" &&
        pullRequest.GetProperty("head").GetProperty("sha").GetString() == sha;
    private static bool Approved(JsonElement review, GitHubReviewReference reference, string owner, DateTimeOffset asOfUtc) =>
        review.GetProperty("id").GetInt64() == reference.ReviewId &&
        Login(review).Equals(owner, StringComparison.OrdinalIgnoreCase) &&
        review.GetProperty("state").GetString() == "APPROVED" &&
        review.GetProperty("commit_id").GetString() == reference.CommitSha &&
        DateTimeOffset.TryParse(review.GetProperty("submitted_at").GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var submittedAt) && submittedAt != default && submittedAt <= asOfUtc;
    private static OwnerReviewResult Result(IEnumerable<Diagnostic> diagnostics)
    {
        var ordered = ContractValidator.Result(diagnostics);
        return new(ordered.Valid, ordered.Valid ? "REVIEW_REFERENCE_BOUND" : "UNVERIFIED", ordered.Diagnostics);
    }
}
