using System.Reflection;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Web.Controllers;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Coglatas.Tests.OpenApi;

public sealed class SurveyFeedbackResponseMetadataTests
{
    [Theory]
    [InlineData(typeof(PresentationFeedbackController), nameof(PresentationFeedbackController.ListWorkspaces),
        StatusCodes.Status200OK, typeof(ApiSuccessEnvelope<FeedbackPage<PresentationWorkspace>>))]
    [InlineData(typeof(PresentationFeedbackController), nameof(PresentationFeedbackController.GetInbox),
        StatusCodes.Status200OK, typeof(ApiSuccessEnvelope<FeedbackInboxPage>))]
    [InlineData(typeof(FeedbackNotesController), nameof(FeedbackNotesController.CreatePrivate),
        StatusCodes.Status201Created, typeof(ApiSuccessEnvelope<PrivateNoteDto>))]
    [InlineData(typeof(FeedbackNotesController), nameof(FeedbackNotesController.ReadShared),
        StatusCodes.Status200OK, typeof(ApiSuccessEnvelope<FeedbackPage<SharedFeedbackDto>>))]
    [InlineData(typeof(FeedbackInvitationsController), nameof(FeedbackInvitationsController.Issue),
        StatusCodes.Status201Created, typeof(ApiSuccessEnvelope<FeedbackInvitationDto>))]
    [InlineData(typeof(FeedbackInvitationsController), nameof(FeedbackInvitationsController.Approve),
        StatusCodes.Status200OK, typeof(ApiSuccessEnvelope<FeedbackInvitationDto>))]
    [InlineData(typeof(FeedbackModerationController), nameof(FeedbackModerationController.Report),
        StatusCodes.Status201Created, typeof(ApiSuccessEnvelope<FeedbackReportDto>))]
    [InlineData(typeof(FeedbackModerationController), nameof(FeedbackModerationController.Void),
        StatusCodes.Status200OK, typeof(ApiSuccessEnvelope<FeedbackSubmissionDto>))]
    [InlineData(typeof(FeedbackCsvController), nameof(FeedbackCsvController.Create),
        StatusCodes.Status202Accepted, typeof(ApiSuccessEnvelope<FeedbackCsvQueued>))]
    [InlineData(typeof(FeedbackCsvController), nameof(FeedbackCsvController.GetStatus),
        StatusCodes.Status200OK, typeof(ApiSuccessEnvelope<FeedbackCsvStatus>))]
    [InlineData(typeof(FeedbackCsvController), nameof(FeedbackCsvController.CreateGrant),
        StatusCodes.Status201Created, typeof(ApiSuccessEnvelope<FeedbackCsvGrantReceipt>))]
    public void AdvertisesActualJsonSuccessEnvelope(Type controller, string action, int status, Type envelope)
    {
        var metadata = controller.GetMethod(action)!.GetCustomAttributes<ProducesResponseTypeAttribute>();
        var success = Assert.Single(metadata, response => response.StatusCode is >= 200 and < 300);
        Assert.Equal(status, success.StatusCode);
        Assert.Equal(envelope, success.Type);
        Assert.Equal("application/json", ResponseContentType(success));
    }

    [Fact]
    public void CsvDownloadAdvertisesRawCsvAndBothSizeRejectionBodies()
    {
        var metadata = typeof(FeedbackCsvController).GetMethod(nameof(FeedbackCsvController.Download))!
            .GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        var success = Assert.Single(metadata, response => response.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(string), success.Type);
        Assert.Equal("text/csv", ResponseContentType(success));
        var sizeErrors = metadata.Where(response => response.StatusCode == StatusCodes.Status413PayloadTooLarge).ToArray();
        Assert.Equal(2, sizeErrors.Length);
        Assert.Contains(sizeErrors, response => response.Type == typeof(ErrorResponse));
        Assert.Contains(sizeErrors, response => response.Type == typeof(ApiErrorEnvelope));
        Assert.All(sizeErrors, response => Assert.Equal("application/json", ResponseContentType(response)));
        Assert.Contains(metadata, response => response.StatusCode == StatusCodes.Status410Gone && response.Type == typeof(ApiErrorEnvelope));
    }

    private static string ResponseContentType(ProducesResponseTypeAttribute response)
    {
        var contentTypes = new MediaTypeCollection();
        ((IApiResponseMetadataProvider)response).SetContentTypes(contentTypes);
        return Assert.Single(contentTypes);
    }
}
