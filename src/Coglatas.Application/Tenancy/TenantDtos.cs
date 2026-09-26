using Coglatas.Domain.Enums;

namespace Coglatas.Application.Tenancy;

public sealed record TenantResponse(
    Guid Id,
    string Name,
    string Slug,
    string DisplayName,
    string? PrimaryDomain,
    TenantStatus Status,
    string? PlanId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record CreateTenantRequest(
    string Name,
    string Slug,
    string? DisplayName,
    string? PrimaryDomain,
    string? PlanId);

public sealed record UpdateTenantRequest(
    string? Name,
    string? DisplayName,
    string? PrimaryDomain,
    string? PlanId);

public sealed record TenantUserResponse(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string DisplayName,
    string Email,
    TenantUserRole Role,
    TenantUserStatus Status,
    DateTimeOffset JoinedAt,
    Guid? InvitedByUserId);

public sealed record AddTenantUserRequest(
    Guid UserId,
    TenantUserRole Role);

public sealed record UpdateTenantUserRequest(
    TenantUserRole? Role,
    TenantUserStatus? Status);

public sealed record SwitchTenantRequest(Guid TenantId);

public sealed record CurrentTenantResponse(
    Guid TenantId,
    string? TenantSlug,
    bool IsAvailable,
    bool IsPlatformScope,
    string? DisplayName,
    TenantStatus? Status,
    TenantUserRole? CurrentUserRole,
    AppMode AppMode,
    bool AllowTenantSwitching);
