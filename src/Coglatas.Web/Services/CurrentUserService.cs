using System.Security.Claims;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Web.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? UserId => TryGetGuid(ClaimTypes.NameIdentifier);

    public Guid? SessionId => TryGetGuid("session_id");

    public string? Email => User?.FindFirstValue(ClaimTypes.Email);

    public SystemRole? SystemRole
    {
        get
        {
            var value = User?.FindFirstValue("system_role") ?? User?.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<SystemRole>(value, out var role) ? role : null;
        }
    }

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    private Guid? TryGetGuid(string claimType)
    {
        var value = User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
