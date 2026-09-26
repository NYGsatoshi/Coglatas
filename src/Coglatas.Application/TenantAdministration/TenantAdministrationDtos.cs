using Coglatas.Domain.Enums;

namespace Coglatas.Application.TenantAdministration;

public sealed record PlatformOverviewResponse(
    int TenantCount,
    int ActiveTenantCount,
    int SuspendedTenantCount,
    int TotalUserCount,
    long TotalStorageUsedBytes,
    int TotalProjectCount,
    IReadOnlyList<TenantUsageResponse> TenantUsage);

public sealed record TenantOverviewResponse(
    Guid TenantId,
    int ActiveUserCount,
    long StorageUsedBytes,
    int ProjectCount,
    int TaskCount,
    int FileCount,
    IReadOnlyList<string> EnabledFeatures,
    SubscriptionResponse? Subscription);

public sealed record TenantSettingsResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    Guid? LogoFileId,
    string? ThemeColor,
    string DefaultLocale,
    string TimeZone,
    InvitationMode InvitationMode,
    long StorageQuotaBytes,
    int UserLimit,
    int ProjectLimit,
    long FileUploadLimitBytes,
    string FeatureFlagsJson,
    string NotificationSettingsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateTenantSettingsRequest(
    string? DisplayName,
    Guid? LogoFileId,
    string? ThemeColor,
    string? DefaultLocale,
    string? TimeZone,
    InvitationMode? InvitationMode,
    long? StorageQuotaBytes,
    int? UserLimit,
    int? ProjectLimit,
    long? FileUploadLimitBytes,
    string? FeatureFlagsJson,
    string? NotificationSettingsJson);

public sealed record TenantFeatureResponse(string Key, bool IsEnabled);

public sealed record TenantFeaturesResponse(Guid TenantId, IReadOnlyList<string> EnabledFeatures);

public sealed record TenantUsageResponse(
    Guid TenantId,
    int ActiveUserCount,
    int TotalUserCount,
    int ProjectCount,
    int TaskCount,
    int FileCount,
    long StorageUsedBytes,
    int ApiRequestCount);

public sealed record PlatformUsageResponse(
    IReadOnlyList<TenantUsageResponse> Tenants,
    int TotalActiveUserCount,
    int TotalUserCount,
    int TotalProjectCount,
    int TotalTaskCount,
    int TotalFileCount,
    long TotalStorageUsedBytes,
    int TotalApiRequestCount);

public sealed record InviteTenantUserRequest(
    Guid UserId,
    TenantUserRole Role);

public sealed record PlanResponse(
    Guid Id,
    string Name,
    string? Description,
    int MaxUsers,
    long MaxStorageBytes,
    int MaxProjects,
    int? MaxExternalGuests,
    int? MaxApiRequestsPerDay,
    string EnabledFeaturesJson,
    decimal? PriceMonthly,
    PlanStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertPlanRequest(
    string? Name,
    string? Description,
    int? MaxUsers,
    long? MaxStorageBytes,
    int? MaxProjects,
    int? MaxExternalGuests,
    int? MaxApiRequestsPerDay,
    string? EnabledFeaturesJson,
    decimal? PriceMonthly,
    PlanStatus? Status);

public sealed record SubscriptionResponse(
    Guid Id,
    Guid TenantId,
    Guid PlanId,
    string? PlanName,
    SubscriptionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateSubscriptionRequest(
    Guid PlanId,
    SubscriptionStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset? TrialEndsAt);
