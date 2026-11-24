using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Forms.SurveyManagement;

public sealed partial class AqSurveyManagementService
{
    private async Task<Result<AqTargetSummary>> RepinCommonVersionAsync(
        EventScope scope, SurveyTarget target, EventFeedbackSettings settings, Guid versionId,
        AqUpdateTargetRequest request, CancellationToken token)
    {
        if (versionId == Guid.Empty || request.Enabled is not null || request.ResultsStopped is not null ||
            request.StartAt.ValueKind != JsonValueKind.Undefined || request.EndAt.ValueKind != JsonValueKind.Undefined)
        {
            return Invalid<AqTargetSummary>("Select the common version separately from target reception and result settings.");
        }

        var current = await CurrentPublicationAsync(target, token);
        if (current is null)
            return Conflict<AqTargetSummary>("The target has no current published common version.");
        if (current.FormVersionId == versionId)
            return Success(ToTarget(target, current, settings));

        var next = await store.FindCommonVersionAsync(target.FormDefinitionId, versionId, token);
        if (next is null)
            return NotFound<AqTargetSummary>();
        var previous = await store.FindCommonVersionAsync(target.FormDefinitionId, current.FormVersionId, token);
        var additional = await store.FindAdditionalVersionAsync(target.Id, current.TargetQuestionSetVersionId, token);
        if (previous is null || additional is null || !IsForwardCompatible(previous, next))
            return Conflict<AqTargetSummary>("The replacement must be a later, explicitly compatible publication of the same common version lineage.");

        var composition = PublishedSurveySchema.Compose(target, next, additional, NewOpaqueToken(), clock.UtcNow);
        store.Add(composition);
        await store.FlushAsync(token);
        target.Publish(request.ExpectedVersionNo, composition);
        await AuditAsync(scope.Actor, "aq.target.common-version.publish", target.Id, token);
        return Success(ToTarget(target, composition, settings));
    }

    private static bool IsForwardCompatible(FormVersion prior, FormVersion next)
    {
        if (next.Number <= prior.Number || next.Fields.Count != prior.Fields.Count)
            return false;

        var previous = prior.Fields.ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        return next.Fields.All(field => previous.TryGetValue(field.FieldKey, out var preceding) &&
            field.CompatiblePreviousFieldId is not null &&
            field.CompatibilityRootFieldId == (preceding.CompatibilityRootFieldId ?? preceding.Id) &&
            preceding.IsCompatibleCorrectionOf(field.ToSurveyField()));
    }
}
