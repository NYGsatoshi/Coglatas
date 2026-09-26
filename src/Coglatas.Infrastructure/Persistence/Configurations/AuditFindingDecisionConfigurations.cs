using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class AuditFindingDecisionConfiguration : IEntityTypeConfiguration<AuditFindingDecision>
{
    public void Configure(EntityTypeBuilder<AuditFindingDecision> builder)
    {
        builder.ToTable("audit_finding_decisions", table => table.ExcludeFromMigrations());
        builder.ConfigureAuditableEntity();
        builder.Property(decision => decision.Decision)
            .HasEnumStringConversion()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(decision => decision.PreviousDecision)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(decision => decision.Rationale).HasMaxLength(1000);
        builder.Property(decision => decision.ReviewerDisplayName).HasMaxLength(256).IsRequired();
        builder.HasIndex(decision => new { decision.TenantId, decision.ArtifactFindingId, decision.CreatedAt });
        builder.HasOne(decision => decision.Finding)
            .WithMany()
            .HasForeignKey(decision => decision.ArtifactFindingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
