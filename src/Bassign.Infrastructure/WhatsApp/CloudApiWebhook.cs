using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bassign.Application.Messaging;
using Bassign.Domain.Messaging;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>One message or status from a webhook body. <see cref="Key"/> is the dedupe key.</summary>
public sealed record WebhookItem(string Key, string Kind, string PayloadJson);

/// <summary>Reads the Cloud-API webhook shape (360dialog forwards it unchanged):
/// entry[].changes[].value.{messages[], statuses[]}. Verify against current docs when going live.</summary>
public static class CloudApiWebhook
{
    public const string MessageKind = "message";
    public const string StatusKind = "status";
    private const int MaxKeyLength = 128;

    /// <summary>Splits a body into items. A malformed body yields nothing (we still 200 — retries won't fix it).</summary>
    public static IReadOnlyList<WebhookItem> Split(string body)
    {
        var items = new List<WebhookItem>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return items; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return items;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)) continue;

                    if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                        foreach (var m in messages.EnumerateArray())
                            if (Str(m, "id") is { } id)
                                items.Add(new WebhookItem(Clip(id), MessageKind, m.GetRawText()));

                    if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                        foreach (var s in statuses.EnumerateArray())
                            if (Str(s, "id") is { } id && Str(s, "status") is { } status)
                                items.Add(new WebhookItem(Clip($"{id}:{status}"), StatusKind, s.GetRawText()));
                }
            }
        }
        return items;
    }

    public static WhatsAppInbound? ToInbound(string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var m = doc.RootElement;
            if (Str(m, "id") is not { } id || Str(m, "from") is not { } from) return null;

            var at = long.TryParse(Str(m, "timestamp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : DateTimeOffset.UtcNow;
            var type = Str(m, "type") ?? "unknown";
            string? text = null, replyId = null, mediaId = null, mime = null;

            switch (type)
            {
                case "text" when m.TryGetProperty("text", out var t):
                    text = Str(t, "body");
                    break;
                case "interactive" when m.TryGetProperty("interactive", out var i):
                    var reply = i.TryGetProperty("button_reply", out var br) ? br
                              : i.TryGetProperty("list_reply", out var lr) ? lr : default;
                    if (reply.ValueKind == JsonValueKind.Object) { replyId = Str(reply, "id"); text = Str(reply, "title"); }
                    break;
                case "button" when m.TryGetProperty("button", out var b):   // template quick reply
                    replyId = Str(b, "payload");
                    text = Str(b, "text");
                    break;
                case "image" when m.TryGetProperty("image", out var img):
                    mediaId = Str(img, "id");
                    mime = Str(img, "mime_type");
                    text = Str(img, "caption");
                    break;
            }

            return new WhatsAppInbound(id, PhoneNumbers.FromWhatsAppId(from), at, type, text, replyId, mediaId, mime);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static (string ProviderMessageId, string Status)? ToStatus(string statusJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(statusJson);
            return Str(doc.RootElement, "id") is { } id && Str(doc.RootElement, "status") is { } status ? (id, status) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Builds a webhook body exactly as the provider would send it — used by the simulator so
    /// simulated messages travel the same path (intake → inbox → worker) as real ones.</summary>
    public static string BuildInbound(string id, string fromE164, DateTimeOffset at, string? text, string? replyId, string? replyTitle, string? mediaId)
    {
        var message = new JsonObject
        {
            ["from"] = fromE164.TrimStart('+'),
            ["id"] = id,
            ["timestamp"] = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        };
        if (mediaId is not null)
        {
            message["type"] = "image";
            message["image"] = new JsonObject { ["id"] = mediaId, ["mime_type"] = "image/jpeg", ["caption"] = text };
        }
        else if (replyId is not null)
        {
            message["type"] = "interactive";
            message["interactive"] = new JsonObject
            {
                ["type"] = "button_reply",
                ["button_reply"] = new JsonObject { ["id"] = replyId, ["title"] = replyTitle ?? replyId }
            };
        }
        else
        {
            message["type"] = "text";
            message["text"] = new JsonObject { ["body"] = text ?? string.Empty };
        }

        var body = new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["id"] = "simulator",
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["field"] = "messages",
                    ["value"] = new JsonObject
                    {
                        ["messaging_product"] = "whatsapp",
                        ["messages"] = new JsonArray(message)
                    }
                })
            })
        };
        return body.ToJsonString();
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string Clip(string key) => key.Length <= MaxKeyLength ? key : key[..MaxKeyLength];
}
