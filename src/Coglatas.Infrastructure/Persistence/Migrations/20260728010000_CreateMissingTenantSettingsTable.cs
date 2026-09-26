using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Globalization;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Repairs the historical migration chain omission for the already-modelled
/// tenant_settings table. The model snapshot has always contained this entity,
/// but a clean database did not receive its physical table.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260728010000_CreateMissingTenantSettingsTable")]
public sealed class CreateMissingTenantSettingsTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS tenant_settings (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "DisplayName" character varying(160) NOT NULL,
                "LogoFileId" uuid NULL,
                "ThemeColor" character varying(40) NULL,
                "DefaultLocale" character varying(20) NOT NULL,
                "TimeZone" character varying(80) NOT NULL,
                "InvitationMode" character varying(40) NOT NULL,
                "StorageQuotaBytes" bigint NOT NULL,
                "UserLimit" integer NOT NULL,
                "ProjectLimit" integer NOT NULL,
                "FileUploadLimitBytes" bigint NOT NULL,
                "FeatureFlagsJson" jsonb NOT NULL,
                "NotificationSettingsJson" jsonb NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );
            """);

        HistoricalTableMigrationGuard.ValidateShape(
            migrationBuilder,
            "tenant_settings",
            ("Id", "uuid", false, null),
            ("TenantId", "uuid", false, null),
            ("DisplayName", "character varying", false, 160),
            ("LogoFileId", "uuid", true, null),
            ("ThemeColor", "character varying", true, 40),
            ("DefaultLocale", "character varying", false, 20),
            ("TimeZone", "character varying", false, 80),
            ("InvitationMode", "character varying", false, 40),
            ("StorageQuotaBytes", "bigint", false, null),
            ("UserLimit", "integer", false, null),
            ("ProjectLimit", "integer", false, null),
            ("FileUploadLimitBytes", "bigint", false, null),
            ("FeatureFlagsJson", "jsonb", false, null),
            ("NotificationSettingsJson", "jsonb", false, null),
            ("CreatedAt", "timestamp with time zone", false, null),
            ("UpdatedAt", "timestamp with time zone", true, null));

        migrationBuilder.Sql(
            """
            DO $migration$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_tenant_settings'
                      AND conrelid = 'tenant_settings'::regclass
                ) THEN
                    ALTER TABLE tenant_settings
                        ADD CONSTRAINT "PK_tenant_settings" PRIMARY KEY ("Id");
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_tenant_settings_file_objects_LogoFileId'
                      AND conrelid = 'tenant_settings'::regclass
                ) THEN
                    ALTER TABLE tenant_settings
                        ADD CONSTRAINT "FK_tenant_settings_file_objects_LogoFileId"
                        FOREIGN KEY ("LogoFileId") REFERENCES file_objects ("Id") ON DELETE SET NULL;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_tenant_settings_tenants_TenantId'
                      AND conrelid = 'tenant_settings'::regclass
                ) THEN
                    ALTER TABLE tenant_settings
                        ADD CONSTRAINT "FK_tenant_settings_tenants_TenantId"
                        FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT;
                END IF;
            END
            $migration$;

            CREATE INDEX IF NOT EXISTS "IX_tenant_settings_CreatedAt"
                ON tenant_settings ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_tenant_settings_LogoFileId"
                ON tenant_settings ("LogoFileId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_tenant_settings_TenantId"
                ON tenant_settings ("TenantId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This migration repairs a historical omission for a table already
        // present in every earlier model snapshot. It cannot know whether a
        // deployment created that table out-of-band, so rollback intentionally
        // preserves the production table and its data. Reapplying Up is safe.
    }
}

internal static class HistoricalTableMigrationGuard
{
    public static void ValidateShape(
        MigrationBuilder migrationBuilder,
        string tableName,
        params (string Name, string DataType, bool Nullable, int? MaxLength)[] expectedColumns)
    {
        var expectedValues = string.Join(
            ",\n",
            expectedColumns.Select(column =>
                $"('{column.Name}', '{column.DataType}', '{(column.Nullable ? "YES" : "NO")}', " +
                (column.MaxLength is null
                    ? "NULL::integer"
                    : column.MaxLength.Value.ToString(CultureInfo.InvariantCulture)) +
                ")"));

        migrationBuilder.Sql(
            $$"""
            DO $migration$
            DECLARE
                expected record;
            BEGIN
                FOR expected IN
                    SELECT *
                    FROM (VALUES
                        {{expectedValues}}
                    ) AS shape(column_name, data_type, is_nullable, maximum_length)
                LOOP
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns AS actual
                        WHERE actual.table_schema = current_schema()
                          AND actual.table_name = '{{tableName}}'
                          AND actual.column_name = expected.column_name
                          AND actual.data_type = expected.data_type
                          AND actual.is_nullable = expected.is_nullable
                          AND (
                              expected.maximum_length IS NULL
                              OR actual.character_maximum_length = expected.maximum_length
                          )
                    ) THEN
                        RAISE EXCEPTION
                            'Existing table %.% does not match the EF model for column %.',
                            current_schema(),
                            '{{tableName}}',
                            expected.column_name;
                    END IF;
                END LOOP;
            END
            $migration$;
            """);
    }
}
