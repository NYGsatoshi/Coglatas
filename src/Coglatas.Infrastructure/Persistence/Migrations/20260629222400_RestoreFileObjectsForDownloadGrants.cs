using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestoreFileObjectsForDownloadGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS file_objects (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "WorkspaceId" uuid NULL,
                    "GroupId" uuid NULL,
                    "ProjectId" uuid NULL,
                    "UploadedByUserId" uuid NOT NULL,
                    "OriginalFileName" character varying(260) NOT NULL,
                    "StorageKey" character varying(1024) NOT NULL,
                    "ContentType" character varying(160) NOT NULL,
                    "SizeBytes" bigint NOT NULL,
                    "HashSha256" character varying(64) NULL,
                    "Status" character varying(40) NOT NULL,
                    "DeletedAt" timestamp with time zone NULL,
                    "DeletedByUserId" uuid NULL,
                    "DeleteReason" character varying(500) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_file_objects" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "IX_file_objects_CreatedAt" ON file_objects ("CreatedAt");
                CREATE INDEX IF NOT EXISTS "IX_file_objects_GroupId" ON file_objects ("GroupId");
                CREATE INDEX IF NOT EXISTS "IX_file_objects_ProjectId" ON file_objects ("ProjectId");
                CREATE INDEX IF NOT EXISTS "IX_file_objects_Status" ON file_objects ("Status");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_file_objects_StorageKey" ON file_objects ("StorageKey");
                CREATE INDEX IF NOT EXISTS "IX_file_objects_TenantId" ON file_objects ("TenantId");
                CREATE INDEX IF NOT EXISTS "IX_file_objects_TenantId_Status_CreatedAt" ON file_objects ("TenantId", "Status", "CreatedAt");
                CREATE INDEX IF NOT EXISTS "IX_file_objects_UploadedByUserId" ON file_objects ("UploadedByUserId");
                CREATE INDEX IF NOT EXISTS "IX_file_objects_WorkspaceId" ON file_objects ("WorkspaceId");

                ALTER TABLE attachments ADD COLUMN IF NOT EXISTS "FileObjectId" uuid;
                CREATE INDEX IF NOT EXISTS "IX_attachments_FileObjectId" ON attachments ("FileObjectId");

                ALTER TABLE artifact_versions ADD COLUMN IF NOT EXISTS "FileObjectId" uuid;
                CREATE INDEX IF NOT EXISTS "IX_artifact_versions_FileObjectId" ON artifact_versions ("FileObjectId");
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_file_objects_workspaces_WorkspaceId'
                    ) THEN
                        ALTER TABLE file_objects
                        ADD CONSTRAINT "FK_file_objects_workspaces_WorkspaceId"
                        FOREIGN KEY ("WorkspaceId") REFERENCES workspaces ("Id") ON DELETE SET NULL NOT VALID;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_file_objects_groups_GroupId'
                    ) THEN
                        ALTER TABLE file_objects
                        ADD CONSTRAINT "FK_file_objects_groups_GroupId"
                        FOREIGN KEY ("GroupId") REFERENCES groups ("Id") ON DELETE SET NULL NOT VALID;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_file_objects_projects_ProjectId'
                    ) THEN
                        ALTER TABLE file_objects
                        ADD CONSTRAINT "FK_file_objects_projects_ProjectId"
                        FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE SET NULL NOT VALID;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_file_objects_users_UploadedByUserId'
                    ) THEN
                        ALTER TABLE file_objects
                        ADD CONSTRAINT "FK_file_objects_users_UploadedByUserId"
                        FOREIGN KEY ("UploadedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT NOT VALID;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_attachments_file_objects_FileObjectId'
                    ) THEN
                        ALTER TABLE attachments
                        ADD CONSTRAINT "FK_attachments_file_objects_FileObjectId"
                        FOREIGN KEY ("FileObjectId") REFERENCES file_objects ("Id") ON DELETE RESTRICT NOT VALID;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_artifact_versions_file_objects_FileObjectId'
                    ) THEN
                        ALTER TABLE artifact_versions
                        ADD CONSTRAINT "FK_artifact_versions_file_objects_FileObjectId"
                        FOREIGN KEY ("FileObjectId") REFERENCES file_objects ("Id") ON DELETE SET NULL NOT VALID;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'attachments' AND column_name = 'FileObjectId'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM attachments WHERE "FileObjectId" IS NULL
                    ) THEN
                        ALTER TABLE attachments ALTER COLUMN "FileObjectId" SET NOT NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS artifact_versions DROP CONSTRAINT IF EXISTS "FK_artifact_versions_file_objects_FileObjectId";
                ALTER TABLE IF EXISTS attachments DROP CONSTRAINT IF EXISTS "FK_attachments_file_objects_FileObjectId";
                ALTER TABLE IF EXISTS file_objects DROP CONSTRAINT IF EXISTS "FK_file_objects_users_UploadedByUserId";
                ALTER TABLE IF EXISTS file_objects DROP CONSTRAINT IF EXISTS "FK_file_objects_projects_ProjectId";
                ALTER TABLE IF EXISTS file_objects DROP CONSTRAINT IF EXISTS "FK_file_objects_groups_GroupId";
                ALTER TABLE IF EXISTS file_objects DROP CONSTRAINT IF EXISTS "FK_file_objects_workspaces_WorkspaceId";

                DROP INDEX IF EXISTS "IX_artifact_versions_FileObjectId";
                ALTER TABLE IF EXISTS artifact_versions DROP COLUMN IF EXISTS "FileObjectId";

                DROP INDEX IF EXISTS "IX_attachments_FileObjectId";
                ALTER TABLE IF EXISTS attachments DROP COLUMN IF EXISTS "FileObjectId";

                DROP TABLE IF EXISTS file_objects;
                """);
        }
    }
}
