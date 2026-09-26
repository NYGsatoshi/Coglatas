using Coglatas.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/me/message-notification-preferences")]
public sealed class MessageNotificationPreferencesController(IMessageNotificationPreferenceService preferences) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await preferences.GetAsync(cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpPatch]
    public async Task<IActionResult> Update(
        [FromBody] UpdateMessageNotificationPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await preferences.UpdateAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }
}
