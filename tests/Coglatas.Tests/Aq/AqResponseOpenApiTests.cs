using System.Reflection;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Web.Controllers;
using Coglatas.Web.Models;
using Coglatas.Web.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqResponseOpenApiTests
{
    private static readonly HttpClient Http = new();

    [Fact]
    public async Task PublishedResponseSchemasMatchTheFieldsOmittedByJsonSerialization()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new SurveyResponseControllers());
        });
        builder.Services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer<SecurityOpenApiSchemaTransformer>();
            options.AddOperationTransformer<SecurityOpenApiOperationTransformer>();
            options.AddOperationTransformer<AqPublicOpenApiOperationTransformer>();
        });
        await using var host = builder.Build();
        host.MapControllers();
        host.MapOpenApi();
        await host.StartAsync();

        var documentUri = new Uri(new Uri(host.Urls.Single()), "/openapi/v1.json");
        using var document = JsonDocument.Parse(await Http.GetStringAsync(documentUri));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        AssertOptionalFields(schemas, "AqPublicProgram", "presentations", "title", "timeZone", "nextCursor");
        AssertOptionalFields(schemas, "AqPublicProgramEntry", "surveyEntryRef",
            "title", "startAt", "endAt", "venue", "presenterDisplayNames");
        AssertOptionalFields(schemas, "AqPublicField", "fieldKey", "options", "constraints");
        AssertOptionalFields(schemas, "AqEventSettings", "startAt", "invitationPolicy");
        AssertOptionalFields(schemas, "AqChangedCode", "versionNo", "entryRef");
        AssertOptionalFields(schemas, "FeedbackInboxPage", "items", "nextCursor", "unresolvedReportCount");
        AssertOptionalFields(schemas, "FeedbackInboxRow", "submission", "reportStatus");
        AssertOptionalFields(schemas, "FeedbackCsvStatus", "state", "expiresAt");
        AssertOptionalFields(schemas, "FeedbackInvitationApprovalRow", "invitation", "inviterLabel", "recipientLabel");
        AssertOptionalFields(schemas, "FeedbackInvitationSummary", "status", "expiresAt");
        AssertOptionalFields(schemas, "FeedbackInvitationDto", "status", "expiresAt");
        AssertOptionalFields(schemas, "FeedbackReportDetail", "reason", "visibility", "dispositionReason", "disposedAtUtc");
        AssertOptionalFields(schemas, "FeedbackAnswer", "visibility", "value", "visibilityVersionNo", "label");
        AssertOptionalFields(schemas, "FeedbackQuestionSummary", "denominator", "distribution", "mean");

        Assert.Equal(new[] { "items", "unreadCount" }, schemas.GetProperty("FeedbackInboxPage")
            .GetProperty("required").EnumerateArray().Select(name => name.GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "readerState", "submission" }, schemas.GetProperty("FeedbackInboxRow")
            .GetProperty("required").EnumerateArray().Select(name => name.GetString()).Order(StringComparer.Ordinal));

        var publicSubmission = document.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/public/surveys/{code}/submissions").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(new[] { "answers", "schemaDigest", "versionRef" }, publicSubmission.GetProperty("required")
            .EnumerateArray().Select(name => name.GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SerializationOmitsUnavailableInboxAndProgramFields()
    {
        var emptyInbox = JsonSerializer.SerializeToElement(
            new ApiSuccessEnvelope<FeedbackInboxPage>("synthetic-request", new([], 0, null, null), []),
            JsonSerializerOptions.Web).GetProperty("data");
        Assert.Empty(emptyInbox.GetProperty("items").EnumerateArray());
        Assert.Equal(0, emptyInbox.GetProperty("unreadCount").GetInt64());
        Assert.False(emptyInbox.TryGetProperty("nextCursor", out _));
        Assert.False(emptyInbox.TryGetProperty("unresolvedReportCount", out _));

        var row = new FeedbackInboxRow(
            new FeedbackRow(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, []),
            new FeedbackReaderState(false, 0), null);
        var readerInbox = JsonSerializer.SerializeToElement(
            new FeedbackInboxPage([row], 1, null, null), JsonSerializerOptions.Web);
        Assert.False(readerInbox.GetProperty("items")[0].TryGetProperty("reportStatus", out _));
        var moderatorInbox = JsonSerializer.SerializeToElement(
            new FeedbackInboxPage([row with { ReportStatus = "open" }], 1, "synthetic-cursor", 2),
            JsonSerializerOptions.Web);
        Assert.Equal("open", moderatorInbox.GetProperty("items")[0].GetProperty("reportStatus").GetString());
        Assert.Equal("synthetic-cursor", moderatorInbox.GetProperty("nextCursor").GetString());
        Assert.Equal(2, moderatorInbox.GetProperty("unresolvedReportCount").GetInt64());

        var program = JsonSerializer.SerializeToElement(new AqPublicProgram(null, null, [], null),
            JsonSerializerOptions.Web);
        Assert.Empty(program.GetProperty("presentations").EnumerateArray());
        Assert.False(program.TryGetProperty("title", out _));
        Assert.False(program.TryGetProperty("timeZone", out _));
        Assert.False(program.TryGetProperty("nextCursor", out _));
    }

    private static void AssertOptionalFields(JsonElement schemas, string schemaName, string requiredField,
        params string[] optionalFields)
    {
        var schema = schemas.GetProperty(schemaName);
        var properties = schema.GetProperty("properties");
        var required = schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ToArray();
        Assert.Contains(requiredField, required);
        foreach (var field in optionalFields)
        {
            Assert.True(properties.TryGetProperty(field, out _), $"{schemaName}.{field} must remain in the schema.");
            Assert.DoesNotContain(field, required);
        }
    }

    private sealed class SurveyResponseControllers : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "AQ response controllers";
        public IEnumerable<TypeInfo> Types =>
        [
            typeof(AqPublicSurveyController).GetTypeInfo(),
            typeof(AqSurveyManagementController).GetTypeInfo(),
            typeof(AqSurveyEntryCodesController).GetTypeInfo(),
            typeof(PresentationFeedbackController).GetTypeInfo(),
            typeof(FeedbackCsvController).GetTypeInfo(),
            typeof(FeedbackInvitationsController).GetTypeInfo(),
            typeof(FeedbackModerationController).GetTypeInfo()
        ];
    }
}
