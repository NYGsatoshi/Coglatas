using System.Diagnostics;
using System.Text.Json.Nodes;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Http;

namespace Coglatas.Tests.Security;

[Trait("Portability", "CrossPlatform")]
public sealed class CanonicalRedactionServiceTests
{
    [Fact]
    public void RedactionProfile_ContainsCanonicalProfilesOnly()
    {
        var names = Enum.GetNames<RedactionProfile>();

        Assert.Equal(
            new[]
            {
                "UiList",
                "UiDetail",
                "SearchSnippet",
                "ExportRow",
                "AuditDisplay",
                "NotificationPayload",
                "FileMetadata",
                "ErrorResponse"
            },
            names);
    }

    [Theory]
    [InlineData(RedactionAuthorizationState.Denied, RedactionProfile.UiList)]
    [InlineData(RedactionAuthorizationState.Denied, RedactionProfile.UiDetail)]
    [InlineData(RedactionAuthorizationState.Denied, RedactionProfile.SearchSnippet)]
    [InlineData(RedactionAuthorizationState.Denied, RedactionProfile.ExportRow)]
    [InlineData(RedactionAuthorizationState.Denied, RedactionProfile.AuditDisplay)]
    [InlineData(RedactionAuthorizationState.Denied, RedactionProfile.NotificationPayload)]
    [InlineData(RedactionAuthorizationState.Denied, RedactionProfile.FileMetadata)]
    [InlineData(RedactionAuthorizationState.Unknown, RedactionProfile.UiList)]
    [InlineData(RedactionAuthorizationState.Unknown, RedactionProfile.UiDetail)]
    [InlineData(RedactionAuthorizationState.Unknown, RedactionProfile.SearchSnippet)]
    [InlineData(RedactionAuthorizationState.Unknown, RedactionProfile.ExportRow)]
    [InlineData(RedactionAuthorizationState.Unknown, RedactionProfile.AuditDisplay)]
    [InlineData(RedactionAuthorizationState.Unknown, RedactionProfile.NotificationPayload)]
    [InlineData(RedactionAuthorizationState.Unknown, RedactionProfile.FileMetadata)]
    public void NonErrorProfile_UnknownOrDeniedAuthorization_FailsClosed(
        RedactionAuthorizationState authorizationState,
        RedactionProfile profile)
    {
        var service = new CanonicalRedactionService();
        var source = new { secret = "must-not-pass-through" };
        var context = CreateContext(authorizationState);

        var result = service.Redact(context, source, profile);

        Assert.True(result.RedactionApplied);
        var redacted = Assert.IsType<RedactedPayload>(result.Value);
        Assert.Equal(profile, redacted.Profile);
        Assert.Equal("authorization", redacted.Reason);
        Assert.NotSame(source, result.Value);
    }

    [Theory]
    [InlineData(RedactionProfile.UiList)]
    [InlineData(RedactionProfile.UiDetail)]
    [InlineData(RedactionProfile.SearchSnippet)]
    [InlineData(RedactionProfile.ExportRow)]
    [InlineData(RedactionProfile.AuditDisplay)]
    [InlineData(RedactionProfile.NotificationPayload)]
    [InlineData(RedactionProfile.FileMetadata)]
    public void AllowedProfile_AppliesProductionFieldPolicy(RedactionProfile profile)
    {
        var service = new CanonicalRedactionService();
        var source = new
        {
            Email = "student@example.invalid",
            OriginalFileName = "restricted-plan.pdf",
            Snippet = "confidential snippet",
            Summary = "sensitive summary",
            Message = "You have a new message.",
            Body = "confidential body",
            HealthNotes = "restricted health data",
            StorageKey = "tenant/private/object",
            HashSha256 = "0123456789abcdef",
            SafeId = Guid.NewGuid()
        };

        var result = service.Redact(
            CreateContext(RedactionAuthorizationState.Allowed),
            source,
            profile);

        Assert.True(result.RedactionApplied);
        var projected = Assert.IsType<JsonObject>(result.Value);
        Assert.Equal("[redacted:restricted]", projected["healthNotes"]?.GetValue<string>());
        Assert.False(projected.ContainsKey("storageKey"));
        Assert.False(projected.ContainsKey("hashSha256"));
        Assert.NotNull(projected["safeId"]);

        if (profile is RedactionProfile.UiList or RedactionProfile.UiDetail or RedactionProfile.ExportRow)
        {
            Assert.Equal("[redacted:email]", projected["email"]?.GetValue<string>());
        }

        if (profile == RedactionProfile.ExportRow)
        {
            Assert.Equal("[redacted:file]", projected["originalFileName"]?.GetValue<string>());
        }

        if (profile == RedactionProfile.FileMetadata)
        {
            Assert.Equal("restricted-plan.pdf", projected["originalFileName"]?.GetValue<string>());
        }

        if (profile == RedactionProfile.SearchSnippet)
        {
            Assert.Equal("[redacted:restricted]", projected["snippet"]?.GetValue<string>());
        }

        if (profile == RedactionProfile.AuditDisplay)
        {
            Assert.Equal("[redacted:restricted]", projected["summary"]?.GetValue<string>());
        }

        if (profile == RedactionProfile.NotificationPayload)
        {
            Assert.Equal("[redacted:restricted]", projected["body"]?.GetValue<string>());
            Assert.Equal("You have a new message.", projected["message"]?.GetValue<string>());
        }
    }

