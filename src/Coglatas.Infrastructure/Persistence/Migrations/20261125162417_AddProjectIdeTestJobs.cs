using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125162417_AddProjectIdeTestJobs")]
public sealed class AddProjectIdeTestJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE project_ide_test_jobs (
            tenant_id uuid NOT NULL,
            project_id uuid NOT NULL,
            branch_id uuid NOT NULL,
            job_id uuid NOT NULL,
            binding jsonb NOT NULL,
            state integer NOT NULL,
            created_at timestamptz NOT NULL,
            terminal_at timestamptz NULL,
            result jsonb NULL,
            PRIMARY KEY (tenant_id, job_id),
            CONSTRAINT project_ide_test_jobs_project
                FOREIGN KEY (project_id, tenant_id) REFERENCES projects ("Id", "TenantId") ON DELETE RESTRICT,
            CONSTRAINT project_ide_test_jobs_binding CHECK (jsonb_typeof(binding) = 'object'),
            CONSTRAINT project_ide_test_jobs_terminal CHECK (
                (state = 0 AND terminal_at IS NULL AND result IS NULL) OR
                (state = 1 AND terminal_at IS NOT NULL AND terminal_at >= created_at AND
                    result IS NOT NULL AND jsonb_typeof(result) = 'object') OR
                (state = 2 AND terminal_at IS NOT NULL AND terminal_at >= created_at AND result IS NULL)
            )
        );

        CREATE INDEX project_ide_test_jobs_project_idx ON project_ide_test_jobs (project_id, tenant_id);

        CREATE FUNCTION enforce_project_ide_test_job_transition() RETURNS trigger LANGUAGE plpgsql AS $guard$
        BEGIN
            IF OLD.state <> 0 OR NEW.state = 0 OR
                (NEW.tenant_id, NEW.project_id, NEW.branch_id, NEW.job_id, NEW.binding, NEW.created_at)
                    IS DISTINCT FROM
                (OLD.tenant_id, OLD.project_id, OLD.branch_id, OLD.job_id, OLD.binding, OLD.created_at)
            THEN
                RAISE EXCEPTION 'Project Test job lineage or terminal result is immutable'
                    USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END;
        $guard$;

        CREATE TRIGGER project_ide_test_jobs_transition
            BEFORE UPDATE ON project_ide_test_jobs
            FOR EACH ROW EXECUTE FUNCTION enforce_project_ide_test_job_transition();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("project_ide_test_jobs");
        migrationBuilder.Sql("DROP FUNCTION enforce_project_ide_test_job_transition();");
    }
}
