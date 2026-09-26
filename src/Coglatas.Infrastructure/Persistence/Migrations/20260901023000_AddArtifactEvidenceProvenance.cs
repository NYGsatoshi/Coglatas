using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260901023000_AddArtifactEvidenceProvenance")]
public sealed class AddArtifactEvidenceProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SourcePublisherSnapshot",
            table: "artifact_evidence",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SourceTypeSnapshot",
            table: "artifact_evidence",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SourceClassification",
            table: "artifact_evidence",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PublishedAtSnapshot",
            table: "artifact_evidence",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RetrievedAtSnapshot",
            table: "artifact_evidence",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContentHashSnapshot",
            table: "artifact_evidence",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SourceVersionSnapshot",
            table: "artifact_evidence",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VerificationStatus",
            table: "artifact_evidence",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "Unverified");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SourcePublisherSnapshot", table: "artifact_evidence");
        migrationBuilder.DropColumn(name: "SourceTypeSnapshot", table: "artifact_evidence");
        migrationBuilder.DropColumn(name: "SourceClassification", table: "artifact_evidence");
        migrationBuilder.DropColumn(name: "PublishedAtSnapshot", table: "artifact_evidence");
        migrationBuilder.DropColumn(name: "RetrievedAtSnapshot", table: "artifact_evidence");
        migrationBuilder.DropColumn(name: "ContentHashSnapshot", table: "artifact_evidence");
        migrationBuilder.DropColumn(name: "SourceVersionSnapshot", table: "artifact_evidence");
        migrationBuilder.DropColumn(name: "VerificationStatus", table: "artifact_evidence");
    }
}
