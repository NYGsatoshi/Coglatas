using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(tenant => tenant.Name).HasMaxLength(160).IsRequired();
        builder.Property(tenant => tenant.Slug).HasMaxLength(120).IsRequired();
        builder.Property(tenant => tenant.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(tenant => tenant.PrimaryDomain).HasMaxLength(260);
        builder.Property(tenant => tenant.Status).HasEnumStringConversion().IsRequired();
        builder.Property(tenant => tenant.PlanId).HasMaxLength(120);

        builder.HasIndex(tenant => tenant.Slug).IsUnique();
        builder.HasIndex(tenant => tenant.PrimaryDomain).IsUnique();
        builder.HasIndex(tenant => tenant.Status);
    }
}

public sealed class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> builder)
    {
        builder.ToTable("tenant_users");
        builder.ConfigureAuditableEntity();

        builder.Property(user => user.Role).HasEnumStringConversion().IsRequired();
        builder.Property(user => user.Status).HasEnumStringConversion().IsRequired();
        builder.Property(user => user.JoinedAt).IsRequired();

        builder.HasIndex(user => user.UserId);
        builder.HasIndex(user => user.Status);
        builder.HasIndex(user => new { user.TenantId, user.UserId, user.Status });

        builder
            .HasIndex(user => new { user.TenantId, user.UserId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");

        builder
            .HasOne(user => user.Tenant)
            .WithMany(tenant => tenant.Users)
            .HasForeignKey(user => user.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(user => user.User)
            .WithMany()
            .HasForeignKey(user => user.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(user => user.InvitedByUser)
            .WithMany()
            .HasForeignKey(user => user.InvitedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("tenant_settings");
        builder.ConfigureAuditableEntity();

        builder.Property(settings => settings.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(settings => settings.ThemeColor).HasMaxLength(40);
        builder.Property(settings => settings.DefaultLocale).HasMaxLength(20).IsRequired();
        builder.Property(settings => settings.TimeZone).HasMaxLength(80).IsRequired();
        builder.Property(settings => settings.InvitationMode).HasEnumStringConversion().IsRequired();
        builder.Property(settings => settings.FeatureFlagsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(settings => settings.NotificationSettingsJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(settings => settings.TenantId).IsUnique();
        builder.HasIndex(settings => settings.LogoFileId);

        builder
            .HasOne(settings => settings.Tenant)
            .WithOne(tenant => tenant.Settings)
            .HasForeignKey<TenantSettings>(settings => settings.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(settings => settings.LogoFile)
            .WithMany()
            .HasForeignKey(settings => settings.LogoFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.ConfigureAuditableEntity();

        builder.Property(plan => plan.Name).HasMaxLength(120).IsRequired();
        builder.Property(plan => plan.Description).HasMaxLength(1000);
        builder.Property(plan => plan.EnabledFeaturesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(plan => plan.PriceMonthly).HasPrecision(12, 2);
        builder.Property(plan => plan.Status).HasEnumStringConversion().IsRequired();

        builder.HasIndex(plan => plan.Name).IsUnique();
        builder.HasIndex(plan => plan.Status);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.ConfigureAuditableEntity();

        builder.Property(subscription => subscription.Status).HasEnumStringConversion().IsRequired();
        builder.Property(subscription => subscription.StartedAt).IsRequired();

        builder.HasIndex(subscription => subscription.TenantId);
        builder.HasIndex(subscription => subscription.PlanId);
        builder.HasIndex(subscription => new { subscription.TenantId, subscription.Status });

        builder
            .HasOne(subscription => subscription.Tenant)
            .WithMany(tenant => tenant.Subscriptions)
            .HasForeignKey(subscription => subscription.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(subscription => subscription.Plan)
            .WithMany(plan => plan.Subscriptions)
            .HasForeignKey(subscription => subscription.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records");
        builder.ConfigureEntity();

        builder.Property(record => record.Date).IsRequired();
        builder.Property(record => record.CreatedAt).IsRequired();

        builder.HasIndex(record => new { record.TenantId, record.Date }).IsUnique();

        builder
            .HasOne(record => record.Tenant)
            .WithMany()
            .HasForeignKey(record => record.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
