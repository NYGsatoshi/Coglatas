using Coglatas.Application.Security.Redaction;

namespace Coglatas.Tests.Security;

public sealed class RedactionAppliedSemanticsTests
{
    [Fact]
    public void SensitiveError_AlreadyAtCanonicalPublicShape_DoesNotClaimVisibleRedaction()
    {
        var service = new CanonicalRedactionService();
        var source = new ErrorRedactionSource(
            "LookupFailed",
            "The request could not be completed.",
            null,
            Array.Empty<object>(),
            RedactionSensitivity.Sensitive);
        var context = new AuthorizationContext(
            ActorId: null,
            TenantId: null,
            ModuleKey: "Tests",
            Purpose: "NormalOperation",
            RequestId: "request-test",
            AuthorizationState: RedactionAuthorizationState.Unknown);

        var result = service.Redact(context, source, RedactionProfile.ErrorResponse);

        Assert.False(result.RedactionApplied);
        var redacted = Assert.IsType<ErrorRedactionSource>(result.Value);
        Assert.Equal("The request could not be completed.", redacted.Message);
        Assert.Null(redacted.Target);
        Assert.Empty(redacted.Details);
    }
}
