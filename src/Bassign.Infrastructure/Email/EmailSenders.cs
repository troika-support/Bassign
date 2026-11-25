using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using Bassign.Application.Common.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Bassign.Infrastructure.Email;

/// <summary>One way of sending. The router picks the one matching Email:Provider at send time.</summary>
public interface IEmailTransport : IEmailSender
{
    bool Handles(string provider);
}

/// <summary>The IEmailSender the app uses: routes each message to the provider configured right now.</summary>
public sealed class RoutingEmailSender(IEnumerable<IEmailTransport> transports, IOptionsMonitor<EmailOptions> options) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        if (o.IsDisabled) throw new EmailSendException("Email is turned off (Settings → Email).", transient: false);
        if (o.Missing() is { Count: > 0 } missing) throw new EmailSendException($"Email isn't fully set up: {string.Join(", ", missing)}.", transient: false);

        var transport = transports.FirstOrDefault(t => t.Handles(o.Normalized))
            ?? throw new EmailSendException($"Unknown email provider '{o.Provider}'.", transient: false);
        return transport.SendAsync(message, cancellationToken);
    }
}

/// <summary>SMTP — Microsoft 365 / Google Workspace / cPanel hosts, and the Gmail preset — via MailKit.</summary>
public sealed class SmtpEmailTransport(IOptionsMonitor<EmailOptions> options) : IEmailTransport
{
    public bool Handles(string provider) => provider is "smtp" or "gmail";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        var smtp = o.EffectiveSmtp();

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 30_000 };
        try
        {
            var security = smtp.Security?.ToLowerInvariant() switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "none" => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls
            };
            await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);
            if (!string.IsNullOrWhiteSpace(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (AuthenticationException ex)
        {
            throw new EmailSendException(o.Normalized == "gmail"
                ? "Gmail refused the sign-in. Use an app password (2-Step Verification must be on), not the normal password."
                : "The mail server refused the sign-in. Check the username and password.", transient: false, ex);
        }
        catch (SmtpCommandException ex)
        {
            var code = (int)ex.StatusCode;
            throw new EmailSendException($"The mail server rejected the message ({code}): {ex.Message}", transient: code is >= 400 and < 500, ex);
        }
        catch (Exception ex) when (ex is SocketException or IOException or SmtpProtocolException or ServiceNotConnectedException or SslHandshakeException)
        {
            throw new EmailSendException($"Couldn't reach the mail server {smtp.Host}:{smtp.Port}.", transient: true, ex);
        }
    }
}

/// <summary>SendGrid v3 Mail Send. 202 = accepted; 429/5xx are retried by the outbox.</summary>
public sealed class SendGridEmailTransport(HttpClient http, IOptionsMonitor<EmailOptions> options) : IEmailTransport
{
    public bool Handles(string provider) => provider == "sendgrid";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        var body = new
        {
            personalizations = new[] { new { to = new[] { new { email = message.ToAddress, name = message.ToName } } } },
            from = new { email = o.FromAddress, name = o.FromName },
            subject = message.Subject,
            content = new[] { new { type = "text/plain", value = message.Text }, new { type = "text/html", value = message.Html } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.ApiKey);

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException ex) { throw new EmailSendException("Couldn't reach SendGrid.", transient: true, ex); }

        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            var transient = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            throw new EmailSendException(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "SendGrid refused the API key (or the From address isn't a verified sender)."
                : $"SendGrid returned {(int)response.StatusCode}.", transient);
        }
    }
}

/// <summary>Microsoft 365 via Microsoft Graph (client credentials + /users/{sender}/sendMail).</summary>
public sealed class GraphEmailTransport(HttpClient http, IOptionsMonitor<EmailOptions> options, GraphTokenCache tokens) : IEmailTransport
{
    public bool Handles(string provider) => provider == "m365";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var g = options.CurrentValue.Graph;
        var token = await tokens.GetAsync(http, g, cancellationToken);
        var body = new
        {
            message = new
            {
                subject = message.Subject,
                body = new { contentType = "HTML", content = message.Html },
                toRecipients = new[] { new { emailAddress = new { address = message.ToAddress, name = message.ToName } } }
            },
            saveToSentItems = false
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(g.Sender)}/sendMail")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException ex) { throw new EmailSendException("Couldn't reach Microsoft 365.", transient: true, ex); }

        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Forget(g);
            var transient = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            throw new EmailSendException(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? "Microsoft 365 refused: check the app has the Mail.Send application permission (admin-consented) and the sending mailbox exists."
                : $"Microsoft 365 returned {(int)response.StatusCode}.", transient);
        }
    }
}

/// <summary>Caches the Graph access token (≈1 h) per tenant/app so each email doesn't sign in again.</summary>
public sealed class GraphTokenCache(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new();

    private static string Key(GraphSettings g) => $"{g.TenantId}|{g.ClientId}|{g.ClientSecret.GetHashCode(StringComparison.Ordinal)}";

    public void Forget(GraphSettings g) => _tokens.TryRemove(Key(g), out _);

    public async Task<string> GetAsync(HttpClient http, GraphSettings g, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(Key(g), out var cached) && cached.Expires > clock.GetUtcNow().AddMinutes(2))
            return cached.Token;

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = g.ClientId,
            ["client_secret"] = g.ClientSecret,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials"
        });
        HttpResponseMessage response;
        try { response = await http.PostAsync($"https://login.microsoftonline.com/{Uri.EscapeDataString(g.TenantId)}/oauth2/v2.0/token", form, cancellationToken); }
        catch (HttpRequestException ex) { throw new EmailSendException("Couldn't reach Microsoft sign-in.", transient: true, ex); }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new EmailSendException("Microsoft 365 sign-in failed: check the tenant ID, client ID and client secret (secrets expire).",
                    transient: (int)response.StatusCode >= 500);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new EmailSendException("Microsoft 365 sign-in returned nothing.", transient: true);
            _tokens[Key(g)] = (token.AccessToken, clock.GetUtcNow().AddSeconds(token.ExpiresIn));
            return token.AccessToken;
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Development: writes each email as an .eml file (double-click to open in Outlook / Mail).</summary>
public sealed class PickupFolderEmailTransport(IOptionsMonitor<EmailOptions> options, TimeProvider clock, ILogger<PickupFolderEmailTransport> logger) : IEmailTransport
{
    public bool Handles(string provider) => provider == "pickup";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        var dir = o.ResolvePickupDirectory();
        Directory.CreateDirectory(dir);
        var now = clock.GetUtcNow();
        var safeTo = string.Concat(message.ToAddress.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(dir, $"{now:yyyyMMdd-HHmmss}-{safeTo}-{Guid.NewGuid():N}"[..48] + ".eml");

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, string.IsNullOrWhiteSpace(o.FromAddress) ? "no-reply@bassign.local" : o.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Date = now;
        mime.Headers.Add("X-Unsent", "1");
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();
        await mime.WriteToAsync(path, cancellationToken);
        logger.LogInformation("Email to {To} written to {Path}", Bassign.Domain.Common.Pii.MaskEmail(message.ToAddress), path);
    }
}
