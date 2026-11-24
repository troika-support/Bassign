using System.Text.Json;
using Bassign.Application.Messaging;
using Bassign.Domain.Messaging;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.WhatsApp;

public sealed class WhatsAppBindingRepository(AppDbContext db) : IWhatsAppBindingRepository
{
    public void Add(WhatsAppBinding binding) => db.WhatsAppBindings.Add(binding);

    public Task<WhatsAppBinding?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        db.WhatsAppBindings.FirstOrDefaultAsync(b => b.UserId == userId, cancellationToken);

    public Task<WhatsAppBinding?> GetVerifiedByPhoneAsync(string phoneE164, CancellationToken cancellationToken = default) =>
        db.WhatsAppBindings.FirstOrDefaultAsync(b => b.PhoneE164 == phoneE164 && b.Verified, cancellationToken);

    public Task<WhatsAppBinding?> GetByActiveCodeHashAsync(string codeHash, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        db.WhatsAppBindings.FirstOrDefaultAsync(b => b.LinkCodeHash == codeHash && b.LinkCodeExpiresAt > nowUtc, cancellationToken);
}

public sealed class WhatsAppLog(AppDbContext db) : IWhatsAppLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DateTimeOffset?> LastInboundAtAsync(string phoneE164, CancellationToken cancellationToken = default) =>
        await db.WhatsAppMessages.AsNoTracking()
            .Where(m => m.PhoneE164 == phoneE164 && m.Direction == "in")
            .OrderByDescending(m => m.At)
            .Select(m => (DateTimeOffset?)m.At)
            .FirstOrDefaultAsync(cancellationToken);

    public void RecordInbound(WhatsAppInbound message) =>
        db.WhatsAppMessages.Add(new WhatsAppLogEntry
        {
            Direction = "in",
            PhoneE164 = message.FromE164,
            ProviderMessageId = message.ProviderMessageId,
            Kind = message.Type,
            Body = Truncate(message.Text ?? (message.ReplyId is null ? $"[{message.Type}]" : $"[{message.ReplyId}]")),
            ActionsJson = message.MediaId is null ? null : JsonSerializer.Serialize(new { mediaId = message.MediaId }, Json),
            Status = "received",
            At = message.At
        });

    public Task<int> CountInboundSinceAsync(string phoneE164, DateTimeOffset since, string textPrefix, CancellationToken cancellationToken = default) =>
        db.WhatsAppMessages.AsNoTracking().CountAsync(
            m => m.Direction == "in" && m.PhoneE164 == phoneE164 && m.At >= since && m.Body.StartsWith(textPrefix),
            cancellationToken);

    public void RecordOutbound(WhatsAppOutbound message, string providerMessageId, DateTimeOffset at)
    {
        var (kind, actions) = message switch
        {
            WaButtons b => ("buttons", (object?)b.Buttons.Select(x => new { x.Id, x.Title })),
            WaList l => ("list", l.Rows.Select(r => new { r.Id, r.Title })),
            WaTemplate t => ("template", t.QuickReplies.Select(x => new { x.Id, x.Title })),
            _ => ("text", null)
        };
        db.WhatsAppMessages.Add(new WhatsAppLogEntry
        {
            Direction = "out",
            PhoneE164 = message.ToE164,
            ProviderMessageId = providerMessageId,
            Kind = kind,
            Body = Truncate(message.Body),
            ActionsJson = actions is null ? null : JsonSerializer.Serialize(actions, Json),
            Status = "sent",
            At = at
        });
    }

    public Task UpdateStatusAsync(string providerMessageId, string status, CancellationToken cancellationToken = default) =>
        db.WhatsAppMessages
            .Where(m => m.ProviderMessageId == providerMessageId && m.Direction == "out")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, status), cancellationToken);

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}

public sealed class WhatsAppSettings(IOptions<WhatsAppOptions> options) : IWhatsAppSettings
{
    public string? BusinessNumberE164 =>
        PhoneNumbers.IsE164(options.Value.BusinessNumber) ? options.Value.BusinessNumber : null;

    public string TemplateLanguage => options.Value.TemplateLanguage;

    public bool IsSimulator => options.Value.IsSimulator;
}
