using System.Text.Json;
using System.Text.Json.Serialization;
using Bassign.Application.Messaging;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>Maps provider-neutral messages to the WhatsApp Cloud API message JSON (docs/whatsapp.md).
/// Verify field names against the current 360dialog docs when going live.</summary>
public static class CloudApiPayloads
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static object Build(WhatsAppOutbound message)
    {
        var to = message.ToE164.TrimStart('+');
        return message switch
        {
            WaText t => new
            {
                messaging_product = "whatsapp", recipient_type = "individual", to, type = "text",
                text = new { preview_url = false, body = t.Body }
            },
            WaButtons b => new
            {
                messaging_product = "whatsapp", recipient_type = "individual", to, type = "interactive",
                interactive = new
                {
                    type = "button",
                    body = new { text = Clip(b.Body, 1024) },
                    action = new { buttons = b.Buttons.Take(3).Select(x => new { type = "reply", reply = new { id = x.Id, title = Clip(x.Title, 20) } }).ToArray() }
                }
            },
            WaList l => new
            {
                messaging_product = "whatsapp", recipient_type = "individual", to, type = "interactive",
                interactive = new
                {
                    type = "list",
                    body = new { text = Clip(l.Body, 1024) },
                    action = new
                    {
                        button = Clip(l.ButtonText, 20),
                        sections = new[]
                        {
                            new { title = "Tasks", rows = l.Rows.Take(10).Select(r => new { id = r.Id, title = Clip(r.Title, 24), description = r.Description is null ? null : Clip(r.Description, 72) }).ToArray() }
                        }
                    }
                }
            },
            WaTemplate t => new
            {
                messaging_product = "whatsapp", recipient_type = "individual", to, type = "template",
                template = new
                {
                    name = t.Name,
                    language = new { code = t.Language },
                    components = TemplateComponents(t)
                }
            },
            _ => throw new NotSupportedException($"Unsupported message type {message.GetType().Name}.")
        };
    }

    private static object[] TemplateComponents(WaTemplate t)
    {
        var components = new List<object>
        {
            new { type = "body", parameters = t.BodyParameters.Select(p => new { type = "text", text = p }).ToArray() }
        };
        for (var i = 0; i < t.QuickReplies.Count; i++)
        {
            components.Add(new
            {
                type = "button", sub_type = "quick_reply", index = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                parameters = new[] { new { type = "payload", payload = t.QuickReplies[i].Id } }
            });
        }
        return [.. components];
    }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
