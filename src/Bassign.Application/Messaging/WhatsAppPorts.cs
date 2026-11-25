using Bassign.Domain.Messaging;

namespace Bassign.Application.Messaging;

/// <summary>Sends to / downloads from the WhatsApp provider (360dialog, or the dev simulator).</summary>
public interface IWhatsAppClient
{
    /// <summary>Returns the provider message id. Throws <see cref="WhatsAppSendException"/> on failure.</summary>
    Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default);

    /// <summary>Downloads inbound media (proof photos). Media URLs are short-lived: call promptly.</summary>
    Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default);
}

/// <summary>Conversation log: drives the 24-hour window rule and the simulator / troubleshooting view.</summary>
public interface IWhatsAppLog
{
    Task<DateTimeOffset?> LastInboundAtAsync(string phoneE164, CancellationToken cancellationToken = default);
    void RecordInbound(WhatsAppInbound message);
    Task<int> CountInboundSinceAsync(string phoneE164, DateTimeOffset since, string textPrefix, CancellationToken cancellationToken = default);
    void RecordOutbound(WhatsAppOutbound message, string providerMessageId, DateTimeOffset at);

    /// <summary>The buttons / list rows of the last message we sent this number (newest first lookup), so a reply
    /// of "1" or "Accept" can be matched when the provider can't carry button ids (Twilio, typed replies).</summary>
    Task<IReadOnlyList<WaButton>> LastOutboundActionsAsync(string phoneE164, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WaButton>>([]);
    Task UpdateStatusAsync(string providerMessageId, string status, CancellationToken cancellationToken = default);
}

public interface IWhatsAppBindingRepository
{
    void Add(WhatsAppBinding binding);
    Task<WhatsAppBinding?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<WhatsAppBinding?> GetVerifiedByPhoneAsync(string phoneE164, CancellationToken cancellationToken = default);
    Task<WhatsAppBinding?> GetByActiveCodeHashAsync(string codeHash, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Organization-level WhatsApp settings the Application layer needs.</summary>
public interface IWhatsAppSettings
{
    /// <summary>The business number staff message (E.164), or null when WhatsApp isn't configured.</summary>
    string? BusinessNumberE164 { get; }
    string TemplateLanguage { get; }

    /// <summary>True in development: messages go to the built-in simulator, not a real provider.</summary>
    bool IsSimulator { get; }

    /// <summary>"none", "360dialog", "meta", "twilio" or "simulator".</summary>
    string Provider => IsSimulator ? "simulator" : "none";
    /// <summary>A provider is chosen and fully configured.</summary>
    bool IsReady => BusinessNumberE164 is not null;
    IReadOnlyList<string> Missing => [];
}
