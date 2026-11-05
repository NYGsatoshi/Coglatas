using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace Coglatas.Tests.Artifacts;

public sealed class WorkspaceArtifactServiceTests
{
    [Fact]
    public async Task ExtraAuthorizedRowCreatesBoundCursorAndNeverReturnsAggregateCount()
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        fixture.Repository.Rows = Enumerable.Range(1, 3).Select(fixture.Item).ToArray();
        var result = await fixture.ListAsync(new(PageSize: 2));
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.True(result.Value.HasMore);
        Assert.Null(result.Value.TotalCount);
        Assert.True(fixture.Codec.TryDecode(result.Value.NextCursor!, out var cursor));
        Assert.Equal(fixture.Tenant.TenantId, cursor!.TenantId);
        Assert.Equal(fixture.User.UserId, cursor.ActorUserId);
        Assert.Equal(fixture.Authorization.WorkspaceId, cursor.WorkspaceId);
        Assert.NotNull(cursor.After);
        Assert.Equal(fixture.Repository.Rows[1].Id, cursor.After.Id);
        Assert.Equal(fixture.Repository.Rows[1].CreatedAt, cursor.After.UpdatedAtUtc);
        Assert.Equal(fixture.Clock.UtcNow.AddMinutes(15), cursor.ExpiresAtUtc);
        Assert.Equal(2, cursor.PageSize);
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        Assert.Equal(JsonValueKind.Null, json.GetProperty("totalCount").ValueKind);
        Assert.False(json.TryGetProperty("tenantId", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EmptyOrExactPageHasNoCursor(int count)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        fixture.Repository.Rows = Enumerable.Range(1, count).Select(fixture.Item).ToArray();
        var result = await fixture.ListAsync(new(PageSize: 2));
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasMore);
        Assert.Null(result.Value.NextCursor);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(500, 100)]
    public async Task EffectivePageSizeIsBoundedBeforeRepositoryAndCursor(int requested, int expected)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        await fixture.ListAsync(new(PageSize: requested));
        Assert.Equal(expected, fixture.Repository.LastQuery!.PageSize);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("workspace")]
    [InlineData("query")]
    [InlineData("pageSize")]
    [InlineData("sort")]
    [InlineData("direction")]
    [InlineData("version")]
    [InlineData("expired")]
    [InlineData("emptyId")]
    public async Task AuthenticCursorForDifferentContextAlwaysFailsWithoutReading(string difference)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var cursor = fixture.Cursor();
        cursor = difference switch
        {
            "actor" => cursor with { ActorUserId = Guid.NewGuid() },
            "tenant" => cursor with { TenantId = Guid.NewGuid() },
            "workspace" => cursor with { WorkspaceId = Guid.NewGuid() },
            "query" => cursor with { Query = "private other search" },
            "pageSize" => cursor with { PageSize = 10 },
            "sort" => cursor with { Sort = "createdAt" },
            "direction" => cursor with { Direction = "asc" },
            "version" => cursor with { FormatVersion = 2 },
            "emptyId" => cursor with { After = cursor.After! with { Id = Guid.Empty } },
            _ => cursor with { ExpiresAtUtc = fixture.Clock.UtcNow }
        };
        var result = await fixture.ListAsync(new(Cursor: fixture.Codec.Encode(cursor)));
        Assert.Equal("ArtifactCursorInvalid", result.ErrorDetail!.Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public async Task CursorExpirationIsEnforcedOnEveryPageAndNeverRefreshesOldToken()
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var cursor = fixture.Cursor();
        var token = fixture.Codec.Encode(cursor);
        Assert.True((await fixture.ListAsync(new(Cursor: token))).IsSuccess);
        fixture.Clock.UtcNow = cursor.ExpiresAtUtc;
        Assert.Equal("ArtifactCursorInvalid", (await fixture.ListAsync(new(Cursor: token))).ErrorDetail!.Code);
        Assert.Equal(2, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("anonymous")]
    [InlineData("platform")]
    [InlineData("missingTenant")]
    public async Task WorkspaceAdmissionPrecedesCursorErrorsAndRevealsNoCandidates(string state)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        if (state == "denied") fixture.Authorization.Allowed = false;
        if (state == "anonymous") fixture.User.IsAuthenticated = false;
        if (state == "platform") fixture.Tenant.SetPlatformScope();
        if (state == "missingTenant") fixture.Tenant.Clear();
        var result = await fixture.ListAsync(new(Cursor: "invalid"));
        Assert.Equal("WorkspaceNotFound", result.ErrorDetail!.Code);
        Assert.Equal("Workspace not found.", result.ErrorDetail.Message);
        Assert.Null(result.Value);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("actor")]
    [InlineData("tenant")]
    public async Task AuthorityOrContextLossDuringReadDoesNotReturnRowsOrCursor(string change)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        fixture.Repository.Rows = [fixture.Item(1), fixture.Item(2)];
        fixture.Repository.DuringRead = () =>
        {
            if (change == "membership") fixture.Authorization.Allowed = false;
            if (change == "actor") fixture.User.UserId = Guid.NewGuid();
            if (change == "tenant") fixture.Tenant.SetTenant(Guid.NewGuid(), "other-review");
        };
        var result = await fixture.ListAsync(new(PageSize: 1));
        Assert.Equal("WorkspaceNotFound", result.ErrorDetail!.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("embedded\0query")]
    [InlineData("embedded\nquery")]
    [InlineData("embedded\u007fquery")]
    [InlineData("\nleading")]
    [InlineData("trailing\t")]
    public async Task ControlCharactersAreRejected(string query)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var result = await fixture.ListAsync(new(Query: query));
        Assert.Equal("ArtifactQueryInvalid", result.ErrorDetail!.Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public async Task UnicodeLimitCountsCodePointsAndTrimsCursorBoundQuery()
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var query = string.Concat(Enumerable.Repeat("\U0001F4C4", 200));
        Assert.True((await fixture.ListAsync(new(Query: " " + query + " "))).IsSuccess);
        Assert.Equal(query, fixture.Repository.LastQuery!.Query);
        Assert.Equal("ArtifactQueryInvalid", (await fixture.ListAsync(new(Query: query + "x"))).ErrorDetail!.Code);
        Assert.Equal("ArtifactQueryInvalid", (await fixture.ListAsync(new(Query: "\ud800"))).ErrorDetail!.Code);
    }

    [Fact]
    public async Task SortAndDirectionCannotChangeTheInitialContract()
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        Assert.Equal("ArtifactQueryInvalid", (await fixture.ListAsync(new(Sort: "name"))).ErrorDetail!.Code);
        Assert.Equal("ArtifactQueryInvalid", (await fixture.ListAsync(new(Direction: "asc"))).ErrorDetail!.Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public void ProtectedCursorIsOpaqueAuthenticatedPurposeBoundAndSizeBounded()
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var cursor = fixture.Cursor() with { Query = "private synthetic search" };
        var token = fixture.Codec.Encode(cursor);
        Assert.DoesNotContain(cursor.Query, token, StringComparison.Ordinal);
        Assert.True(fixture.Codec.TryDecode(token, out var restored));
        Assert.Equal(cursor, restored);
        var index = token.Length / 2;
        var tampered = token[..index] + (token[index] == 'A' ? 'B' : 'A') + token[(index + 1)..];
        Assert.False(fixture.Codec.TryDecode(tampered, out _));
        Assert.False(fixture.Codec.TryDecode(new string('x', 8193), out _));
        var wrongPurpose = fixture.Keys.CreateProtector("unrelated.review.purpose").Protect(JsonSerializer.Serialize(cursor));
        Assert.False(fixture.Codec.TryDecode(wrongPurpose, out _));
        var codecWithDifferentKeys = new Coglatas.Web.Security.WorkspaceArtifactCursorCodec(new EphemeralDataProtectionProvider());
        Assert.False(codecWithDifferentKeys.TryDecode(token, out _));
        var malformed = fixture.Keys.CreateProtector("Coglatas.WorkspaceArtifacts.Cursor.v1").Protect("{malformed");
        Assert.False(fixture.Codec.TryDecode(malformed, out _));
    }
}
