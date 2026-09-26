using System.Text.Json.Nodes;
using Coglatas.Application;
using Coglatas.Application.Projects;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.Security;

[Trait("Scope", "WPCFinal03")]
public sealed class WpcFinal03FileMetadataRedactionTests
{
    [Fact]
    public void StandardAuthorizedPolicyRedactsCanonicalSensitiveMetadataFields()
    {
        var workspaceId = Guid.NewGuid();
        var source = new
        {
            OriginalFileName = "student-evidence.pdf",
            UploadedByUserId = Guid.NewGuid(),
            UploadedByDisplayName = "Student Name",
            TargetLabel = "Private review task",
            Classification = "Restricted",
            WorkspaceId = workspaceId,
            SizeBytes = 4096L
        };
        var service = CreateService();

        var result = service.Redact(
            AllowedContext(FieldAccessPolicySnapshot.StandardAuthorized),
            source,
            RedactionProfile.FileMetadata);

        Assert.True(result.RedactionApplied);
        var projected = Assert.IsType<JsonObject>(result.Value);
        Assert.Equal("[redacted:file]", projected["originalFileName"]!.GetValue<string>());
        Assert.True(projected.ContainsKey("uploadedByUserId"));
        Assert.Null(projected["uploadedByUserId"]);
        Assert.Equal("[redacted:confidential]", projected["uploadedByDisplayName"]!.GetValue<string>());
        Assert.Equal("[redacted:confidential]", projected["targetLabel"]!.GetValue<string>());
        Assert.Equal("[redacted:confidential]", projected["classification"]!.GetValue<string>());
        Assert.Equal(workspaceId, projected["workspaceId"]!.GetValue<Guid>());
        Assert.Equal(4096L, projected["sizeBytes"]!.GetValue<long>());
    }

    [Fact]
    public void ExplicitConfidentialFieldPolicyPreservesAuthorizedMetadata()
    {
        var source = new
        {
            OriginalFileName = "authorized.pdf",
            UploadedByDisplayName = "Authorized User",
            TargetLabel = "Authorized target",
            Classification = "Internal"
        };
        var service = CreateService();

        var result = service.Redact(
            AllowedContext(FieldAccessPolicySnapshot.ThroughConfidential),
            source,
            RedactionProfile.FileMetadata);

        Assert.False(result.RedactionApplied);
        Assert.Same(source, result.Value);
    }

    [Fact]
    public void NonAllowedAuthorizationStillFailsClosedBeforeMetadataProjection()
    {
        var service = CreateService();
        var context = new AuthorizationContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Files",
            RedactionPurpose.NormalOperation,
            "wpc-final03-denied",
            RedactionAuthorizationState.Unknown);

        var result = service.Redact(
            context,
            new { OriginalFileName = "must-not-leak.pdf" },
            RedactionProfile.FileMetadata);

        Assert.True(result.RedactionApplied);
        Assert.IsType<RedactedPayload>(result.Value);
    }

    [Fact]
    public void ApplicationRegistrationUsesTheHardenedCanonicalBoundary()
    {
        using var provider = new ServiceCollection()
            .AddApplication()
            .BuildServiceProvider();

        var service = provider.GetRequiredService<IRedactionService>();

        Assert.IsType<CanonicalFileMetadataRedactionService>(service);
    }

    [Fact]
    public void TaskFileResponsesUseFileMetadataProfileAndRedactNestedFileNames()
    {
        var item = new TaskFileAssociationResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "student-private-evidence.pdf",
            "application/pdf",
            8192,
            "Clean",
            DateTimeOffset.UtcNow,
            "Available",
            CanOpen: true,
            CanRequestDownloadGrant: true,
            DownloadGrantRequired: false,
            RestrictionCode: null);
        var page = new TaskFileAssociationPage([item], 1, 20, 1, HasMore: false);

        Assert.Equal(
            RedactionProfile.FileMetadata,
            CanonicalProjectsResponseProjectionFilter.ProfileFor(item));
        Assert.Equal(
            RedactionProfile.FileMetadata,
            CanonicalProjectsResponseProjectionFilter.ProfileFor(page));

        var result = CreateService().Redact(
            AllowedContext(FieldAccessPolicySnapshot.StandardAuthorized),
            page,
            RedactionProfile.FileMetadata);

        Assert.True(result.RedactionApplied);
        var projected = Assert.IsType<JsonObject>(result.Value);
        var items = Assert.IsType<JsonArray>(projected["items"]);
        var projectedItem = Assert.IsType<JsonObject>(Assert.Single(items));
        Assert.Equal("[redacted:file]", projectedItem["fileName"]!.GetValue<string>());
    }

    private static IRedactionService CreateService() =>
        new CanonicalFileMetadataRedactionService(new CanonicalRedactionService());

    private static AuthorizationContext AllowedContext(FieldAccessPolicySnapshot policy) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Files",
            RedactionPurpose.NormalOperation,
            "wpc-final03-file-metadata",
            RedactionAuthorizationState.Allowed,
            policy);
}
