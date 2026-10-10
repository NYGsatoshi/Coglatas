using System.Net;
using System.Text;
using System.Text.Json;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureOwnerReviewTests
{
    private static readonly string Sha = new('a', 40);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
    private const string RegistryBytes = "synthetic registry bytes";
    private const string MappingBytes = "synthetic mapping bytes";
    private const string ContractBytes = "synthetic contract bytes";

    private static GitHubReviewReference Reference() => new("synthetic-owner/synthetic-private-spec", 88, 100, Sha,
        [new(SpecReviewArtifactKind.Registry, "synthetic/registry.json", SpecDigest.Text(RegistryBytes)),
            new(SpecReviewArtifactKind.Traceability, "synthetic/mappings.json", SpecDigest.Text(MappingBytes))]);

    [Fact]
    public async Task LiveReviewAdapterRequiresScopedOwnerReviewAndIndependentExactArtifactBytes()
    {
        using var client = new HttpClient(new SyntheticGitHubHandler("valid"));
        var result = await GitHubOwnerReviewVerifier.VerifyAsync(Reference(), "synthetic-owner", Now, client);
        Assert.True(result.Verified);
        Assert.Equal("REVIEW_REFERENCE_BOUND", result.Status);
        Assert.Empty(result.Diagnostics);
        var withContracts = Reference() with { Artifacts = [.. Reference().Artifacts,
            new(SpecReviewArtifactKind.Contracts, "synthetic/contracts.json", SpecDigest.Text(ContractBytes))] };
        using var contractClient = new HttpClient(new SyntheticGitHubHandler("valid", withContracts));
        var contractResult = await GitHubOwnerReviewVerifier.VerifyAsync(withContracts, "synthetic-owner", Now, contractClient);
        Assert.True(contractResult.Verified);
        Assert.Contains("contracts-sha256: " + SpecDigest.Text(ContractBytes), GitHubOwnerReviewVerifier.RequiredApprovalBody(withContracts));
        // These are deliberately synthetic HTTP responses, not an observed personal approval.
    }

    [Theory]
    [InlineData("ordinary-code-review")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-review-id")]
    [InlineData("wrong-review-commit")]
    [InlineData("dismissed")]
    [InlineData("pending")]
    [InlineData("future-review")]
    [InlineData("draft")]
    [InlineData("closed")]
    [InlineData("new-head")]
    [InlineData("superseded")]
    [InlineData("wrong-scope-digest")]
    [InlineData("wrong-artifact-bytes")]
    [InlineData("missing-artifact")]
    [InlineData("api-unavailable")]
    [InlineData("head-changed-during-resolution")]
    [InlineData("dismissed-during-resolution")]
    [InlineData("superseded-during-resolution")]
    public async Task ForgedUnscopedAndStaleReviewAuthorityCannotQualify(string mutation)
    {
        using var client = new HttpClient(new SyntheticGitHubHandler(mutation));
        var result = await GitHubOwnerReviewVerifier.VerifyAsync(Reference(), "synthetic-owner", Now, client);
        Assert.False(result.Verified);
        Assert.Equal("UNVERIFIED", result.Status);
        var json = JsonSerializer.Serialize(result, ContractJson.Options);
        Assert.DoesNotContain(RegistryBytes, json);
        Assert.DoesNotContain(MappingBytes, json);
        Assert.DoesNotContain("synthetic/registry.json", json);
    }

    [Fact]
    public async Task LocalApprovalDocumentCannotReplaceLiveAuthority()
    {
        using var client = new HttpClient(new SyntheticGitHubHandler("api-unavailable"));
        var result = await GitHubOwnerReviewVerifier.VerifyAsync(Reference(), "synthetic-owner", Now, client);
        Assert.False(result.Verified);
        var json = JsonSerializer.Serialize(Reference(), ContractJson.Options)
            .Replace("\"repository\":", "\"ownerApproved\": true, \"repository\":");
        Assert.Throws<JsonException>(() => ContractJson.Read<GitHubReviewReference>(json));
    }

    private sealed class SyntheticGitHubHandler(string mutation, GitHubReviewReference? reference = null) : HttpMessageHandler
    {
        private readonly GitHubReviewReference _reference = reference ?? Reference();
        private int _headReads;
        private int _reviewReads;
        private int _historyReads;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.github.com", request.RequestUri.Host);
            if (mutation == "api-unavailable") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var path = request.RequestUri.AbsolutePath;
            object payload;
            if (path.EndsWith("/pulls/88", StringComparison.Ordinal))
            {
                _headReads++;
                payload = new { draft = mutation == "draft", state = mutation == "closed" ? "closed" : "open",
                    head = new { sha = mutation == "new-head" || mutation == "head-changed-during-resolution" && _headReads > 1 ? new string('c', 40) : Sha } };
            }
            else if (path.EndsWith("/reviews/100", StringComparison.Ordinal))
            {
                _reviewReads++;
                payload = Review(100, mutation == "dismissed-during-resolution" && _reviewReads > 1 ? "DISMISSED" : "APPROVED");
            }
            else if (path.EndsWith("/reviews", StringComparison.Ordinal))
            {
                _historyReads++;
                var superseded = mutation == "superseded" || mutation == "superseded-during-resolution" && _historyReads > 1;
                payload = superseded ? new[] { Review(100), Review(101, "CHANGES_REQUESTED") } : [Review(100)];
            }
            else
            {
                if (mutation == "missing-artifact") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var bytes = path.EndsWith("registry.json", StringComparison.Ordinal) ? RegistryBytes :
                    path.EndsWith("contracts.json", StringComparison.Ordinal) ? ContractBytes : MappingBytes;
                if (mutation == "wrong-artifact-bytes") bytes += "synthetic mutation";
                payload = new { type = "file", encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(bytes)) };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") });
        }

        private object Review(long id, string state = "APPROVED") => new
        {
            id = mutation == "wrong-review-id" ? 999 : id,
            user = new { login = mutation == "wrong-owner" ? "synthetic-impostor" : "synthetic-owner" },
            state = mutation == "dismissed" ? "DISMISSED" : mutation == "pending" ? "PENDING" : state,
            commit_id = mutation == "wrong-review-commit" ? new string('c', 40) : Sha,
            submitted_at = mutation == "future-review" ? Now.AddDays(1).ToString("O") : Now.AddMinutes(-1).ToString("O"),
            body = mutation == "ordinary-code-review" ? "Ordinary synthetic code review approval." :
                mutation == "wrong-scope-digest" ? GitHubOwnerReviewVerifier.RequiredApprovalBody(_reference).Replace(SpecDigest.Text(RegistryBytes), new string('f', 64)) :
                GitHubOwnerReviewVerifier.RequiredApprovalBody(_reference)
        };
    }
}
