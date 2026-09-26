using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("export_jobs");
        builder.ConfigureAuditableEntity();

        builder.Property(job => job.Status).HasEnumStringConversion().IsRequired();
        builder.Property(job => job.ExportType).HasEnumStringConversion().IsRequired();
        builder.Property(job => job.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(job => job.RequestedByUserId);
        builder.HasIndex(job => job.Status);
        builder.HasIndex(job => job.CreatedAt);
        builder.HasIndex(job => job.FileObjectId);

        builder
            .HasOne(job => job.RequestedByUser)
            .WithMany()
            .HasForeignKey(job => job.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(job => job.FileObject)
            .WithMany()
            .HasForeignKey(job => job.FileObjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class ExportPackageGrantConfiguration : IEntityTypeConfiguration<ExportPackageGrant>
{
    public void Configure(EntityTypeBuilder<ExportPackageGrant> builder)
    {
        builder.ToTable("export_package_grants");
        builder.ConfigureAuditableEntity();

        builder.Property(grant => grant.Classification).HasEnumStringConversion().IsRequired();
        builder.Property(grant => grant.ExportType).HasMaxLength(80).IsRequired();
        builder.Property(grant => grant.IncludedClassifications).HasMaxLength(500).IsRequired();
        builder.Property(grant => grant.RequestedScopeType).HasMaxLength(80).IsRequired();
        builder.Property(grant => grant.RequestedFields).HasMaxLength(1000).IsRequired();
        builder.Property(grant => grant.AuthorizedFields).HasMaxLength(1000).IsRequired();
        builder.Property(grant => grant.PolicyStamp).HasMaxLength(128).IsRequired();
        builder.Property(grant => grant.BuildAuthorizationState).HasMaxLength(80).IsRequired();
        builder.Property(grant => grant.DownloadAuthorizationState).HasMaxLength(80).IsRequired();

        builder.HasIndex(grant => grant.RequestedByUserId);
        builder.HasIndex(grant => grant.StudentRecordId);
        builder.HasIndex(grant => grant.WorkspaceId);
        builder.HasIndex(grant => new { grant.RequestedScopeType, grant.RequestedScopeId });
        builder.HasIndex(grant => grant.ExpiresAt);
        builder.HasIndex(grant => grant.RevokedAt);
    }
}

public sealed class IntegrationAccountConfiguration : IEntityTypeConfiguration<IntegrationAccount>
{
    public void Configure(EntityTypeBuilder<IntegrationAccount> builder)
    {
        builder.ToTable("integration_accounts");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(account => account.Provider).HasEnumStringConversion().IsRequired();
        builder.Property(account => account.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(account => account.Status).HasEnumStringConversion().IsRequired();
        builder.Property(account => account.SettingsJson).HasMaxLength(12000).IsRequired();

        builder.HasIndex(account => account.Provider);
        builder.HasIndex(account => account.Status);
        builder.HasIndex(account => account.CreatedByUserId);
        builder.HasIndex(account => new { account.TenantId, account.Provider, account.DisplayName });

        builder
            .HasOne(account => account.CreatedByUser)
            .WithMany()
            .HasForeignKey(account => account.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(webhook => webhook.Name).HasMaxLength(160).IsRequired();
        builder.Property(webhook => webhook.Url).HasMaxLength(2000).IsRequired();
        builder.Property(webhook => webhook.SecretHash).HasMaxLength(128);
        builder.Property(webhook => webhook.EnabledEventsJson).HasMaxLength(12000).IsRequired();
        builder.Property(webhook => webhook.Status).HasEnumStringConversion().IsRequired();

        builder.HasIndex(webhook => webhook.Status);
        builder.HasIndex(webhook => webhook.CreatedByUserId);
        builder.HasIndex(webhook => new { webhook.TenantId, webhook.Name });

        builder
            .HasOne(webhook => webhook.CreatedByUser)
            .WithMany()
            .HasForeignKey(webhook => webhook.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("api_tokens");
        builder.ConfigureAuditableEntity();

        builder.Property(token => token.Name).HasMaxLength(160).IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(token => token.ScopesJson).HasMaxLength(4000).IsRequired();

        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.CreatedByUserId);
        builder.HasIndex(token => token.ExpiresAt);
        builder.HasIndex(token => token.RevokedAt);
        builder.HasIndex(token => new { token.TenantId, token.Name });

        builder
            .HasOne(token => token.CreatedByUser)
            .WithMany()
            .HasForeignKey(token => token.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
