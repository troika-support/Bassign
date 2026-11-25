using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bassign.Application.Media;
using Bassign.Application.Messaging;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>One WhatsApp provider. The router picks the one matching WhatsApp:Provider at send time.</summary>
public interface IWhatsAppTransport : IWhatsAppClient
{
    bool Handles(string provider);
}

/// <summary>The IWhatsAppClient the app uses: the provider configured right now (Settings → WhatsApp).</summary>
public sealed class RoutingWhatsAppClient(IEnumerable<IWhatsAppTransport> transports, IOptionsMonitor<WhatsAppOptions> options) : IWhatsAppClient
{
    private IWhatsAppTransport Current()
    {
        var o = options.CurrentValue;
        if (o.IsDisabled) throw new WhatsAppSendException("WhatsApp is turned off (Settings → WhatsApp).", transient: false);
        if (o.Missing() is { Count: > 0 } missing) throw new WhatsAppSendException($"WhatsApp isn't fully set up: {string.Join(", ", missing)}.", transient: false);
        return transports.FirstOrDefault(t => t.Handles(o.Normalized))
            ?? throw new WhatsAppSendException($"Unknown WhatsApp provider '{o.Provider}'.", transient: false);
    }

    public Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default) =>
        Current().SendAsync(message, cancellationToken);

    public Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default) =>
        Current().DownloadMediaAsync(mediaId, cancellationToken);
}

/// <summary>Shared by 360dialog and Meta direct: same JSON, different host and credentials.</summary>
internal static class CloudApiHttp
{
    public static async Task<string> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException ex) { throw new WhatsAppSendException("Couldn't reach WhatsApp.", transient: true, ex); }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) { throw new WhatsAppSendException("WhatsApp timed out.", transient: true, ex); }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                throw new WhatsAppSendException(Explain(response.StatusCode, body), transient);
            }
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString()
                   ?? throw new WhatsAppSendException("WhatsApp returned no message id.", transient: false);
        }
    }

    /// <summary>The common Cloud API error codes, in words an Admin can act on.</summary>
    public static string Explain(HttpStatusCode status, string body)
    {
        var code = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) && c.TryGetInt32(out var n)) code = n;
        }
        catch (JsonException) { /* not JSON */ }

        return code switch
        {
            131047 => "Outside the 24-hour window: the person must message the business number first (or an approved template is needed).",
            132000 or 132001 => "The message template isn't approved (or its name/language doesn't match) in WhatsApp Manager.",
            131026 => "That number can't receive WhatsApp messages.",
            190 => "The access token is invalid or expired — create a new one and save it in Settings.",
            100 => "WhatsApp rejected a parameter — check the phone number ID.",
            _ => $"WhatsApp rejected the message ({(int)status}{(code > 0 ? $", code {code}" : "")})."
        };
    }

    public static async Task<(byte[] Content, string ContentType)?> DownloadAsync(HttpClient http, Func<HttpRequestMessage> request, CancellationToken cancellationToken)
    {
        using var req = request();
        using var file = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!file.IsSuccessStatusCode || file.Content.Headers.ContentLength > ProofReferences.MaxBytes) return null;
        var bytes = await file.Content.ReadAsByteArrayAsync(cancellationToken);
        var mime = file.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return bytes.Length > ProofReferences.MaxBytes ? null : (bytes, mime);
    }
}

/// <summary>360dialog (Cloud API v2): D360-API-KEY header; media fetched back through 360dialog's host.</summary>
public sealed class Dialog360Transport(HttpClient http, IOptionsMonitor<WhatsAppOptions> options) : IWhatsAppTransport
{
    public bool Handles(string provider) => provider == "360dialog";

    private HttpRequestMessage Request(HttpMethod method, Uri uri, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Add("D360-API-KEY", options.CurrentValue.ApiKey);
        return request;
    }

    private Uri Base => new(options.CurrentValue.BaseUrl.EndsWith('/') ? options.CurrentValue.BaseUrl : options.CurrentValue.BaseUrl + "/");

    public async Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Post, new Uri(Base, "messages"), JsonContent.Create(CloudApiPayloads.Build(message), options: CloudApiPayloads.Json));
        return await CloudApiHttp.SendAsync(http, request, cancellationToken);
    }

    public async Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        using var meta = await http.SendAsync(Request(HttpMethod.Get, new Uri(Base, Uri.EscapeDataString(mediaId))), cancellationToken);
        if (!meta.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await meta.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("url", out var u) || u.GetString() is not { } url) return null;
        var proxied = new Uri(Base, new Uri(url).PathAndQuery.TrimStart('/'));
        return await CloudApiHttp.DownloadAsync(http, () => Request(HttpMethod.Get, proxied), cancellationToken);
    }
}

/// <summary>Meta WhatsApp Cloud API direct (graph.facebook.com), bearer system-user token.</summary>
public sealed class MetaCloudTransport(HttpClient http, IOptionsMonitor<WhatsAppOptions> options) : IWhatsAppTransport
{
    public bool Handles(string provider) => provider == "meta";

