using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstPartyProjectFilesRuntimeContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueuedAtUtc",
                table: "task_execution_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuntimeContractVersion",
                table: "task_execution_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "RuntimeProvider",
                table: "task_execution_runs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "FirstPartyProjectFilesRuntimeV1");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAtUtc",
                table: "task_execution_runs",
                type: "timestamp with time zone",
                nullable: true);

            // #357 persisted a policy-only, provider-none lifecycle. Preserve
            // historical rows without retaining RuntimeUnavailable as a
            // terminal state in the selected V1 runtime contract.
            migrationBuilder.Sql("""
                UPDATE task_execution_runs
                SET
                    "Status" = CASE "Status"
                        WHEN 'Prepared' THEN 'Accepted'
                        WHEN 'Waiting' THEN 'Queued'
                        WHEN 'NeedsInput' THEN 'Failed'
                        WHEN 'RuntimeUnavailable' THEN 'Failed'
                        WHEN 'Completed' THEN 'Succeeded'
                        ELSE "Status"
                    END,
                    "QueuedAtUtc" = CASE
                        WHEN "Status" = 'Waiting' THEN "RequestedAtUtc"
                        ELSE "QueuedAtUtc"
                    END,
                    "FinishedAtUtc" = CASE
                        WHEN "Status" IN ('NeedsInput', 'RuntimeUnavailable')
                            THEN COALESCE("FinishedAtUtc", "RequestedAtUtc")
                        ELSE "FinishedAtUtc"
                    END,
                    "FailureCode" = CASE
                        WHEN "Status" = 'NeedsInput' AND "FailureCode" IS NULL
                            THEN 'TASK_EXECUTION_LEGACY_INPUT_UNAVAILABLE'
                        WHEN "Status" = 'RuntimeUnavailable' AND (
                            "FailureCode" IS NULL OR
                            "FailureCode" = 'TASK_EXECUTION_RUNTIME_UNAVAILABLE')
                            THEN 'TASK_EXECUTION_LEGACY_PROVIDER_UNAVAILABLE'
                        ELSE "FailureCode"
                    END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_execution_runs_status",
                table: "task_execution_runs",
                sql: "\"Status\" IN ('Accepted', 'Queued', 'Running', 'Succeeded', 'Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_execution_runs_runtime_contract",
                table: "task_execution_runs",
                sql: "\"RuntimeProvider\" = 'FirstPartyProjectFilesRuntimeV1' AND \"RuntimeContractVersion\" = 1");

            // Entity configuration is not sufficient to prevent a raw SQL or
            // platform-scope update from rewriting provider identity or
            // skipping an immutable lifecycle transition. Keep the existing
            // source-policy/ownership trigger and add narrow V1 guards.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION task_execution_run_runtime_contract_guard() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' AND (
                        NEW."RuntimeProvider" IS DISTINCT FROM OLD."RuntimeProvider" OR
                        NEW."RuntimeContractVersion" IS DISTINCT FROM OLD."RuntimeContractVersion") THEN
                        RAISE EXCEPTION 'Task execution run runtime contract is immutable';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER task_execution_run_runtime_contract_guard_trigger
                    BEFORE UPDATE OF "RuntimeProvider", "RuntimeContractVersion"
                    ON task_execution_runs
                    FOR EACH ROW EXECUTE FUNCTION task_execution_run_runtime_contract_guard();

                CREATE OR REPLACE FUNCTION task_execution_run_lifecycle_guard() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'INSERT' AND NEW."Status" <> 'Accepted' THEN
                        RAISE EXCEPTION 'Task execution run must begin Accepted';
                    END IF;

                    IF TG_OP = 'UPDATE' AND NEW."Status" IS DISTINCT FROM OLD."Status" AND NOT (
                        (OLD."Status" = 'Accepted' AND NEW."Status" = 'Queued') OR
                        (OLD."Status" = 'Queued' AND NEW."Status" = 'Running') OR
                        (OLD."Status" = 'Running' AND NEW."Status" IN ('Succeeded', 'Failed'))
                    ) THEN
                        RAISE EXCEPTION 'Task execution run lifecycle transition is invalid';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER task_execution_run_lifecycle_guard_trigger
                    BEFORE INSERT OR UPDATE OF "Status"
                    ON task_execution_runs
                    FOR EACH ROW EXECUTE FUNCTION task_execution_run_lifecycle_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_run_lifecycle_guard_trigger ON task_execution_runs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_run_lifecycle_guard();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_run_runtime_contract_guard_trigger ON task_execution_runs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_run_runtime_contract_guard();");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_execution_runs_status",
                table: "task_execution_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_execution_runs_runtime_contract",
                table: "task_execution_runs");

            migrationBuilder.Sql("""
                UPDATE task_execution_runs
                SET "Status" = CASE "Status"
                    WHEN 'Accepted' THEN 'Prepared'
                    WHEN 'Queued' THEN 'Waiting'
                    WHEN 'Running' THEN 'Prepared'
                    WHEN 'Succeeded' THEN 'Completed'
                    WHEN 'Failed' THEN 'RuntimeUnavailable'
                    ELSE "Status"
                END;
                """);

            migrationBuilder.DropColumn(
                name: "QueuedAtUtc",
                table: "task_execution_runs");

            migrationBuilder.DropColumn(
                name: "RuntimeContractVersion",
                table: "task_execution_runs");

            migrationBuilder.DropColumn(
                name: "RuntimeProvider",
                table: "task_execution_runs");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "task_execution_runs");
        }
    }
}
