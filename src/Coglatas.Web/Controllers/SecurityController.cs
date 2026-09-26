using Coglatas.Web.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Route("api/security")]
public sealed class SecurityController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf-token")]
    [AllowAnonymous]
    public ActionResult<CsrfTokenResponse> CsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(new CsrfTokenResponse(tokens.RequestToken ?? string.Empty, SecurityOptions.CsrfHeaderName));
    }
}

public sealed record CsrfTokenResponse(string Token, string HeaderName);
