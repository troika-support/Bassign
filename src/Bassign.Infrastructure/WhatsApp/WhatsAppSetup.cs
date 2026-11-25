using Bassign.Application.Messaging.Setup;
using Bassign.Domain.Messaging;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.WhatsApp;

public sealed class WhatsAppSetup(IOptionsMonitor<WhatsAppOptions> options, IOptionsMonitor<PublicOptions> publicUrls) : IWhatsAppSetup
{
    public WhatsAppSetupStatus Current()
    {
        var o = options.CurrentValue;
        var api = publicUrls.CurrentValue.ApiBaseUrl;
        var known = Uri.TryCreate(api, UriKind.Absolute, out _);
        return new WhatsAppSetupStatus(
            o.Normalized, !o.IsDisabled, o.IsReady, o.Missing(),
            PhoneNumbers.IsE164(o.BusinessNumber) ? o.BusinessNumber : null,
            o.WebhookToken.Length >= WhatsAppOptions.MinWebhookTokenLength,
            known ? $"{api.TrimEnd('/')}/webhooks/{(o.IsTwilio ? "twilio" : "whatsapp")}/••••••••" : null,
            known);
    }

    public string? WebhookUrl(string provider, string token)
    {
        var api = publicUrls.CurrentValue.ApiBaseUrl;
        if (!Uri.TryCreate(api, UriKind.Absolute, out _)) return null;
        return $"{api.TrimEnd('/')}/webhooks/{(provider == "twilio" ? "twilio" : "whatsapp")}/{token}";
    }
}