    [Fact]
    public void AllowedProfile_FieldAccessPolicyCanPermitConfidentialField()
    {
        var service = new CanonicalRedactionService();
        var source = new
        {
            Email = "authorized@example.invalid",
            HealthNotes = "still restricted"
        };

        var result = service.Redact(
            CreateContext(
                RedactionAuthorizationState.Allowed,
                FieldAccessPolicySnapshot.ThroughConfidential),
            source,
            RedactionProfile.UiDetail);

        Assert.True(result.RedactionApplied);
        var projected = Assert.IsType<JsonObject>(result.Value);
        Assert.Equal("authorized@example.invalid", projected["email"]?.GetValue<string>());
        Assert.Equal("[redacted:restricted]", projected["healthNotes"]?.GetValue<string>());
    }

    [Fact]
    public void RestrictedField_RequiresExplicitFieldGrant()
    {
        var service = new CanonicalRedactionService();
        var source = new
        {
            HealthNotes = "authorized restricted value",
            SafeId = Guid.NewGuid()
        };

        var deniedByFieldPolicy = service.Redact(
            CreateContext(RedactionAuthorizationState.Allowed),
            source,
            RedactionProfile.UiDetail);
        Assert.True(deniedByFieldPolicy.RedactionApplied);
        Assert.Equal(
            "[redacted:restricted]",
            Assert.IsType<JsonObject>(deniedByFieldPolicy.Value)["healthNotes"]?.GetValue<string>());

        var allowedByFieldPolicy = service.Redact(
            CreateContext(
                RedactionAuthorizationState.Allowed,
                FieldAccessPolicySnapshot.ThroughRestrictedFields("HealthNotes")),
            source,
            RedactionProfile.UiDetail);

        Assert.False(allowedByFieldPolicy.RedactionApplied);
        Assert.Same(source, allowedByFieldPolicy.Value);
    }

    [Fact]
    public void Token_IsDefaultDeny_ExceptExactFileDownloadGrantIssuanceBoundary()
    {
        var service = new CanonicalRedactionService();
        var source = new
        {
            Token = "one-time-download-token",
            SafeId = Guid.NewGuid()
        };

        var defaultResult = service.Redact(
            CreateContext(RedactionAuthorizationState.Allowed),
            source,
            RedactionProfile.UiDetail);
        Assert.True(defaultResult.RedactionApplied);
        Assert.False(Assert.IsType<JsonObject>(defaultResult.Value).ContainsKey("token"));

        var exactGrantContext = new AuthorizationContext(
            ActorId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            ModuleKey: "FileDownloadGrant",
            Purpose: RedactionPurpose.FileDownload,
            RequestId: "request-download-grant",
            AuthorizationState: RedactionAuthorizationState.Allowed);
        var grantResult = service.Redact(
            exactGrantContext,
            source,
            RedactionProfile.UiDetail);
        Assert.False(grantResult.RedactionApplied);
        Assert.Same(source, grantResult.Value);

        var wrongPurposeContext = new AuthorizationContext(
            ActorId: exactGrantContext.ActorId,
            TenantId: exactGrantContext.TenantId,
            ModuleKey: exactGrantContext.ModuleKey,
            Purpose: RedactionPurpose.NormalOperation,
            RequestId: exactGrantContext.RequestId,
            AuthorizationState: RedactionAuthorizationState.Allowed);
        var wrongPurposeResult = service.Redact(
            wrongPurposeContext,
            source,
            RedactionProfile.UiDetail);
        Assert.True(wrongPurposeResult.RedactionApplied);
        Assert.False(Assert.IsType<JsonObject>(wrongPurposeResult.Value).ContainsKey("token"));
    }

