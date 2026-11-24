using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bassign.Application.Common.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Email;

/// <summary>Development: writes each email as an .eml file (double-click to open in Outlook / Mail).</summary>
public sealed class PickupFolderEmailSender(IOptions<EmailOptions> options, TimeProvider clock, ILogger<PickupFolderEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var dir = o.ResolvePickupDirectory();
        Directory.CreateDirectory(dir);
        var now = clock.GetUtcNow();
        var safeTo = string.Concat(message.ToAddress.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(dir, $"{now:yyyyMMdd-HHmmss}-{safeTo}-{Guid.NewGuid():N}"[..48] + ".eml");
        var boundary = $"b-{Guid.NewGuid():N}";

        var eml = new StringBuilder()
            .Append("X-Unsent: 1\r\n")
            .Append("From: ").Append(Address(o.FromName, o.FromAddress)).Append("\r\n")
            .Append("To: ").Append(Address(message.ToName, message.ToAddress)).Append("\r\n")
            .Append("Subject: =?utf-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Subject))).Append("?=\r\n")
            .Append("Date: ").Append(now.ToString("r", System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("MIME-Version: 1.0\r\n")
            .Append("Content-Type: multipart/alternative; boundary=\"").Append(boundary).Append("\"\r\n\r\n")
            .Append("--").Append(boundary).Append("\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n")
            .Append(Base64Lines(message.Text)).Append("\r\n")
            .Append("--").Append(boundary).Append("\r\nContent-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n")
            .Append(Base64Lines(message.Html)).Append("\r\n")
            .Append("--").Append(boundary).Append("--\r\n");

        await File.WriteAllTextAsync(path, eml.ToString(), Encoding.ASCII, cancellationToken);
        logger.LogInformation("Email to {To} written to {Path}", message.ToAddress, path);
    }

    private static string Address(string? name, string address) =>
        string.IsNullOrWhiteSpace(name) ? address : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(name))}?= <{address}>";

    private static string Base64Lines(string text)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder();
        for (var i = 0; i < b64.Length; i += 76)
            sb.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");
        return sb.ToString();
    }
}

/// <summary>SendGrid v3 Mail Send (docs/decisions.md #2). 202 = accepted; 429/5xx are retried by the outbox.</summary>
public sealed class SendGridEmailSender(HttpClient http, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var body = new
        {
            personalizations = new[] { new { to = new[] { new { email = message.ToAddress, name = message.ToName } } } },
            from = new { email = o.FromAddress, name = o.FromName },
            subject = message.Subject,
            content = new[] { new { type = "text/plain", value = message.Text }, new { type = "text/html", value = message.Html } }
        };

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("v3/mail/send", body, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailSendException("Couldn't reach SendGrid.", transient: true, ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            var transient = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            throw new EmailSendException($"SendGrid returned {(int)response.StatusCode}.", transient);
        }
    }
}

public sealed class DisabledEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        throw new EmailSendException("Email is disabled (Email:Provider = none).", transient: false);
}
