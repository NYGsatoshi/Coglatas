using System.Reflection;
using Coglatas.Application.Realtime;
using Coglatas.Web.Realtime;
using Microsoft.AspNetCore.Authorization;

namespace Coglatas.Web.Testing;

// Source observations only. None of these entries supplies an approved SPEC identity.
internal static class SecurityArchitectureRealtimeInventory
{
    public static object Observe() => new
    {
        hub = typeof(AppHub).FullName,
        authorization = typeof(AppHub).GetCustomAttributes<AuthorizeAttribute>()
            .Select(item => new { item.Policy, item.Roles, item.AuthenticationSchemes }).ToArray(),
        methods = typeof(AppHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.Name is not ("OnConnectedAsync" or "OnDisconnectedAsync"))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .Select(method => new
            {
                name = method.Name, parameters = method.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray(),
                observedBoundary = method.Name.StartsWith("Subscribe", StringComparison.Ordinal)
                    ? "CURRENT_SESSION_TENANT_FEATURE_AND_RESOURCE_AUTHORITY"
                    : method.Name.StartsWith("Unsubscribe", StringComparison.Ordinal)
                        ? "REMOVE_ONLY_THIS_CONNECTION_SUBSCRIPTION" : "UNKNOWN_REQUIRES_REVIEW",
                specIds = Array.Empty<string>(), contractOutcome = "UNVERIFIED"
            }).ToArray(),
        subscriptionTypes = Enum.GetNames<RealtimeSubscriptionType>(),
        serverEvents = new[] { "DurableEvent" },
        events = RealtimeEventCatalog.EventTypes.Order(StringComparer.Ordinal).Select(eventType => new
        {
            eventType, payloadSchemaVersion = RealtimeEventCatalog.PayloadSchemaVersion1,
            observedDeliveryBoundary = DeliveryBoundary(eventType),
            specIds = Array.Empty<string>(), contractOutcome = "UNVERIFIED", runtimeOutcome = "UNVERIFIED"
        }).ToArray(),
        catchUp = new
        {
            transport = "AUTHORITATIVE_HTTP_REFETCH_AFTER_RECONNECT",
            hubCatchUpMethod = (string?)null,
            outcome = "UNVERIFIED"
        },
        approval = "DRAFT", completion = "UNVERIFIED"
    };

    private static string DeliveryBoundary(string eventType) => eventType switch
    {
        "Security.AuthorizationStateChanged.v1" => "TENANT_MATCH_AND_CURRENT_RECIPIENT_INVALIDATION_TARGET_NO_PROTECTED_PAYLOAD",
        "Notifications.NotificationCreated.v1" => "CURRENT_SESSION_USER_ROUTE_AND_CURRENT_NOTIFICATION_TARGET",
        "Notifications.NotificationReadStateChanged.v1" => "CURRENT_SESSION_USER_ROUTE_AND_CURRENT_READ_STATE_TARGET",
        "Messaging.ConversationUnreadChanged.v1" => "CURRENT_SESSION_USER_ROUTE_AND_CURRENT_PAYLOAD_CONVERSATION_READ",
        "Projects.TaskChanged.v1" or "Projects.TaskAssignmentChanged.v1" or "Projects.TaskWorkflowChanged.v1" or
            "Projects.TaskCommentChanged.v1" => "CURRENT_SESSION_AND_CURRENT_TASK_TARGET_RESOURCE",
        "Projects.ProjectChanged.v1" => "CURRENT_SESSION_AND_CURRENT_PROJECT_TARGET_RESOURCE",
        "Files.FileChanged.v1" => "CURRENT_SESSION_ROUTE_AUTHORITY_AND_CURRENT_FILE_ATTACHMENT_READ",
        "Messaging.MessageCreated.v1" or "Messaging.MessageUpdated.v1" or "Messaging.MessageDeleted.v1" or
            "Messaging.ThreadChanged.v1" or "Announcements.AnnouncementChanged.v1" => "CURRENT_SESSION_AND_CURRENT_ROUTED_RESOURCE_AUTHORITY",
        _ => "UNKNOWN_REQUIRES_REVIEW"
    };
}
