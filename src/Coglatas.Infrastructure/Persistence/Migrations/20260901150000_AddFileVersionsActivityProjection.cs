using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the immutable File-version ledger used by the Files Activity pane.
/// It is deliberately not part of the mutable EF aggregate model: writes
/// are append-only and future restore operations create another version.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901150000_AddFileVersionsActivityProjection")]
public sealed class AddFileVersionsActivityProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE file_versions (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "FileObjectId" uuid NOT NULL,
                "VersionNumber" integer NOT NULL,
                "OriginalFileName" character varying(260) NOT NULL,
                "StorageKey" character varying(1024) NOT NULL,
                "ContentType" character varying(160) NOT NULL,
                "SizeBytes" bigint NOT NULL,
                "HashSha256" character varying(64) NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_file_versions" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_file_versions_file_objects_FileObjectId"
                    FOREIGN KEY ("FileObjectId") REFERENCES file_objects ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_file_versions_users_CreatedByUserId"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX "IX_file_versions_TenantId_FileObjectId_VersionNumber"
                ON file_versions ("TenantId", "FileObjectId", "VersionNumber");
            CREATE INDEX "IX_file_versions_TenantId_FileObjectId_CreatedAt"
                ON file_versions ("TenantId", "FileObjectId", "CreatedAt" DESC);
            CREATE INDEX "IX_file_versions_CreatedByUserId"
                ON file_versions ("CreatedByUserId");

            INSERT INTO file_versions (
                "Id", "TenantId", "FileObjectId", "VersionNumber",
                "OriginalFileName", "StorageKey", "ContentType", "SizeBytes",
                "HashSha256", "CreatedByUserId", "CreatedAt")
            SELECT
                f."Id", f."TenantId", f."Id", 1,
                f."OriginalFileName", f."StorageKey", f."ContentType", f."SizeBytes",
                f."HashSha256", f."UploadedByUserId", f."CreatedAt"
            FROM file_objects AS f
            ON CONFLICT ("Id") DO NOTHING;

            CREATE OR REPLACE FUNCTION coglatas_file_versions_append_only_guard()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'file_versions is append-only; create a new version instead';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_file_versions_append_only
            BEFORE UPDATE OR DELETE ON file_versions
            FOR EACH ROW EXECUTE FUNCTION coglatas_file_versions_append_only_guard();

            CREATE OR REPLACE FUNCTION coglatas_capture_initial_file_version()
            RETURNS trigger AS $$
            BEGIN
                INSERT INTO file_versions (
                    "Id", "TenantId", "FileObjectId", "VersionNumber",
                    "OriginalFileName", "StorageKey", "ContentType", "SizeBytes",
                    "HashSha256", "CreatedByUserId", "CreatedAt")
                VALUES (
                    NEW."Id", NEW."TenantId", NEW."Id", 1,
                    NEW."OriginalFileName", NEW."StorageKey", NEW."ContentType", NEW."SizeBytes",
                    NEW."HashSha256", NEW."UploadedByUserId", NEW."CreatedAt")
                ON CONFLICT ("Id") DO NOTHING;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_file_objects_capture_initial_version
            AFTER INSERT ON file_objects
            FOR EACH ROW EXECUTE FUNCTION coglatas_capture_initial_file_version();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_file_objects_capture_initial_version ON file_objects;
            DROP FUNCTION IF EXISTS coglatas_capture_initial_file_version();
            DROP TRIGGER IF EXISTS trg_file_versions_append_only ON file_versions;
            DROP FUNCTION IF EXISTS coglatas_file_versions_append_only_guard();
            DROP TABLE IF EXISTS file_versions;
            """);
    }
}
