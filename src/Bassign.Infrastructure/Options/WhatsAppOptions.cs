using Bassign.Domain.Messaging;

namespace Bassign.Infrastructure.Options;

/// <summary>
/// WhatsApp Business Platform (docs/whatsapp.md). Admins choose the provider in Settings → WhatsApp (step 16b); a
/// server environment variable (e.g. WhatsApp__ApiKey) overrides and locks a value. Read live (IOptionsMonitor).
/// </summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";
    public const int MinWebhookTokenLength = 24;

    /// <summary>"none" (default), "360dialog", "meta" (Cloud API direct), "twilio", or "simulator" (development).</summary>
    public string Provider { get; set; } = "none";

    /// <summary>The organization's WhatsApp Business number in E.164, e.g. +263771234567.</summary>
    public string? BusinessNumber { get; set; }

    /// <summary>360dialog D360-API-KEY, or the Meta system-user access token (secret).</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://waba-v2.360dialog.io/";

    /// <summary>Secret path segment of the webhook URL. Generated from Settings.</summary>
    public string WebhookToken { get; set; } = string.Empty;

    /// <summary>Meta/360dialog: when set, POSTs must carry a valid X-Hub-Signature-256 (HMAC-SHA256 of the raw body).</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Meta: answers the GET hub.challenge handshake when hub.verify_token matches. Generated from Settings.</summary>
    public string VerifyToken { get; set; } = string.Empty;

    public string TemplateLanguage { get; set; } = "en";

    public MetaSettings Meta { get; set; } = new();
    public TwilioSettings Twilio { get; set; } = new();

    public string Normalized => (Provider ?? "none").Trim().ToLowerInvariant();
    public bool IsSimulator => Normalized == "simulator";
    public bool IsDisabled => Normalized is "none" or "";
    public bool IsTwilio => Normalized == "twilio";
    /// <summary>Providers that deliver Meta's webhook format to /webhooks/whatsapp/{token}.</summary>
    public bool UsesCloudWebhook => Normalized is "360dialog" or "meta" or "simulator";

    /// <summary>What still has to be filled in for the chosen provider (empty = ready). While anything is missing,
    /// WhatsApp is treated as off and everyone gets email.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        void Need(bool ok, string what) { if (!ok) missing.Add(what); }
        if (IsDisabled || IsSimulator) return missing;

        Need(PhoneNumbers.IsE164(BusinessNumber), "Business number (+263…)");
        Need(WebhookToken.Length >= MinWebhookTokenLength, "Webhook address (generate it)");
        switch (Normalized)
        {
            case "360dialog":
                Need(!string.IsNullOrWhiteSpace(ApiKey), "360dialog API key");
                break;
            case "meta":
                Need(!string.IsNullOrWhiteSpace(ApiKey), "Access token");
                Need(!string.IsNullOrWhiteSpace(Meta.PhoneNumberId), "Phone number ID");
                Need(!string.IsNullOrWhiteSpace(AppSecret), "App secret");
                Need(!string.IsNullOrWhiteSpace(VerifyToken), "Verify token (generate it)");
                break;
            case "twilio":
                Need(!string.IsNullOrWhiteSpace(Twilio.AccountSid), "Account SID");
                Need(!string.IsNullOrWhiteSpace(Twilio.AuthToken), "Auth token");
                break;
            default:
                missing.Add($"A supported provider (not '{Provider}')");
                break;
        }
        return missing;
    }

    public bool IsReady => !IsDisabled && Missing().Count == 0;
}

public sealed class MetaSettings
{
    /// <summary>From WhatsApp Manager → API setup (not the phone number itself).</summary>
    public string PhoneNumberId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v21.0";
}

public sealed class TwilioSettings
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>Approved Content template SIDs (HX…) by Bassign template name: task_assigned, task_reminder,
    /// task_overdue, daily_digest. Needed to message people outside the 24-hour window.</summary>
    public Dictionary<string, string> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Public addresses of this install (server settings, set at deploy).</summary>
public sealed class PublicOptions
{
    public const string Section = "Public";

    /// <summary>The API's public https address, e.g. https://api.client.co.zw — used in webhook URLs and the phone connect code.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
}
