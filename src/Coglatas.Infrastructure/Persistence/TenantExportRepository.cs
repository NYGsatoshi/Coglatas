using System.IO.Compression;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Security.Redaction;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class TenantExportRepository(
    AppDbContext dbContext,
    IRedactionService redactionService) : ITenantExportRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
    }

    public Task<ExportJob?> GetExportJobAsync(Guid exportJobId, CancellationToken cancellationToken = default)
    {
        return dbContext.ExportJobs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(job => job.Id == exportJobId, cancellationToken);
    }

    public async Task AddExportJobAsync(ExportJob exportJob, CancellationToken cancellationToken = default)
    {
        await dbContext.ExportJobs.AddAsync(exportJob, cancellationToken);
    }

    public async Task<byte[]> CreateMetadataZipAsync(
        Guid tenantId,
        AuthorizationContext authorizationContext,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty ||
            authorizationContext.AuthorizationState != RedactionAuthorizationState.Allowed ||
            !authorizationContext.ActorId.HasValue ||
            authorizationContext.ActorId.Value == Guid.Empty ||
            authorizationContext.TenantId != tenantId ||
            authorizationContext.Purpose != RedactionPurpose.ExportBuild ||
            !string.Equals(authorizationContext.ModuleKey, "TenantExport", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Tenant export authorization context does not match the requested Tenant export boundary.");
        }

        async Task AddRedactedJsonAsync<T>(
            ZipArchive archive,
            string path,
            T value,
            CancellationToken token)
        {
            object projection;
            if (value is System.Collections.IEnumerable values && value is not string)
            {
                var rows = new List<object>();
                foreach (var row in values)
                {
                    if (row is null)
                    {
                        throw new InvalidOperationException("Tenant export rows must not be null.");
                    }

                    rows.Add(RedactRow(row));
                }

                projection = rows;
            }
            else
            {
                projection = RedactRow(value!);
            }

            await AddJsonAsync(archive, path, projection, token);
        }

        object RedactRow(object source)
        {
            var result = redactionService.Redact(
                authorizationContext,
                source,
                RedactionProfile.ExportRow);

            return result.Value switch
            {
                RedactedPayload => throw new InvalidOperationException(
                    "Canonical redaction did not return a serializable export row."),
                null => throw new InvalidOperationException(
                    "Canonical redaction returned a null export row."),
                _ => result.Value
            };
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddRedactedJsonAsync(archive, "manifest.json", new
            {
                exportVersion = 1,
                exportType = "Metadata",
                tenantId,
                createdAtUtc = DateTimeOffset.UtcNow,
                excludes = new[]
                {
                    "password hashes",
                    "raw tokens",
                    "token hashes",
                    "webhook secrets",
                    "sensitive secrets",
                    "file bodies"
                }
            }, cancellationToken);

            await AddRedactedJsonAsync(archive, "tenant.json", await dbContext.Tenants
                .IgnoreQueryFilters()
                .Where(tenant => tenant.Id == tenantId)
                .Select(tenant => new
                {
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.DisplayName,
                    tenant.PrimaryDomain,
                    tenant.Status,
                    tenant.PlanId,
                    tenant.CreatedAt,
                    tenant.UpdatedAt,
                    tenant.DeletedAt,
                    tenant.DeletedByUserId,
                    tenant.DeleteReason
                })
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "users.json", await dbContext.TenantUsers
                .IgnoreQueryFilters()
                .Where(membership => membership.TenantId == tenantId)
                .Select(membership => new
                {
                    membership.Id,
                    membership.TenantId,
                    membership.UserId,
                    membership.Role,
                    membership.Status,
                    membership.JoinedAt,
                    membership.InvitedByUserId,
                    user = membership.User == null ? null : new
                    {
                        membership.User.Id,
                        membership.User.DisplayName,
                        membership.User.Email,
                        membership.User.Status,
                        membership.User.LastLoginAt,
                        membership.User.CreatedAt,
                        membership.User.UpdatedAt,
                        membership.User.DeletedAt
                    }
                })
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "workspaces.json", await dbContext.Workspaces
                .IgnoreQueryFilters()
                .Where(workspace => workspace.TenantId == tenantId)
                .Select(workspace => new
                {
                    workspace.Id,
                    workspace.TenantId,
                    workspace.Name,
                    workspace.Slug,
                    workspace.Description,
                    workspace.Icon,
                    workspace.Status,
                    workspace.CreatedByUserId,
                    workspace.CreatedAt,
                    workspace.UpdatedAt,
                    workspace.DeletedAt,
                    workspace.DeletedByUserId,
                    workspace.DeleteReason
                })
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "groups.json", await dbContext.Groups
                .IgnoreQueryFilters()
                .Where(group => group.TenantId == tenantId)
                .Select(group => new
                {
                    group.Id,
                    group.TenantId,
                    group.WorkspaceId,
                    group.ParentGroupId,
                    group.Name,
                    group.Slug,
                    group.Description,
                    group.GroupType,
                    group.Status,
                    group.CreatedByUserId,
                    group.CreatedAt,
                    group.UpdatedAt,
                    group.DeletedAt,
                    group.DeletedByUserId,
                    group.DeleteReason
                })
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "projects.json", await dbContext.Projects
                .IgnoreQueryFilters()
                .Where(project => project.TenantId == tenantId)
                .Select(project => new
                {
                    project.Id,
                    project.TenantId,
                    project.WorkspaceId,
                    project.GroupId,
                    project.OwnerUserId,
                    project.Name,
                    project.Slug,
                    project.Description,
                    project.Status,
                    project.StartDate,
                    project.DueDate,
                    project.CreatedByUserId,
                    project.CreatedAt,
                    project.UpdatedAt,
                    project.DeletedAt,
                    project.DeletedByUserId,
                    project.DeleteReason
                })
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "tasks.json", await dbContext.TaskItems
                .IgnoreQueryFilters()
                .Where(task => task.TenantId == tenantId)
                .Select(task => new
                {
                    task.Id,
                    task.TenantId,
                    task.ProjectId,
                    task.MilestoneId,
                    task.Title,
                    task.Description,
                    task.Status,
                    task.Priority,
                    task.StartDate,
                    task.DueDate,
                    task.ProgressPercent,
                    task.SortOrder,
                    task.CreatedByUserId,
                    task.CreatedAt,
                    task.UpdatedAt,
                    task.DeletedAt,
                    task.DeletedByUserId,
                    task.DeleteReason
                })
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "events.json", await dbContext.ActivityEvents
                .IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "attendance.json", await dbContext.EventAttendances
                .IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "comments.json", await dbContext.Comments
                .IgnoreQueryFilters()
                .Where(comment => comment.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "artifacts.json", await dbContext.Artifacts
                .IgnoreQueryFilters()
                .Where(artifact => artifact.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "file_objects.json", await dbContext.FileObjects
                .IgnoreQueryFilters()
                .Where(file => file.TenantId == tenantId)
                .Select(file => new
                {
                    file.Id,
                    file.TenantId,
                    file.WorkspaceId,
                    file.GroupId,
                    file.ProjectId,
                    file.UploadedByUserId,
                    file.OriginalFileName,
                    file.StorageKey,
                    file.ContentType,
                    file.SizeBytes,
                    file.HashSha256,
                    file.Status,
                    file.CreatedAt,
                    file.UpdatedAt,
                    file.DeletedAt,
                    file.DeletedByUserId,
                    file.DeleteReason
                })
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "notifications.json", await dbContext.Notifications
                .IgnoreQueryFilters()
                .Where(notification => notification.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "audit_logs.json", await dbContext.AuditLogs
                .IgnoreQueryFilters()
                .Where(log => log.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "tenant_settings.json", await dbContext.TenantSettings
                .IgnoreQueryFilters()
                .Where(settings => settings.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);

            await AddRedactedJsonAsync(archive, "usage_records.json", await dbContext.UsageRecords
                .IgnoreQueryFilters()
                .Where(record => record.TenantId == tenantId)
                .ToListAsync(cancellationToken), cancellationToken);
        }

        return stream.ToArray();
    }

    private static async Task AddJsonAsync<T>(ZipArchive archive, string path, T value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, value, JsonOptions, cancellationToken);
    }
}
