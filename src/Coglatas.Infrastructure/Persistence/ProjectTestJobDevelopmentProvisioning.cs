using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Explicit PostgreSQL provisioning for a development Project Test host.</summary>
public static class ProjectTestJobDevelopmentProvisioning
{
    public static async Task ProvisionAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql() || context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Project Test development provisioning requires its own PostgreSQL transaction.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await ProjectIdeDevelopmentSchema.EnsureExistsAsync(context, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE projectide_development.project_ide_test_jobs (
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
                    FOREIGN KEY (project_id, tenant_id) REFERENCES public.projects ("Id", "TenantId") ON DELETE RESTRICT,
                CONSTRAINT project_ide_test_jobs_binding CHECK (jsonb_typeof(binding) = 'object'),
                CONSTRAINT project_ide_test_jobs_terminal CHECK (
                    (state = 0 AND terminal_at IS NULL AND result IS NULL) OR
                    (state = 1 AND terminal_at IS NOT NULL AND terminal_at >= created_at AND
                        result IS NOT NULL AND jsonb_typeof(result) = 'object') OR
                    (state = 2 AND terminal_at IS NOT NULL AND terminal_at >= created_at AND result IS NULL)
                )
            );

            CREATE INDEX project_ide_test_jobs_project_idx ON projectide_development.project_ide_test_jobs (project_id, tenant_id);

            CREATE FUNCTION projectide_development.enforce_project_ide_test_job_transition() RETURNS trigger LANGUAGE plpgsql AS $guard$
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
                BEFORE UPDATE ON projectide_development.project_ide_test_jobs
                FOR EACH ROW EXECUTE FUNCTION projectide_development.enforce_project_ide_test_job_transition();
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
