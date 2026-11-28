using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;

namespace Coglatas.Application.SurveyFeedback;

public sealed class SurveyFeedbackInvalidation(
    IFeedbackLifecycleRepository repository,
    SurveyFeedbackAuthorization authorization,
    ITransactionalOutbox outbox,
    IAuthorizationStateChangePublisher authorizationChanges,
    IClock clock) : ISurveyFeedbackInvalidation
{
    public const string EventName = "Feedback.PresentationInvalidated.v1";

    public async Task<IReadOnlyList<Guid>> ReadCurrentRecipientsAsync(Guid tenantId, Guid presentationId,
        CancellationToken cancellationToken)
    {
        var recipients = new List<Guid>();
        foreach (var recipient in await repository.ReadInvalidationCandidatesAsync(tenantId, presentationId, cancellationToken))
        {
            var actor = new FeedbackActor(tenantId, recipient);
            if (await authorization.CanReceiveInvalidationAsync(actor, presentationId, cancellationToken))
            {
                recipients.Add(recipient);
            }
        }

        return recipients;
    }

    public async Task InvalidateAsync(Guid tenantId, Guid presentationId, CancellationToken cancellationToken)
    {
        var recipients = await ReadCurrentRecipientsAsync(tenantId, presentationId, cancellationToken);
        var routes = recipients.Select(recipient => new RealtimeRoutingTarget(RealtimeSubscriptionType.User, recipient));
        foreach (var batch in routes.Chunk(50))
        {
            var result = await outbox.EnqueueAsync(new DurableEventEnvelope(Guid.NewGuid(), EventName,
                RealtimeEventCatalog.PayloadSchemaVersion1, clock.UtcNow, tenantId, "Presentation", presentationId,
                null, RealtimeActor.System(), null, null,
                JsonSerializer.SerializeToElement(new Dictionary<string, string>())), batch, cancellationToken);
            if (!result.IsSuccess)
            {
                throw new RequiredOutboxStagingException();
            }
        }
    }

    public Task InvalidateAccessAsync(Guid tenantId, Guid recipientId,
        CancellationToken cancellationToken) =>
        authorizationChanges.PublishAsync(tenantId, recipientId, "SurveyFeedback", null,
            "FeedbackAccessChanged", cancellationToken);
}
