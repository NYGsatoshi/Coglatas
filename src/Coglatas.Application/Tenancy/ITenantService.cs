using Coglatas.Application.Common;

namespace Coglatas.Application.Tenancy;

public interface ITenantService
{
    Task<Result<IReadOnlyList<TenantResponse>>> ListPlatformTenantsAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantResponse>> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);

    Task<Result<TenantResponse>> GetPlatformTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Result<TenantResponse>> UpdateTenantAsync(Guid tenantId, UpdateTenantRequest request, CancellationToken cancellationToken = default);

    Task<Result> SuspendTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Result> ActivateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Result> ArchiveTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Result<CurrentTenantResponse>> GetCurrentTenantAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<TenantResponse>>> ListMyTenantsAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantResponse>> SwitchTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<TenantUserResponse>>> ListCurrentTenantUsersAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantUserResponse>> AddCurrentTenantUserAsync(AddTenantUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<TenantUserResponse>> UpdateCurrentTenantUserAsync(Guid userId, UpdateTenantUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> RemoveCurrentTenantUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
