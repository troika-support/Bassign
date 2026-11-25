using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bassign.Application.Media;
using Bassign.Application.Messaging;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>
/// 360dialog (Cloud API v2). Base address + D360-API-KEY header are configured in DI. Sends are not
/// retried by the HTTP pipeline (a retried POST could double-send); the notification outbox retries
/// transient failures with back-off instead.
/// </summary>
public sealed class Dialog360WhatsAppClient(HttpClient http) : IWhatsAppClient
{
    public async Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("messages", CloudApiPayloads.Build(message), CloudApiPayloads.Json, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new WhatsAppSendException("Couldn't reach WhatsApp.", transient: true, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WhatsAppSendException("WhatsApp timed out.", transient: true, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                throw new WhatsAppSendException(
                    $"WhatsApp rejected the message ({(int)response.StatusCode}): {(body.Length > 300 ? body[..300] : body)}", transient);
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString()
                   ?? throw new WhatsAppSendException("WhatsApp returned no message id.", transient: false);
        }
    }

    public async Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        // 1) media id → short-lived URL; 2) fetch it through 360dialog (replace the Meta host with ours).
        using var meta = await http.GetAsync(Uri.EscapeDataString(mediaId), cancellationToken);
        if (!meta.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await meta.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("url", out var urlElement) || urlElement.GetString() is not { } url) return null;
        var mime = doc.RootElement.TryGetProperty("mime_type", out var m) ? m.GetString() ?? "image/jpeg" : "image/jpeg";

        var proxied = new Uri(http.BaseAddress!, new Uri(url).PathAndQuery.TrimStart('/'));
        using var file = await http.GetAsync(proxied, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!file.IsSuccessStatusCode || file.Content.Headers.ContentLength > ProofReferences.MaxBytes) return null;

        var bytes = await file.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length > ProofReferences.MaxBytes ? null : (bytes, mime);
    }
}
