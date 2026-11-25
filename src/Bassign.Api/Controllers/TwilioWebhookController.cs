using Bassign.Infrastructure.Hosting;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Bassign.Api.Controllers;

/// <summary>
/// Twilio WhatsApp webhook: incoming messages AND status callbacks. Secret path token + X-Twilio-Signature
/// (HMAC-SHA1 with the Auth token). Each post is turned into the same Cloud-API shape the inbox already
/// understands, so dedupe (by MessageSid), the worker and the conversation logic are shared.
/// </summary>
[ApiController]
[Route("webhooks/twilio/{token}")]
[AllowAnonymous]
[EnableRateLimiting(Hardening.WebhookPolicy)]
public sealed class TwilioWebhookController(
    IOptionsMonitor<WhatsAppOptions> options,
    WhatsAppWebhookIntake intake,
    InboxSignal signal,
    TimeProvider clock,
    ILogger<TwilioWebhookController> logger) : ControllerBase
{
    private const string EmptyTwiml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>";

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(200_000)]
    public async Task<IActionResult> Receive(string token, CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!o.IsTwilio || !WebhookSignature.TokenMatches(o.WebhookToken, token))
            return NotFound();

        var form = await Request.ReadFormAsync(ct);
        var fields = form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value.ToString())).ToList();
        // Twilio signs the exact URL it called: scheme + host + path + query, as configured in the Twilio console.
        var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}{Request.QueryString}";
        if (!TwilioSignature.IsValid(o.Twilio.AuthToken, url, fields, Request.Headers[TwilioSignature.Header]))
        {
            logger.LogWarning("Twilio webhook rejected: bad signature.");
            return Unauthorized();
        }

        var sid = form["MessageSid"].ToString();
        if (string.IsNullOrEmpty(sid)) return Content(EmptyTwiml, "text/xml");

        string body;
        var status = form["MessageStatus"].ToString();
        if (!string.IsNullOrEmpty(status) && string.IsNullOrEmpty(form["Body"]) && form["NumMedia"].ToString() is "" or "0")
        {
            body = CloudApiWebhook.BuildStatus(sid, status);   // delivered / read / failed / undelivered …
        }
        else
        {
            var from = form["From"].ToString().Replace("whatsapp:", "", StringComparison.OrdinalIgnoreCase);
            var mediaUrl = form["NumMedia"].ToString() is { Length: > 0 } n && n != "0" ? form["MediaUrl0"].ToString() : null;
            // Template quick replies arrive as ButtonText/ButtonPayload; the conversation matches the text to the
            // last menu we sent (so per-task ids aren't needed in Twilio templates).
            var text = form["ButtonText"].ToString() is { Length: > 0 } button ? button : form["Body"].ToString();
            body = CloudApiWebhook.BuildInbound(sid, from, clock.GetUtcNow(), string.IsNullOrEmpty(text) ? null : text, null, null, mediaUrl);
        }

        if (await intake.AcceptAsync(body, ct) > 0) signal.Notify();
        return Content(EmptyTwiml, "text/xml");
    }
}
