using System.Text;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bassign.Api.Controllers;

/// <summary>
/// WhatsApp webhook (docs/whatsapp.md). Authenticated by a secret path token (constant-time compare) and,
/// when WhatsApp:AppSecret is set, an X-Hub-Signature-256 HMAC of the raw body. It only persists and acks:
/// the inbox worker does the work, so the provider never waits on our database or our business rules.
/// </summary>
[ApiController]
[Route("webhooks/whatsapp/{token}")]
[AllowAnonymous]
public sealed class WhatsAppWebhookController(
    IOptions<WhatsAppOptions> options,
    WhatsAppWebhookIntake intake,
    InboxSignal signal,
    ILogger<WhatsAppWebhookController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 1_000_000;

    /// <summary>Subscription handshake: echo hub.challenge when hub.verify_token matches.</summary>
    [HttpGet]
    public IActionResult Verify(
        string token,
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var o = options.Value;
        if (!WebhookSignature.TokenMatches(o.WebhookToken, token)) return NotFound();
        if (mode == "subscribe" && challenge is not null && WebhookSignature.TokenMatches(o.VerifyToken, verifyToken))
            return Content(challenge, "text/plain");
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(string token, CancellationToken ct)
    {
        var o = options.Value;
        if (!WebhookSignature.TokenMatches(o.WebhookToken, token))
            return NotFound();   // don't confirm the endpoint exists

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var raw = buffer.ToArray();

        if (!string.IsNullOrEmpty(o.AppSecret) &&
            !WebhookSignature.IsValid(o.AppSecret, raw, Request.Headers[WebhookSignature.Header]))
        {
            logger.LogWarning("WhatsApp webhook rejected: bad signature.");
            return Unauthorized();
        }

        var added = await intake.AcceptAsync(Encoding.UTF8.GetString(raw), ct);
        if (added > 0) signal.Notify();
        return Ok();
    }
}
