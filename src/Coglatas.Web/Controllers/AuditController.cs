using Coglatas.Application.Audit;
using Coglatas.Application.Common;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
public sealed class AuditController(
    IAuditQueryService audit,
    IAuditAuthorizationService? auditAuthorization = null) : ControllerBase
{
    private const int AdminAuditGridDefaultPageSize = 100;

    private static readonly FieldAccessPolicySnapshot AuditViewerFieldAccessPolicy =
        FieldAccessPolicySnapshot.ThroughConfidential;

    private static readonly FieldAccessPolicySnapshot AuditSensitiveFieldAccessPolicy =
        FieldAccessPolicySnapshot.ThroughRestrictedFields(
            "metadata",
            "metadataJson",
            "details",
            "ipAddress",
            "userAgent");

    [HttpGet("api/audit/capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken cancellationToken)
    {
        var capabilities = auditAuthorization is null
            ? new AuditCapabilityResponse(false, false, false, false, false)
            : await auditAuthorization.GetCapabilitiesAsync(cancellationToken);
        return Ok(capabilities);
    }

    [HttpGet("api/audit-logs")]
    public async Task<IActionResult> AuditLogs([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        return await ToActionResultAsync(
            await audit.ListAuditLogsAsync(query, cancellationToken),
            "AuditLogs",
            cancellationToken);
    }

    [HttpGet("api/tenant/audit-logs")]
    public async Task<IActionResult> TenantAuditLogs([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        return await ToActionResultAsync(
            await audit.ListAuditLogsAsync(query, cancellationToken),
            "AuditLogs",
            cancellationToken);
    }

    [HttpGet("api/platform/audit-logs")]
    [Authorize(Roles = "PlatformAdmin,SystemAdmin")]
    public async Task<IActionResult> PlatformAuditLogs([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        return await ToActionResultAsync(
            await audit.ListAuditLogsAsync(query, cancellationToken),
            "AuditLogs",
            cancellationToken);
    }

    [HttpGet("api/admin/audit-grid")]
    public async Task<IActionResult> AdminAuditGrid([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        var effectiveQuery = Request.Query.ContainsKey(nameof(AuditLogQuery.PageSize))
            ? query
            : query with { PageSize = AdminAuditGridDefaultPageSize };

        return await ToActionResultAsync(
            await audit.ListAuditGridAsync(effectiveQuery, cancellationToken),
            "AuditGrid",
            cancellationToken);
    }

    [HttpGet("api/admin/audit-grid/{auditId}")]
    public async Task<IActionResult> AdminAuditGridRow(
        string auditId,
        CancellationToken cancellationToken)
    {
        // Route parsing must not turn a malformed URL marker into a distinct
        // observable outcome. The service authorizes first, then uses the
        // same generic not-found result for invalid, absent, and cross-tenant
        // identifiers.
        var parsedAuditId = Guid.TryParse(auditId, out var value) ? value : Guid.Empty;
        return await ToActionResultAsync(
            await audit.GetAuditGridRowAsync(parsedAuditId, cancellationToken),
            "AuditGrid",
            cancellationToken);
    }

    [HttpGet("api/admin/audit-grid/{auditId}/sensitive-metadata")]
    public async Task<IActionResult> AdminAuditSensitiveMetadata(
        string auditId,
        CancellationToken cancellationToken)
    {
        // Keep malformed, absent, and cross-Tenant identifiers on the same
        // authorized exact-event path. The service checks audit.view and the
        // independent sensitive-metadata capability before querying the ID.
        var parsedAuditId = Guid.TryParse(auditId, out var value) ? value : Guid.Empty;
        return await ToActionResultAsync(
            await audit.GetAuditSensitiveMetadataAsync(parsedAuditId, cancellationToken),
            "AuditSensitiveMetadata",
            cancellationToken);
    }

    [HttpGet("api/security-events")]
    public async Task<IActionResult> SecurityEvents([FromQuery] SecurityEventQuery query, CancellationToken cancellationToken)
    {
        return await ToActionResultAsync(
            await audit.ListSecurityEventsAsync(query, cancellationToken),
            "SecurityEvents",
            cancellationToken);
    }

    [HttpGet("api/tenant/security-events")]
    public async Task<IActionResult> TenantSecurityEvents([FromQuery] SecurityEventQuery query, CancellationToken cancellationToken)
    {
        return await ToActionResultAsync(
            await audit.ListSecurityEventsAsync(query, cancellationToken),
            "SecurityEvents",
            cancellationToken);
    }

    [HttpGet("api/platform/security-events")]
    [Authorize(Roles = "PlatformAdmin,SystemAdmin")]
    public async Task<IActionResult> PlatformSecurityEvents([FromQuery] SecurityEventQuery query, CancellationToken cancellationToken)
    {
        return await ToActionResultAsync(
            await audit.ListSecurityEventsAsync(query, cancellationToken),
            "SecurityEvents",
            cancellationToken);
    }

    private async Task<IActionResult> ToActionResultAsync<T>(
        Result<T> result,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            var fieldAccessPolicy = await GetAuditFieldAccessPolicyAsync(cancellationToken);
            return Ok(CanonicalRedactionProjection.Apply(
                HttpContext,
                result.Value!,
                RedactionProfile.AuditDisplay,
                moduleKey,
                RedactionAuthorizationState.Allowed,
                RedactionPurpose.SecurityAuditLite,
                fieldAccessPolicy: fieldAccessPolicy));
        }

        var status = result.ErrorDetail?.Code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "CapabilityDenied" or "TenantMembershipRequired" => StatusCodes.Status403Forbidden,
            "AuditEventNotFound" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(status, CanonicalErrorEnvelope.FromSensitiveResult(
            HttpContext,
            status,
            result.ErrorDetail,
            result.Error,
            "AuditQueryFailed"));
    }

    private async Task<FieldAccessPolicySnapshot> GetAuditFieldAccessPolicyAsync(
        CancellationToken cancellationToken)
    {
        if (auditAuthorization is null)
        {
            return AuditViewerFieldAccessPolicy;
        }

        var capabilities = await auditAuthorization.GetCapabilitiesAsync(cancellationToken);
        return capabilities.CanViewSensitiveMetadata
            ? AuditSensitiveFieldAccessPolicy
            : AuditViewerFieldAccessPolicy;
    }
}
