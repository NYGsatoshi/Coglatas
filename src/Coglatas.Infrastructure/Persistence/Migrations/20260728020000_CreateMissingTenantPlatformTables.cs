using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Repairs historical migration-chain omissions for already-modelled tenant
/// platform tables. A clean database must contain every table represented by
/// the current model before application bootstrap and feature evaluation run.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260728020000_CreateMissingTenantPlatformTables")]
public sealed class CreateMissingTenantPlatformTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS api_tokens (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Name" character varying(160) NOT NULL,
                "TokenHash" character varying(128) NOT NULL,
                "ScopesJson" character varying(4000) NOT NULL,
                "ExpiresAt" timestamp with time zone NULL,
                "CreatedByUserId" uuid NOT NULL,
                "LastUsedAt" timestamp with time zone NULL,
                "RevokedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );

            CREATE TABLE IF NOT EXISTS export_jobs (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "RequestedByUserId" uuid NOT NULL,
                "Status" character varying(40) NOT NULL,
                "ExportType" character varying(40) NOT NULL,
                "FileObjectId" uuid NULL,
                "CompletedAt" timestamp with time zone NULL,
                "ErrorMessage" character varying(2000) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );

            CREATE TABLE IF NOT EXISTS integration_accounts (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Provider" character varying(40) NOT NULL,
                "DisplayName" character varying(160) NOT NULL,
                "Status" character varying(40) NOT NULL,
                "SettingsJson" character varying(12000) NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "DeletedAt" timestamp with time zone NULL,
                "DeletedByUserId" uuid NULL,
                "DeleteReason" character varying(500) NULL
            );

            CREATE TABLE IF NOT EXISTS subscriptions (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "PlanId" uuid NOT NULL,
                "Status" character varying(40) NOT NULL,
                "StartedAt" timestamp with time zone NOT NULL,
                "EndsAt" timestamp with time zone NULL,
                "TrialEndsAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );

            CREATE TABLE IF NOT EXISTS usage_records (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Date" date NOT NULL,
                "ActiveUserCount" integer NOT NULL,
                "TotalUserCount" integer NOT NULL,
                "ProjectCount" integer NOT NULL,
                "TaskCount" integer NOT NULL,
                "FileCount" integer NOT NULL,
                "StorageUsedBytes" bigint NOT NULL,
                "ApiRequestCount" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS webhook_endpoints (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Name" character varying(160) NOT NULL,
                "Url" character varying(2000) NOT NULL,
                "SecretHash" character varying(128) NULL,
                "EnabledEventsJson" character varying(12000) NOT NULL,
                "Status" character varying(40) NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "DeletedAt" timestamp with time zone NULL,
                "DeletedByUserId" uuid NULL,
                "DeleteReason" character varying(500) NULL
            );
            """);

        HistoricalTableMigrationGuard.ValidateShape(
            migrationBuilder,
            "api_tokens",
            ("Id", "uuid", false, null),
            ("TenantId", "uuid", false, null),
            ("Name", "character varying", false, 160),
            ("TokenHash", "character varying", false, 128),
            ("ScopesJson", "character varying", false, 4000),
            ("ExpiresAt", "timestamp with time zone", true, null),
            ("CreatedByUserId", "uuid", false, null),
            ("LastUsedAt", "timestamp with time zone", true, null),
            ("RevokedAt", "timestamp with time zone", true, null),
            ("CreatedAt", "timestamp with time zone", false, null),
            ("UpdatedAt", "timestamp with time zone", true, null));

        HistoricalTableMigrationGuard.ValidateShape(
            migrationBuilder,
            "export_jobs",
            ("Id", "uuid", false, null),
            ("TenantId", "uuid", false, null),
            ("RequestedByUserId", "uuid", false, null),
            ("Status", "character varying", false, 40),
            ("ExportType", "character varying", false, 40),
            ("FileObjectId", "uuid", true, null),
            ("CompletedAt", "timestamp with time zone", true, null),
            ("ErrorMessage", "character varying", true, 2000),
            ("CreatedAt", "timestamp with time zone", false, null),
            ("UpdatedAt", "timestamp with time zone", true, null));

        HistoricalTableMigrationGuard.ValidateShape(
            migrationBuilder,
            "integration_accounts",
            ("Id", "uuid", false, null),
            ("TenantId", "uuid", false, null),
            ("Provider", "character varying", false, 40),
            ("DisplayName", "character varying", false, 160),
            ("Status", "character varying", false, 40),
            ("SettingsJson", "character varying", false, 12000),
            ("CreatedByUserId", "uuid", false, null),
            ("CreatedAt", "timestamp with time zone", false, null),
            ("UpdatedAt", "timestamp with time zone", true, null),
            ("DeletedAt", "timestamp with time zone", true, null),
            ("DeletedByUserId", "uuid", true, null),
            ("DeleteReason", "character varying", true, 500));

        HistoricalTableMigrationGuard.ValidateShape(
            migrationBuilder,
            "subscriptions",
            ("Id", "uuid", false, null),
            ("TenantId", "uuid", false, null),
            ("PlanId", "uuid", false, null),
            ("Status", "character varying", false, 40),
            ("StartedAt", "timestamp with time zone", false, null),
            ("EndsAt", "timestamp with time zone", true, null),
            ("TrialEndsAt", "timestamp with time zone", true, null),
            ("CreatedAt", "timestamp with time zone", false, null),
            ("UpdatedAt", "timestamp with time zone", true, null));

        HistoricalTableMigrationGuard.ValidateShape(
            migrationBuilder,
            "usage_records",
            ("Id", "uuid", false, null),
            ("TenantId", "uuid", false, null),
            ("Date", "date", false, null),
            ("ActiveUserCount", "integer", false, null),
            ("TotalUserCount", "integer", false, null),
            ("ProjectCount", "integer", false, null),
            ("TaskCount", "integer", false, null),
            ("FileCount", "integer", false, null),
            ("StorageUsedBytes", "bigint", false, null),
            ("ApiRequestCount", "integer", false, null),
            ("CreatedAt", "timestamp with time zone", false, null));

        HistoricalTableMigrationGuard.ValidateShape(
            migrationBuilder,
            "webhook_endpoints",
            ("Id", "uuid", false, null),
            ("TenantId", "uuid", false, null),
            ("Name", "character varying", false, 160),
            ("Url", "character varying", false, 2000),
            ("SecretHash", "character varying", true, 128),
            ("EnabledEventsJson", "character varying", false, 12000),
            ("Status", "character varying", false, 40),
            ("CreatedByUserId", "uuid", false, null),
            ("CreatedAt", "timestamp with time zone", false, null),
            ("UpdatedAt", "timestamp with time zone", true, null),
            ("DeletedAt", "timestamp with time zone", true, null),
            ("DeletedByUserId", "uuid", true, null),
            ("DeleteReason", "character varying", true, 500));

        migrationBuilder.Sql(
            """
            DO $migration$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_api_tokens' AND conrelid = 'api_tokens'::regclass
                ) THEN
                    ALTER TABLE api_tokens ADD CONSTRAINT "PK_api_tokens" PRIMARY KEY ("Id");
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_api_tokens_users_CreatedByUserId' AND conrelid = 'api_tokens'::regclass
                ) THEN
                    ALTER TABLE api_tokens ADD CONSTRAINT "FK_api_tokens_users_CreatedByUserId"
                        FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_export_jobs' AND conrelid = 'export_jobs'::regclass
                ) THEN
                    ALTER TABLE export_jobs ADD CONSTRAINT "PK_export_jobs" PRIMARY KEY ("Id");
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_export_jobs_file_objects_FileObjectId' AND conrelid = 'export_jobs'::regclass
                ) THEN
                    ALTER TABLE export_jobs ADD CONSTRAINT "FK_export_jobs_file_objects_FileObjectId"
                        FOREIGN KEY ("FileObjectId") REFERENCES file_objects ("Id") ON DELETE SET NULL;
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_export_jobs_users_RequestedByUserId' AND conrelid = 'export_jobs'::regclass
                ) THEN
                    ALTER TABLE export_jobs ADD CONSTRAINT "FK_export_jobs_users_RequestedByUserId"
                        FOREIGN KEY ("RequestedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_integration_accounts' AND conrelid = 'integration_accounts'::regclass
                ) THEN
                    ALTER TABLE integration_accounts ADD CONSTRAINT "PK_integration_accounts" PRIMARY KEY ("Id");
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_integration_accounts_users_CreatedByUserId' AND conrelid = 'integration_accounts'::regclass
                ) THEN
                    ALTER TABLE integration_accounts ADD CONSTRAINT "FK_integration_accounts_users_CreatedByUserId"
                        FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_subscriptions' AND conrelid = 'subscriptions'::regclass
                ) THEN
                    ALTER TABLE subscriptions ADD CONSTRAINT "PK_subscriptions" PRIMARY KEY ("Id");
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_subscriptions_plans_PlanId' AND conrelid = 'subscriptions'::regclass
                ) THEN
                    ALTER TABLE subscriptions ADD CONSTRAINT "FK_subscriptions_plans_PlanId"
                        FOREIGN KEY ("PlanId") REFERENCES plans ("Id") ON DELETE RESTRICT;
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_subscriptions_tenants_TenantId' AND conrelid = 'subscriptions'::regclass
                ) THEN
                    ALTER TABLE subscriptions ADD CONSTRAINT "FK_subscriptions_tenants_TenantId"
                        FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_usage_records' AND conrelid = 'usage_records'::regclass
                ) THEN
                    ALTER TABLE usage_records ADD CONSTRAINT "PK_usage_records" PRIMARY KEY ("Id");
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_usage_records_tenants_TenantId' AND conrelid = 'usage_records'::regclass
                ) THEN
                    ALTER TABLE usage_records ADD CONSTRAINT "FK_usage_records_tenants_TenantId"
                        FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_webhook_endpoints' AND conrelid = 'webhook_endpoints'::regclass
                ) THEN
                    ALTER TABLE webhook_endpoints ADD CONSTRAINT "PK_webhook_endpoints" PRIMARY KEY ("Id");
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_webhook_endpoints_users_CreatedByUserId' AND conrelid = 'webhook_endpoints'::regclass
                ) THEN
                    ALTER TABLE webhook_endpoints ADD CONSTRAINT "FK_webhook_endpoints_users_CreatedByUserId"
                        FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT;
                END IF;
            END
            $migration$;

            CREATE INDEX IF NOT EXISTS "IX_api_tokens_CreatedAt" ON api_tokens ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_api_tokens_CreatedByUserId" ON api_tokens ("CreatedByUserId");
            CREATE INDEX IF NOT EXISTS "IX_api_tokens_ExpiresAt" ON api_tokens ("ExpiresAt");
            CREATE INDEX IF NOT EXISTS "IX_api_tokens_RevokedAt" ON api_tokens ("RevokedAt");
            CREATE INDEX IF NOT EXISTS "IX_api_tokens_TenantId" ON api_tokens ("TenantId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_api_tokens_TokenHash" ON api_tokens ("TokenHash");
            CREATE INDEX IF NOT EXISTS "IX_api_tokens_TenantId_Name" ON api_tokens ("TenantId", "Name");

            CREATE INDEX IF NOT EXISTS "IX_export_jobs_CreatedAt" ON export_jobs ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_export_jobs_FileObjectId" ON export_jobs ("FileObjectId");
            CREATE INDEX IF NOT EXISTS "IX_export_jobs_RequestedByUserId" ON export_jobs ("RequestedByUserId");
            CREATE INDEX IF NOT EXISTS "IX_export_jobs_Status" ON export_jobs ("Status");
            CREATE INDEX IF NOT EXISTS "IX_export_jobs_TenantId" ON export_jobs ("TenantId");

            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_CreatedAt" ON integration_accounts ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_CreatedByUserId" ON integration_accounts ("CreatedByUserId");
            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_DeletedAt" ON integration_accounts ("DeletedAt");
            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_DeletedByUserId" ON integration_accounts ("DeletedByUserId");
            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_Provider" ON integration_accounts ("Provider");
            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_Status" ON integration_accounts ("Status");
            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_TenantId" ON integration_accounts ("TenantId");
            CREATE INDEX IF NOT EXISTS "IX_integration_accounts_TenantId_Provider_DisplayName"
                ON integration_accounts ("TenantId", "Provider", "DisplayName");

            CREATE INDEX IF NOT EXISTS "IX_subscriptions_CreatedAt" ON subscriptions ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_subscriptions_PlanId" ON subscriptions ("PlanId");
            CREATE INDEX IF NOT EXISTS "IX_subscriptions_TenantId" ON subscriptions ("TenantId");
            CREATE INDEX IF NOT EXISTS "IX_subscriptions_TenantId_Status" ON subscriptions ("TenantId", "Status");

            CREATE INDEX IF NOT EXISTS "IX_usage_records_TenantId" ON usage_records ("TenantId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_usage_records_TenantId_Date" ON usage_records ("TenantId", "Date");

            CREATE INDEX IF NOT EXISTS "IX_webhook_endpoints_CreatedAt" ON webhook_endpoints ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_webhook_endpoints_CreatedByUserId" ON webhook_endpoints ("CreatedByUserId");
            CREATE INDEX IF NOT EXISTS "IX_webhook_endpoints_DeletedAt" ON webhook_endpoints ("DeletedAt");
            CREATE INDEX IF NOT EXISTS "IX_webhook_endpoints_DeletedByUserId" ON webhook_endpoints ("DeletedByUserId");
            CREATE INDEX IF NOT EXISTS "IX_webhook_endpoints_Status" ON webhook_endpoints ("Status");
            CREATE INDEX IF NOT EXISTS "IX_webhook_endpoints_TenantId" ON webhook_endpoints ("TenantId");
            CREATE INDEX IF NOT EXISTS "IX_webhook_endpoints_TenantId_Name" ON webhook_endpoints ("TenantId", "Name");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These tables are represented in every earlier model snapshot and may
        // have been created out-of-band to compensate for the chain omission.
        // Preserve them and their data; reapplying Up is idempotent.
    }
}
