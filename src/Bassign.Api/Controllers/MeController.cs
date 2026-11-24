using Bassign.Application.Messaging.GetMyWhatsApp;
using Bassign.Application.Messaging.StartLink;
using Bassign.Application.Messaging.Unlink;
using Bassign.Application.Privacy;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bassign.Api.Controllers;

/// <summary>The signed-in person's own settings — used by the phone app's "Link WhatsApp".</summary>
[ApiController]
[Route("me")]
[Authorize]
public sealed class MeController(ISender sender) : ControllerBase
{
    [HttpGet("whatsapp")]
    public async Task<IActionResult> GetWhatsApp(CancellationToken ct) =>
        Ok(await sender.Send(new GetMyWhatsAppQuery(), ct));

    /// <summary>Issues a fresh 6-digit code (valid 15 minutes) to send to the business number as "LINK 123456".</summary>
    [HttpPost("whatsapp")]
    public async Task<IActionResult> StartWhatsAppLink(CancellationToken ct)
    {
        var result = await sender.Send(new StartWhatsAppLinkCommand(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error.Message, type: result.Error.Code);
    }

    [HttpDelete("whatsapp")]
    public async Task<IActionResult> UnlinkWhatsApp(CancellationToken ct)
    {
        var result = await sender.Send(new UnlinkWhatsAppCommand(), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error.Message, type: result.Error.Code);
    }

    /// <summary>Right of access / portability: everything Bassign holds about you, as JSON.</summary>
    [HttpGet("data")]
    public async Task<IActionResult> MyData(CancellationToken ct) =>
        await sender.Send(new ExportMyDataQuery(), ct) is { } export ? Ok(export) : NotFound();

    /// <summary>Right to rectification: correct your display name.</summary>
    [HttpPut("name")]
    public async Task<IActionResult> UpdateName([FromBody] UpdateNameRequest body, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateMyNameCommand(body.DisplayName), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error.Message, type: result.Error.Code);
    }
}

public sealed record UpdateNameRequest(string DisplayName);
