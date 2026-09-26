using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Security.Redaction;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.TenantExports;

public sealed class TenantExportService(
    ITenantExportRepository exports,
    ITenantAuthorizationService tenantAuthorization,
    IFeatureFlagService features,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IClock clock,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork) : ITenantExportService
{
    public async Task<Result<TenantExportFileResponse>> ExportAsync(TenantExportRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result<TenantExportFileResponse>.Failure("Authentication is required.");
        }

        var tenantId = request.TenantId ?? currentTenant.TenantId;
        var authorization = await AuthorizeExportAsync(userId, tenantId, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return Result<TenantExportFileResponse>.Failure(authorization.Error!);
        }

        if (await exports.GetTenantAsync(tenantId, cancellationToken) is null)
        {
            return Result<TenantExportFileResponse>.Failure("Tenant not found.");
        }

        var exportJob = new ExportJob
        {
            TenantId = tenantId,
            RequestedByUserId = userId,
            Status = ExportJobStatus.Running,
            ExportType = TenantExportType.Metadata
        };

        await exports.AddExportJobAsync(exportJob, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            // Export generation can happen after the request-side decision and
            // must be authorized again immediately before rows are materialized.
            var buildAuthorization = await ReauthorizeExportBuildAsync(
                userId,
                tenantId,
                exportJob.Id,
                cancellationToken);
            if (!buildAuthorization.IsSuccess)
            {
                exportJob.Status = ExportJobStatus.Failed;
                exportJob.ErrorMessage = "Authorization changed before export build.";
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<TenantExportFileResponse>.Failure(
                    "Tenant export could not be completed.");
            }

            var content = await exports.CreateMetadataZipAsync(
                tenantId,
                buildAuthorization.Value!,
                cancellationToken);

            // Building the archive can take long enough for tenant membership,
            // feature availability, or manage permission to change. Re-check
            // immediately after materialization and discard the bytes when the
            // caller no longer has export authority.
            var deliveryAuthorization = await AuthorizeExportAsync(
                userId,
                tenantId,
                cancellationToken);
            if (!deliveryAuthorization.IsSuccess)
            {
                exportJob.Status = ExportJobStatus.Failed;
                exportJob.ErrorMessage = "Authorization changed before export delivery.";
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<TenantExportFileResponse>.Failure(
                    "Tenant export could not be completed.");
            }

            exportJob.Status = ExportJobStatus.Completed;
            exportJob.CompletedAt = clock.UtcNow;
            await auditLogger.LogUserActionAsync(
                userId,
                "TenantExportCreated",
                "ExportJob",
                exportJob.Id,
                "Tenant metadata export created.",
                new Dictionary<string, object?> { ["tenantId"] = tenantId, ["exportType"] = exportJob.ExportType.ToString() },
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<TenantExportFileResponse>.Success(new TenantExportFileResponse(
                exportJob.Id,
                tenantId,
                exportJob.Status,
                $"tenant-{tenantId:N}-metadata-export.zip",
                "application/zip",
                content,
                exportJob.CreatedAt,
                exportJob.CompletedAt));
        }
        catch
        {
            exportJob.Status = ExportJobStatus.Failed;
            exportJob.ErrorMessage = "Tenant export could not be completed.";
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TenantExportFileResponse>.Failure("Tenant export could not be completed.");
        }
    }

    public async Task<Result<TenantExportJobResponse>> GetJobAsync(Guid exportJobId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result<TenantExportJobResponse>.Failure("Authentication is required.");
        }

        var job = await exports.GetExportJobAsync(exportJobId, cancellationToken);
        if (job is null || job.ExportType != TenantExportType.Metadata)
        {
            return Result<TenantExportJobResponse>.Failure("Export job not found.");
        }

        var authorization = await AuthorizeExportAsync(userId, job.TenantId, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return Result<TenantExportJobResponse>.Failure("Export job not found.");
        }

        return Result<TenantExportJobResponse>.Success(ToResponse(job));
    }

    private async Task<Result<AuthorizationContext>> ReauthorizeExportBuildAsync(
        Guid userId,
        Guid tenantId,
        Guid exportJobId,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeExportAsync(userId, tenantId, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return Result<AuthorizationContext>.Failure(
                "Tenant export authorization could not be confirmed.");
        }

        return Result<AuthorizationContext>.Success(new AuthorizationContext(
            ActorId: userId,
            TenantId: tenantId,
            ModuleKey: "TenantExport",
            Purpose: "ExportBuild",
            RequestId: exportJobId.ToString("N"),
            AuthorizationState: RedactionAuthorizationState.Allowed,
            FieldAccessPolicy: FieldAccessPolicySnapshot.ThroughConfidential));
    }

    private async Task<Result> AuthorizeExportAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (await tenantAuthorization.IsPlatformAdminAsync(userId, cancellationToken))
        {
            return Result.Success();
        }

        if (!currentTenant.IsAvailable || currentTenant.TenantId != tenantId)
        {
            return Result.Failure("You are not allowed to export this tenant.");
        }

        var feature = await features.RequireEnabledAsync(FeatureKeys.TenantExport, cancellationToken);
        if (!feature.IsSuccess)
        {
            return feature;
        }

        return await tenantAuthorization.CanManageTenantAsync(userId, tenantId, cancellationToken)
            ? Result.Success()
            : Result.Failure("You are not allowed to export this tenant.");
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }

    private static TenantExportJobResponse ToResponse(ExportJob job)
    {
        return new TenantExportJobResponse(
            job.Id,
            job.TenantId,
            job.Status,
            job.ExportType,
            job.FileObjectId,
            job.CreatedAt,
            job.CompletedAt,
            job.ErrorMessage);
    }
}
