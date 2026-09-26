using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class StudentRecordConfiguration : IEntityTypeConfiguration<StudentRecord>
{
    public void Configure(EntityTypeBuilder<StudentRecord> builder)
    {
        builder.ToTable("student_records");
        builder.ConfigureAuditableEntity();

        builder.Property(record => record.PublicDisplayName).HasMaxLength(240);
        builder.Property(record => record.HomeroomLabel).HasMaxLength(120);
        builder.Property(record => record.HealthNotes).HasMaxLength(4000);
        builder.Property(record => record.GuardianContact).HasMaxLength(1000);
        builder.Property(record => record.Grades).HasMaxLength(2000);
        builder.Property(record => record.AttendanceStatus).HasConversion<string>().HasMaxLength(40);
        builder.Property(record => record.InternalSensitiveNotes).HasMaxLength(4000);

        builder.HasIndex(record => record.WorkspaceId);

        builder
            .HasOne(record => record.Workspace)
            .WithMany()
            .HasForeignKey(record => record.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
