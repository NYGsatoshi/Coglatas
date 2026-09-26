using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class Tenant : SoftDeletableEntity
{
    public Tenant()
    {
    }

    public Tenant(Guid id)
    {
        Id = id;
    }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PrimaryDomain { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public string? PlanId { get; set; }

    public ICollection<TenantUser> Users { get; } = new List<TenantUser>();
    public TenantSettings? Settings { get; set; }
    public ICollection<Subscription> Subscriptions { get; } = new List<Subscription>();
}

public sealed class TenantUser : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public TenantUserRole Role { get; set; } = TenantUserRole.Member;
    public TenantUserStatus Status { get; set; } = TenantUserStatus.Invited;
    public DateTimeOffset JoinedAt { get; set; }
    public Guid? InvitedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
    public User? InvitedByUser { get; set; }
}

public sealed class TenantSettings : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public Guid? LogoFileId { get; set; }
    public string? ThemeColor { get; set; }
    public string DefaultLocale { get; set; } = "en-US";
    public string TimeZone { get; set; } = "UTC";
    public InvitationMode InvitationMode { get; set; } = InvitationMode.AdminOnly;
    public long StorageQuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;
    public int UserLimit { get; set; } = 100;
    public int ProjectLimit { get; set; } = 100;
    public long FileUploadLimitBytes { get; set; } = 50L * 1024 * 1024;
    public string FeatureFlagsJson { get; set; } = "{}";
    public string NotificationSettingsJson { get; set; } = "{}";

    public Tenant? Tenant { get; set; }
    public FileObject? LogoFile { get; set; }
}

public sealed class Plan : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MaxUsers { get; set; }
    public long MaxStorageBytes { get; set; }
    public int MaxProjects { get; set; }
    public int? MaxExternalGuests { get; set; }
    public int? MaxApiRequestsPerDay { get; set; }
    public string EnabledFeaturesJson { get; set; } = "[]";
    public decimal? PriceMonthly { get; set; }
    public PlanStatus Status { get; set; } = PlanStatus.Active;

    public ICollection<Subscription> Subscriptions { get; } = new List<Subscription>();
}

public sealed class Subscription : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }

    public Tenant? Tenant { get; set; }
    public Plan? Plan { get; set; }
}

public sealed class UsageRecord : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public DateOnly Date { get; set; }
    public int ActiveUserCount { get; set; }
    public int TotalUserCount { get; set; }
    public int ProjectCount { get; set; }
    public int TaskCount { get; set; }
    public int FileCount { get; set; }
    public long StorageUsedBytes { get; set; }
    public int ApiRequestCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
}