    private HttpRequestMessage Request(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.CurrentValue.ApiKey);
        return request;
    }

    private string Graph => $"https://graph.facebook.com/{options.CurrentValue.Meta.ApiVersion}/";

    public async Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Post, $"{Graph}{Uri.EscapeDataString(options.CurrentValue.Meta.PhoneNumberId)}/messages",
            JsonContent.Create(CloudApiPayloads.Build(message), options: CloudApiPayloads.Json));
        return await CloudApiHttp.SendAsync(http, request, cancellationToken);
    }

    public async Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        using var meta = await http.SendAsync(Request(HttpMethod.Get, $"{Graph}{Uri.EscapeDataString(mediaId)}"), cancellationToken);
        if (!meta.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await meta.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("url", out var u) || u.GetString() is not { } url) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        return await CloudApiHttp.DownloadAsync(http, () => Request(HttpMethod.Get, url), cancellationToken);
    }
}

/// <summary>
/// Twilio WhatsApp. In the 24-hour window, buttons and lists are sent as a numbered text menu ("Reply 1 — Accept");
/// the reply is matched back to the menu (HandleInboundWhatsApp). Outside it, an approved Content template (HX… SID
/// from Settings) with numbered variables.
/// </summary>
public sealed class TwilioTransport(HttpClient http, IOptionsMonitor<WhatsAppOptions> options) : IWhatsAppTransport
{
    public bool Handles(string provider) => provider == "twilio";

    private HttpRequestMessage Request(HttpMethod method, string url, HttpContent? content = null)
    {
        var t = options.CurrentValue.Twilio;
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{t.AccountSid}:{t.AuthToken}")));
        return request;
    }

    public async Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        var form = new Dictionary<string, string>
        {
            ["From"] = $"whatsapp:{o.BusinessNumber}",
            ["To"] = $"whatsapp:{message.ToE164}"
        };
        if (message is WaTemplate template)
        {
            if (!o.Twilio.Templates.TryGetValue(template.Name, out var sid) || string.IsNullOrWhiteSpace(sid))
                throw new WhatsAppSendException($"The Twilio template for '{template.Name}' isn't set (Settings → WhatsApp).", transient: false);
            form["ContentSid"] = sid;
            form["ContentVariables"] = JsonSerializer.Serialize(
                template.BodyParameters.Select((p, i) => (Key: (i + 1).ToString(CultureInfo.InvariantCulture), p)).ToDictionary(x => x.Key, x => x.p));
        }
        else
        {
            form["Body"] = RenderText(message);
        }

        using var request = Request(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(o.Twilio.AccountSid)}/Messages.json",
            new FormUrlEncodedContent(form));
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException ex) { throw new WhatsAppSendException("Couldn't reach Twilio.", transient: true, ex); }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                throw new WhatsAppSendException(ExplainTwilio(response.StatusCode, body), transient);
            }
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("sid").GetString() ?? throw new WhatsAppSendException("Twilio returned no message id.", transient: false);
        }
    }

    /// <summary>Buttons and list rows as a numbered menu; the person replies with the number (or the word).</summary>
    public static string RenderText(WhatsAppOutbound message)
    {
        IReadOnlyList<string> options = message switch
        {
            WaButtons b => b.Buttons.Select(x => x.Title).ToList(),
            WaList l => l.Rows.Select(r => r.Description is null ? r.Title : $"{r.Title} ({r.Description})").ToList(),
            _ => []
        };
        if (options.Count == 0) return message.Body;
        var sb = new StringBuilder(message.Body).Append("\n\nReply with a number:");
        for (var i = 0; i < options.Count; i++) sb.Append('\n').Append(i + 1).Append(" — ").Append(options[i]);
        return sb.ToString();
    }

    private static string ExplainTwilio(HttpStatusCode status, string body)
    {
        var code = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var c) && c.TryGetInt32(out var n)) code = n;
        }
        catch (JsonException) { /* not JSON */ }
        return code switch
        {
            63016 => "Outside the 24-hour window: the person must message the business number first (or an approved template is needed).",
            63007 => "Twilio doesn't recognise the business number as a WhatsApp sender on this account.",
            20003 => "Twilio refused the Account SID / Auth token.",
            21211 or 21614 => "That number can't receive WhatsApp messages.",
            _ => $"Twilio rejected the message ({(int)status}{(code > 0 ? $", code {code}" : "")})."
        };
    }

    public async Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        // For Twilio the "media id" is the MediaUrl0 from the webhook; only fetch from Twilio's own host.
        if (!Uri.TryCreate(mediaId, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("api.twilio.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".twilio.com", StringComparison.OrdinalIgnoreCase)))
            return null;
        return await CloudApiHttp.DownloadAsync(http, () => Request(HttpMethod.Get, mediaId), cancellationToken);
    }
}
