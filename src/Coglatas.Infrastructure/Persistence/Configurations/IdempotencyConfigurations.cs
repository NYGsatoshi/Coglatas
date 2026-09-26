using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.ConfigureAuditableEntity();

        builder.Property(record => record.Operation).HasMaxLength(100).IsRequired();
        builder.Property(record => record.KeyHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(record => record.ResourceType).HasMaxLength(80).IsRequired();

        builder.HasIndex(record => new
            {
                record.TenantId,
                record.ActorUserId,
                record.Operation,
                record.KeyHash
            })
            .IsUnique()
            .HasDatabaseName("UX_idempotency_tenant_actor_operation_key");
        builder.HasIndex(record => new { record.TenantId, record.ResourceType, record.ResourceId });
        builder.HasIndex(record => record.CreatedAt);

        builder
            .HasOne(record => record.ActorUser)
            .WithMany()
            .HasForeignKey(record => record.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
