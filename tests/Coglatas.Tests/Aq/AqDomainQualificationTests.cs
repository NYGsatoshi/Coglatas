using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task PublishedTargetsRetainSharedVersionAndSeparateOrderedFieldSnapshots() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var choice = new SurveyField("track", "Track", SurveyFieldType.SingleChoice, false,
                [new("green", "Green"), new("blue", "Blue")]);
            var fixture = await SeedAsync(database, host, project, activity, additionalQuestion: choice);
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var original = await db.Set<PublishedSurveySchema>().SingleAsync(x => x.Id == fixture.SchemaId);
            var common = await db.Set<FormVersion>().Include(x => x.Fields)
                .SingleAsync(x => x.Id == original.FormVersionId);
            var firstTarget = await db.Set<SurveyTarget>().SingleAsync(x => x.Id == fixture.TargetId);
            var campaign = await db.Set<SurveyCampaign>().SingleAsync(x => x.Id == firstTarget.CampaignId);
            var secondPresentation = Presentation.Create(activity, "Synthetic second presentation", null,
                activity.StartsAt, activity.EndsAt);
            var secondTarget = SurveyTarget.Create(campaign, secondPresentation, Guid.NewGuid().ToString("N"));
            var now = DateTimeOffset.UtcNow;
            var empty = TargetQuestionSetVersion.Publish(secondTarget, 1, [], now);
            var secondAdditional = TargetQuestionSetVersion.Publish(secondTarget, 2,
                [new("format", "Format", SurveyFieldType.Boolean, false, [])], now);
            db.AddRange(secondPresentation, secondTarget, empty, secondAdditional);
            await db.SaveChangesAsync();
            var secondEmpty = PublishedSurveySchema.Compose(secondTarget, common, empty, Guid.NewGuid().ToString("N"), now);
            var secondFull = PublishedSurveySchema.Compose(secondTarget, common, secondAdditional, Guid.NewGuid().ToString("N"), now);
            db.AddRange(secondEmpty, secondFull);
            await db.SaveChangesAsync();
            secondTarget.Publish(secondTarget.VersionNo, secondEmpty);
            await db.SaveChangesAsync();
            var firstPublication = secondTarget.VersionNo;
            secondTarget.Publish(firstPublication, secondFull);
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            var currentTarget = await db.Set<SurveyTarget>().AsNoTracking().SingleAsync(x => x.Id == secondTarget.Id);
            Assert.Equal(secondFull.Id, currentTarget.PublishedSchemaId);
            Assert.Equal(firstPublication + 1, currentTarget.VersionNo);
            var schemaIds = new[] { original.Id, secondEmpty.Id, secondFull.Id };
            var saved = await db.Set<PublishedSurveySchema>().AsNoTracking().Include(x => x.Fields)
                .Where(x => schemaIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var firstFields = await AssertStoredCompositionAsync(db, saved[original.Id], firstTarget.Id, common.Id,
                original.TargetQuestionSetVersionId, [FieldKey, "track"]);
            await AssertStoredCompositionAsync(db, saved[secondEmpty.Id], secondTarget.Id, common.Id, empty.Id, [FieldKey]);
            await AssertStoredCompositionAsync(db, saved[secondFull.Id], secondTarget.Id, common.Id, secondAdditional.Id,
                [FieldKey, "format"]);
            Assert.Equal(3, saved.Values.Select(x => x.SchemaDigest).Distinct().Count());
            Assert.Equal(fixture.Digest, saved[original.Id].SchemaDigest);
            Assert.Equal(choice.Options, firstFields[1].Options);

            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using var response = await GetDataAsync(guest, SurveyRoute + fixture.TargetCode);
            var data = response.RootElement.GetProperty("data");
            Assert.Equal(saved[original.Id].SchemaDigest, data.GetProperty("schemaDigest").GetString());
            Assert.Equal<string?>([FieldKey, "track"],
                data.GetProperty("fields").EnumerateArray().Select(x => x.GetProperty("fieldKey").GetString()));
        });

    [PostgreSqlFact]
    public Task LegacyFormCommandsCannotReadOrMutateAnAudienceSurveyOrItsSubmission() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var fixture = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using (var accepted = await SubmitAsync(guest, fixture.TargetCode, "synthetic-ordinary-boundary", Payload(fixture)))
                await ReadMinimalReceiptAsync(accepted);
            await host.LoginAsync();

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var before = await db.Set<FormSubmission>().AsNoTracking().SingleAsync(x => x.SurveyTargetId == fixture.TargetId);
            var legacyRoute = $"/api/forms/{before.FormDefinitionId:D}";
            using (var read = await host.Client.GetAsync(legacyRoute + "/responses/me"))
                await AssertMissingFromOrdinaryFormsAsync(read);
            using (var submit = await host.PostAsync(legacyRoute + "/responses",
                       new SubmitFormResponseRequest([new SubmitFormAnswerRequest(Guid.NewGuid(), "Synthetic ordinary answer")])))
                await AssertMissingFromOrdinaryFormsAsync(submit);
            using (var edit = new HttpRequestMessage(HttpMethod.Patch, legacyRoute))
            {
                edit.Content = JsonContent.Create(new UpdateFormRequest(Title: "Synthetic attempted edit"));
                using var result = await SendAsync(host.Client, edit, includeCsrf: true);
                await AssertMissingFromOrdinaryFormsAsync(result);
            }
            using var close = await host.PostAsync(legacyRoute + "/close", new { });
            await AssertMissingFromOrdinaryFormsAsync(close);

            var after = await db.Set<FormSubmission>().AsNoTracking().SingleAsync(x => x.SurveyTargetId == fixture.TargetId);
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.VersionNo, after.VersionNo);
            Assert.Equal(FormSubmissionStatus.Submitted, after.Status);
            Assert.Equal("Synthetic audience feedback", await db.Set<FormDefinition>().Where(x => x.Id == after.FormDefinitionId)
                .Select(x => x.Title).SingleAsync());
            using var published = await GetDataAsync(guest, SurveyRoute + fixture.TargetCode);
            Assert.Equal(fixture.Digest, published.RootElement.GetProperty("data").GetProperty("schemaDigest").GetString());
        });

    private static async Task AssertMissingFromOrdinaryFormsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Form not found.", body.RootElement.GetProperty("error").GetString());
    }

    private static async Task<IReadOnlyList<SurveyField>> AssertStoredCompositionAsync(
        AppDbContext db, PublishedSurveySchema schema, Guid targetId, Guid commonId, Guid additionalId, string[] expectedKeys)
    {
        Assert.Equal(targetId, schema.SurveyTargetId);
        Assert.Equal(commonId, schema.FormVersionId);
        Assert.Equal(additionalId, schema.TargetQuestionSetVersionId);
        var common = await db.Set<FormVersion>().AsNoTracking().Include(x => x.Fields).SingleAsync(x => x.Id == commonId);
        var additional = await db.Set<TargetQuestionSetVersion>().AsNoTracking().Include(x => x.Fields)
            .SingleAsync(x => x.Id == additionalId);
        var fields = common.Fields.OrderBy(x => x.SortOrder).Concat(additional.Fields.OrderBy(x => x.SortOrder)).ToArray();
        var schemaFields = schema.Fields.OrderBy(x => x.SortOrder).ToArray();
        Assert.Equal(expectedKeys, fields.Select(x => x.FieldKey));
        Assert.Equal(fields.Select(x => x.Id), schemaFields.Select(x => x.FormFieldDefinitionId));
        Assert.Equal(Enumerable.Range(0, expectedKeys.Length), schemaFields.Select(x => x.SortOrder));
        Assert.Equal(targetId, additional.SurveyTargetId);
        Assert.Equal(schema.TenantId, common.TenantId);
        Assert.Equal(schema.TenantId, additional.TenantId);
        Assert.Equal(common.FormDefinitionId, schema.FormDefinitionId);
        Assert.Equal(ExpectedSchemaDigest(common.SnapshotJson, additional.SnapshotJson), schema.SchemaDigest);
        return PublishedSurveySchema.ReadFields(common.SnapshotJson, additional.SnapshotJson);
    }

    private static string ExpectedSchemaDigest(string common, string additional)
    {
        using var input = new MemoryStream();
        input.Write("AQ-SCHEMA-v1"u8);
        Span<byte> length = stackalloc byte[8];
        foreach (var snapshot in new[] { common, additional })
        {
            var bytes = Encoding.UTF8.GetBytes(snapshot);
            BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.Length);
            input.Write(length);
            input.Write(bytes);
        }
        return Convert.ToHexStringLower(SHA256.HashData(input.ToArray()));
    }
}
