using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Coglatas.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Coglatas.Web.Controllers;

[ApiController]
[Route("api/invites")]
public sealed class InvitesController(IAuthService authService) : ControllerBase
{
    [HttpGet("validate")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(
        [FromQuery, Required, RegularExpression("^[a-f0-9]{64}$")] string token,
        CancellationToken cancellationToken)
    {
        var result = await authService.ValidateInviteAsync(token, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : InviteProblem(result.Error, "Invite validation failed.");
    }

    [HttpPost("accept")]
    [EnableRateLimiting("invite")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoginResponse>> Accept(AcceptInviteRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.AcceptInviteAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return InviteProblem(result.Error, "Invite acceptance failed.");
        }

        await SignInAsync(result.Value);
        return Ok(result.Value);
    }

    private ObjectResult InviteProblem(string? detail, string title)
    {
        return Problem(
            title: title,
            detail: string.IsNullOrWhiteSpace(detail) ? "Invite is invalid." : detail,
            statusCode: StatusCodes.Status404NotFound);
    }

    private Task SignInAsync(LoginResponse user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.SystemRole.ToString()),
            new("system_role", user.SystemRole.ToString()),
            new("session_id", user.SessionId.ToString())
        };
        claims.AddRange(user.Capabilities.Select(capability => new Claim("capability", capability)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = user.ExpiresAt
        };

        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }
}