    [Fact]
    public void AuthorizationContext_RejectsFreeFormPurpose()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthorizationContext(
            ActorId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            ModuleKey: "Tests",
            Purpose: "FreeFormPurpose",
            RequestId: "request-test",
            AuthorizationState: RedactionAuthorizationState.Allowed));
    }

    [Fact]
    public void ErrorResponse_PublicSafeContent_PassesThroughWithoutClaimingRedaction()
    {
        var service = new CanonicalRedactionService();
        var source = new ErrorRedactionSource(
            "ValidationFailed",
            "The request body or parameters are invalid.",
            "body",
            Array.Empty<object>(),
            RedactionSensitivity.PublicSafe);

        var result = service.Redact(
            CreateContext(RedactionAuthorizationState.Unknown),
            source,
            RedactionProfile.ErrorResponse);

        Assert.False(result.RedactionApplied);
        Assert.Equal(source, Assert.IsType<ErrorRedactionSource>(result.Value));
    }

    [Theory]
    [InlineData(RedactionAuthorizationState.Denied)]
    [InlineData(RedactionAuthorizationState.Unknown)]
    public void ErrorResponse_SensitiveContent_FailsClosedAndReportsActualChange(
        RedactionAuthorizationState authorizationState)
    {
        var service = new CanonicalRedactionService();
        var source = new ErrorRedactionSource(
            "LookupFailed",
            "Sensitive internal failure for student@example.invalid",
            "body.studentEmail",
            new object[] { "database detail" },
            RedactionSensitivity.Sensitive);

        var result = service.Redact(
            CreateContext(authorizationState),
            source,
            RedactionProfile.ErrorResponse);

        Assert.True(result.RedactionApplied);
        var redacted = Assert.IsType<ErrorRedactionSource>(result.Value);
        Assert.Equal("LookupFailed", redacted.Code);
        Assert.Equal("The request could not be completed.", redacted.Message);
        Assert.Null(redacted.Target);
        Assert.Empty(redacted.Details);
        Assert.Equal(RedactionSensitivity.PublicSafe, redacted.Sensitivity);
    }

    [Fact]
    public void ErrorResponse_SensitiveContent_WithAffirmativeAuthorization_PassesThrough()
    {
        var service = new CanonicalRedactionService();
        var source = new ErrorRedactionSource(
            "LookupFailed",
            "Authorized diagnostic",
            "body.value",
            new object[] { "detail" },
            RedactionSensitivity.Sensitive);

        var result = service.Redact(
            CreateContext(RedactionAuthorizationState.Allowed),
            source,
            RedactionProfile.ErrorResponse);

        Assert.False(result.RedactionApplied);
        Assert.Equal(source, Assert.IsType<ErrorRedactionSource>(result.Value));
    }

    [Fact]
    public void WpcEnvelope_PublicSafeError_PreservesRequestAndTraceIds_AndReportsNoRedaction()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "request-123"
        };
        using var activity = new Activity("redaction-test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var envelope = ApiEnvelope.Error(
            httpContext,
            StatusCodes.Status400BadRequest,
            "ValidationFailed",
            "The request body or parameters are invalid.",
            "body");

        Assert.Equal("request-123", envelope.RequestId);
        Assert.Equal(activity.Id, envelope.TraceId);
        Assert.Equal(StatusCodes.Status400BadRequest, envelope.Status);
        Assert.Equal("ValidationFailed", envelope.Error.Code);
        Assert.Equal("The request body or parameters are invalid.", envelope.Error.Message);
        Assert.Equal("body", envelope.Error.Target);
        Assert.False(envelope.Error.RedactionApplied);
    }

    [Fact]
    public void WpcEnvelope_SensitiveError_UsesCanonicalErrorResponseRedaction()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "request-sensitive"
        };

        var envelope = ApiEnvelope.Error(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "LookupFailed",
            "Sensitive internal failure",
            "body.secret",
            redactionApplied: true);

        Assert.Equal("request-sensitive", envelope.RequestId);
        Assert.False(string.IsNullOrWhiteSpace(envelope.TraceId));
        Assert.Equal("LookupFailed", envelope.Error.Code);
        Assert.Equal("The request could not be completed.", envelope.Error.Message);
        Assert.Null(envelope.Error.Target);
        Assert.Empty(envelope.Error.Details);
        Assert.True(envelope.Error.RedactionApplied);
    }

    private static AuthorizationContext CreateContext(
        RedactionAuthorizationState authorizationState,
        FieldAccessPolicySnapshot? fieldAccessPolicy = null) =>
        new(
            ActorId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            ModuleKey: "Tests",
            Purpose: RedactionPurpose.NormalOperation,
            RequestId: "request-test",
            AuthorizationState: authorizationState,
            FieldAccessPolicy: fieldAccessPolicy);
}
