using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260901030000_AddTaskExecutionSourcePolicyV2")]
public sealed class AddTaskExecutionSourcePolicyV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE task_execution_source_policy_documents (
                "OwnerType" character varying(16) NOT NULL,
                "OwnerId" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "WorkspaceId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "TaskItemId" uuid NULL,
                "PolicySchemaVersion" integer NOT NULL,
                "ProjectScopeVersion" bigint NOT NULL,
                "TaskOverrideVersion" bigint NULL,
                "PolicyJson" jsonb NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_task_execution_source_policy_documents" PRIMARY KEY ("OwnerType", "OwnerId"),
                CONSTRAINT "CK_task_execution_source_policy_documents_owner" CHECK (
                    ("OwnerType" = 'Project' AND "OwnerId" = "ProjectId" AND "TaskItemId" IS NULL AND "TaskOverrideVersion" IS NULL)
                    OR ("OwnerType" = 'Task' AND "TaskItemId" IS NOT NULL AND "OwnerId" = "TaskItemId" AND "TaskOverrideVersion" IS NOT NULL)
                    OR ("OwnerType" = 'Run' AND "TaskItemId" IS NOT NULL)
                ),
                CONSTRAINT "CK_task_execution_source_policy_documents_schema" CHECK ("PolicySchemaVersion" = 2),
                CONSTRAINT "CK_task_execution_source_policy_documents_versions" CHECK (
                    "ProjectScopeVersion" >= 0 AND ("TaskOverrideVersion" IS NULL OR "TaskOverrideVersion" > 0)
                )
            );

            CREATE INDEX "IX_task_execution_source_policy_documents_tenant_project"
                ON task_execution_source_policy_documents ("TenantId", "ProjectId", "OwnerType");
            CREATE INDEX "IX_task_execution_source_policy_documents_task"
                ON task_execution_source_policy_documents ("TenantId", "TaskItemId", "OwnerType")
                WHERE "TaskItemId" IS NOT NULL;

            CREATE OR REPLACE FUNCTION reject_task_execution_run_policy_mutation()
            RETURNS trigger AS $$
            BEGIN
                IF OLD."OwnerType" = 'Run' THEN
                    RAISE EXCEPTION 'Task execution run source-policy snapshots are immutable';
                END IF;
                IF TG_OP = 'DELETE' THEN
                    RETURN OLD;
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_task_execution_source_policy_run_immutable"
            BEFORE UPDATE OR DELETE ON task_execution_source_policy_documents
            FOR EACH ROW EXECUTE FUNCTION reject_task_execution_run_policy_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_task_execution_source_policy_run_immutable" ON task_execution_source_policy_documents;
            DROP FUNCTION IF EXISTS reject_task_execution_run_policy_mutation();
            DROP TABLE IF EXISTS task_execution_source_policy_documents;
            """);
    }
}
