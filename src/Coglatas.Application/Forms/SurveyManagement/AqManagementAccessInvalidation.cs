using Coglatas.Domain.Entities;

namespace Coglatas.Application.Forms.SurveyManagement;

public sealed partial class AqSurveyManagementService
{
    private async Task<IReadOnlyList<Guid>> EventPresentationIdsAsync(Guid eventId, CancellationToken token)
    {
        const int pageSize = 100;
        var ids = new List<Guid>();
        Guid? after = null;
        while (true)
        {
            var page = await store.PageAsync<Presentation>(x => x.EventId == eventId, after, pageSize, token);
            ids.AddRange(page.Select(x => x.Id));
            if (page.Count < pageSize)
                return ids;

            after = page[^1].Id;
        }
    }

    private async Task<Dictionary<Guid, Guid>> CaptureFeedbackAccessAsync(
        IReadOnlyList<Guid> presentationIds, CancellationToken token)
    {
        var recipients = new Dictionary<Guid, Guid>();
        await AddFeedbackRecipientsAsync(recipients, presentationIds, token);
        return recipients;
    }

    private async Task PublishFeedbackAccessChangesAsync(
        IReadOnlyList<Guid> presentationIds, Dictionary<Guid, Guid> recipients, CancellationToken token)
    {
        // The authoritative writes and notifications are still in the same
        // transaction. Capture users with access on either side of the change.
        if (presentationIds.Count == 0)
            return;

        await store.FlushAsync(token);
        await AddFeedbackRecipientsAsync(recipients, presentationIds, token);
        foreach (var (userId, presentationId) in recipients)
            await feedbackInvalidation.InvalidateAccessAsync(currentTenant.TenantId, presentationId, userId, token);
    }

    private async Task AddFeedbackRecipientsAsync(
        Dictionary<Guid, Guid> recipients, IReadOnlyList<Guid> presentationIds, CancellationToken token)
    {
        foreach (var presentationId in presentationIds)
        {
            var visibleTo = await feedbackInvalidation.ReadCurrentRecipientsAsync(currentTenant.TenantId, presentationId, token);
            foreach (var userId in visibleTo)
                recipients.TryAdd(userId, presentationId);
        }
    }
}
