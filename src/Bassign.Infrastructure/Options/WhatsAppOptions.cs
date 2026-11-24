namespace Bassign.Infrastructure.Options;

/// <summary>WhatsApp Business Platform settings (docs/whatsapp.md). Secrets come from user-secrets /
/// environment variables (WhatsApp__ApiKey, WhatsApp__WebhookToken, WhatsApp__AppSecret) — never source.</summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    /// <summary>"simulator" (development default, no external calls) or "360dialog".</summary>
    public string Provider { get; set; } = "simulator";

    /// <summary>The organization's WhatsApp Business number in E.164, e.g. +263771234567.</summary>
    public string? BusinessNumber { get; set; }

    /// <summary>360dialog D360-API-KEY (secret).</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://waba-v2.360dialog.io/";

    /// <summary>Secret path segment: the webhook URL is /webhooks/whatsapp/{WebhookToken}.</summary>
    public string WebhookToken { get; set; } = string.Empty;

    /// <summary>Optional: when set, POSTs must carry a valid X-Hub-Signature-256 (HMAC-SHA256 of the raw body).</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Optional: answers the GET hub.challenge handshake when hub.verify_token matches.</summary>
    public string VerifyToken { get; set; } = string.Empty;

    public string TemplateLanguage { get; set; } = "en";

    public bool IsSimulator => string.Equals(Provider, "simulator", StringComparison.OrdinalIgnoreCase);
}
