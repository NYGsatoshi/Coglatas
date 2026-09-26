using System.Text.Json;
using Coglatas.Application.Announcements;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Announcements;

[Trait("Scope", "Issue382")]
public sealed class AnnouncementContentContractTests
{
    [Fact]
    public void PlainBodyRemainsBackwardCompatible()
    {
        var prepared = AnnouncementContentContract.PrepareForPersistence("  Existing announcement body  ");

        Assert.False(prepared.IsEnvelope);
        Assert.Equal("Existing announcement body", prepared.Body);
        Assert.Equal(prepared.Body, prepared.PersistedBody);
        Assert.Null(prepared.Cta);
        Assert.Null(prepared.Attachment);
    }

    [Fact]
    public void CtaAndLinkedAttachmentRoundTripThroughDraftAndDetailResponses()
    {
        var cta = new AnnouncementActionLink("申請フォームを開く", "/forms/application");
        var attachment = new AnnouncementActionLink("行事要項 PDF", "https://example.jp/files/guide.pdf");
        var request = new AnnouncementDraftContentRequest(
            new AnnouncementDraftTargetRequest(Guid.NewGuid(), null, null),
            "行事のお知らせ",
            "本文です。",
            AnnouncementPriority.Important,
            RequiresReadConfirmation: true,
            Cta: cta,
            Attachment: attachment);

        Assert.NotEqual("本文です。", request.Body);
        var decoded = AnnouncementContentContract.Decode(request.Body);
        Assert.True(decoded.IsEnvelope);
        Assert.Equal("本文です。", decoded.Body);
        Assert.Equal(cta, decoded.Cta);
        Assert.Equal(attachment, decoded.Attachment);

        var now = DateTimeOffset.UtcNow;
        var draftResponse = new AnnouncementDraftResponse(
            Guid.NewGuid(),
            1,
            AnnouncementDraftStatus.Draft,
            request.Target.WorkspaceId,
            null,
            null,
            "行事のお知らせ",
            request.Body,
            AnnouncementPriority.Important,
            false,
            true,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            now,
            null);
        Assert.Equal("本文です。", draftResponse.Body);
        Assert.Equal(cta, draftResponse.Cta);
        Assert.Equal(attachment, draftResponse.Attachment);

        var detailResponse = new AnnouncementDetailResponse(
            Guid.NewGuid(),
            request.Target.WorkspaceId,
            null,
            null,
            Guid.NewGuid(),
            "行事のお知らせ",
            request.Body,
            AnnouncementPriority.Important,
            false,
            true,
            false,
            now,
            null,
            now,
            null);
        Assert.Equal("本文です。", detailResponse.Body);
        Assert.Equal(cta, detailResponse.Cta);
        Assert.Equal(attachment, detailResponse.Attachment);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.jp/form")]
    [InlineData("//example.jp/form")]
    [InlineData("https://user:secret@example.jp/form")]
    [InlineData("/safe/../admin")]
    public void UnsafeUrlsAreRejected(string url)
    {
        Assert.Throws<JsonException>(() => AnnouncementContentContract.PrepareForPersistence(
            "本文",
            new AnnouncementActionLink("Open", url)));
    }

    [Theory]
    [InlineData("/forms/entry")]
    [InlineData("/files/guide.pdf?download=1")]
    [InlineData("https://example.jp/forms/entry")]
    public void SafeUrlsAreAccepted(string url)
    {
        var prepared = AnnouncementContentContract.PrepareForPersistence(
            "本文",
            new AnnouncementActionLink("Open", url));

        Assert.True(prepared.IsEnvelope);
        Assert.Equal(url, prepared.Cta!.Url);
    }

    [Fact]
    public void MalformedEnvelopeFailsClosedToPlainText()
    {
        const string malformed = "@coglatas-announcement-content:v1\n{not-json}";

        var decoded = AnnouncementContentContract.Decode(malformed);

        Assert.False(decoded.IsEnvelope);
        Assert.Equal(malformed, decoded.Body);
        Assert.Null(decoded.Cta);
        Assert.Null(decoded.Attachment);
    }
}
