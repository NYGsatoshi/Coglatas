using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260819070000_Wpc02DTaskWorkflowTemplates")]
public sealed class Wpc02DTaskWorkflowTemplates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_workspaces_TenantId_Id_wpc02d"
                ON "workspaces" ("TenantId", "Id");

            CREATE TABLE "task_workflow_templates" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Name" character varying(120) NOT NULL,
                "ReviewEnforcementEnabled" boolean NOT NULL DEFAULT TRUE,
                "VersionNo" bigint NOT NULL DEFAULT 1,
                CONSTRAINT "PK_task_workflow_templates" PRIMARY KEY ("Id"),
                CONSTRAINT "AK_task_workflow_templates_TenantId_Id" UNIQUE ("TenantId", "Id"),
                CONSTRAINT "FK_task_workflow_templates_tenants_TenantId"
                    FOREIGN KEY ("TenantId") REFERENCES "tenants" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "CK_task_workflow_templates_name" CHECK (btrim("Name") <> ''),
                CONSTRAINT "CK_task_workflow_templates_version" CHECK ("VersionNo" > 0)
            );

            CREATE INDEX "IX_task_workflow_templates_TenantId_Name"
                ON "task_workflow_templates" ("TenantId", "Name");

            CREATE TABLE "task_workflow_template_stages" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "TemplateId" uuid NOT NULL,
                "Name" character varying(120) NOT NULL,
                "InternalCategory" character varying(40) NOT NULL,
                "SortKey" bigint NOT NULL,
                "WipWarningLimit" integer NULL,
                "IsInitialStage" boolean NOT NULL DEFAULT FALSE,
                "IsTerminalStage" boolean NOT NULL DEFAULT FALSE,
                "VersionNo" bigint NOT NULL DEFAULT 1,
                CONSTRAINT "PK_task_workflow_template_stages" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_task_workflow_template_stages_templates_TenantId_TemplateId"
                    FOREIGN KEY ("TenantId", "TemplateId")
                    REFERENCES "task_workflow_templates" ("TenantId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "CK_task_workflow_template_stages_name" CHECK (btrim("Name") <> ''),
                CONSTRAINT "CK_task_workflow_template_stages_category" CHECK (
                    "InternalCategory" IN ('Backlog', 'Todo', 'InProgress', 'Review', 'Done', 'Cancelled')),
                CONSTRAINT "CK_task_workflow_template_stages_wip" CHECK (
                    "WipWarningLimit" IS NULL OR "WipWarningLimit" > 0),
                CONSTRAINT "CK_task_workflow_template_stages_version" CHECK ("VersionNo" > 0)
            );

            CREATE UNIQUE INDEX "UX_task_workflow_template_stages_TemplateId_SortKey"
                ON "task_workflow_template_stages" ("TemplateId", "SortKey");
            CREATE UNIQUE INDEX "UX_task_workflow_template_stages_TemplateId_NormalizedName"
                ON "task_workflow_template_stages" ("TemplateId", lower(btrim("Name")));
            CREATE INDEX "IX_task_workflow_template_stages_TenantId_TemplateId"
                ON "task_workflow_template_stages" ("TenantId", "TemplateId");

            CREATE TABLE "workspace_task_workflow_defaults" (
                "TenantId" uuid NOT NULL,
                "WorkspaceId" uuid NOT NULL,
                "TemplateId" uuid NOT NULL,
                "VersionNo" bigint NOT NULL DEFAULT 1,
                CONSTRAINT "PK_workspace_task_workflow_defaults" PRIMARY KEY ("TenantId", "WorkspaceId"),
                CONSTRAINT "FK_workspace_task_workflow_defaults_workspaces_TenantId_WorkspaceId"
                    FOREIGN KEY ("TenantId", "WorkspaceId")
                    REFERENCES "workspaces" ("TenantId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_workspace_task_workflow_defaults_templates_TenantId_TemplateId"
                    FOREIGN KEY ("TenantId", "TemplateId")
                    REFERENCES "task_workflow_templates" ("TenantId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "CK_workspace_task_workflow_defaults_version" CHECK ("VersionNo" > 0)
            );

            CREATE INDEX "IX_workspace_task_workflow_defaults_TemplateId"
                ON "workspace_task_workflow_defaults" ("TemplateId");

            CREATE TABLE "tenant_task_workflow_defaults" (
                "TenantId" uuid NOT NULL,
                "TemplateId" uuid NOT NULL,
                "VersionNo" bigint NOT NULL DEFAULT 1,
                CONSTRAINT "PK_tenant_task_workflow_defaults" PRIMARY KEY ("TenantId"),
                CONSTRAINT "FK_tenant_task_workflow_defaults_tenants_TenantId"
                    FOREIGN KEY ("TenantId") REFERENCES "tenants" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_tenant_task_workflow_defaults_templates_TenantId_TemplateId"
                    FOREIGN KEY ("TenantId", "TemplateId")
                    REFERENCES "task_workflow_templates" ("TenantId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "CK_tenant_task_workflow_defaults_version" CHECK ("VersionNo" > 0)
            );

            CREATE INDEX "IX_tenant_task_workflow_defaults_TemplateId"
                ON "tenant_task_workflow_defaults" ("TemplateId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS "tenant_task_workflow_defaults";
            DROP TABLE IF EXISTS "workspace_task_workflow_defaults";
            DROP TABLE IF EXISTS "task_workflow_template_stages";
            DROP TABLE IF EXISTS "task_workflow_templates";
            DROP INDEX IF EXISTS "UX_workspaces_TenantId_Id_wpc02d";
            """);
    }
}
