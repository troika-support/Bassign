using System.Text.Json;
using Bassign.Application.Messaging;
using Bassign.Application.Messaging.Simulator;
using Bassign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>Development-only phone: builds a provider-shaped webhook body and feeds it through the real
/// intake, so the API's inbox worker handles it exactly like a message from WhatsApp.</summary>
public sealed class WhatsAppSimulator(WhatsAppWebhookIntake intake, SimulatorMediaStore media, AppDbContext db, TimeProvider clock)
    : IWhatsAppSimulator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InjectAsync(string fromE164, string? text, string? replyId, string? replyTitle, string? mediaId, CancellationToken cancellationToken = default)
    {
        var body = CloudApiWebhook.BuildInbound($"wamid.SIM{Guid.NewGuid():N}", fromE164, clock.GetUtcNow(), text, replyId, replyTitle, mediaId);
        await intake.AcceptAsync(body, cancellationToken);
    }

    public Task<string> SavePhotoAsync(byte[] content, CancellationToken cancellationToken = default) =>
        media.SaveAsync(content, cancellationToken);

    public async Task<IReadOnlyList<ConversationEntry>> GetConversationAsync(string phoneE164, CancellationToken cancellationToken = default)
    {
        var rows = await db.WhatsAppMessages.AsNoTracking()
            .Where(m => m.PhoneE164 == phoneE164)
            .OrderByDescending(m => m.At).ThenByDescending(m => m.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(m => m.At).ThenBy(m => m.Id)
            .Select(m => new ConversationEntry(m.Direction, m.Kind, m.Body, Actions(m.Direction, m.ActionsJson), m.Status, m.At))
            .ToList();
    }

    private static IReadOnlyList<WaButton> Actions(string direction, string? json)
    {
        if (direction != "out" || string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<WaButton>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }
}
