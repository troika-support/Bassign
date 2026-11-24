using System.Text.RegularExpressions;
using Bassign.Application.Messaging;
using Bassign.Infrastructure.Media;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>Development provider: "sends" by returning an id — the message is already in the conversation
/// log, which the web simulator renders as a phone. Inbound media comes from <see cref="SimulatorMediaStore"/>.</summary>
public sealed class SimulatorWhatsAppClient(SimulatorMediaStore media) : IWhatsAppClient
{
    public Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default) =>
        Task.FromResult($"sim.{Guid.NewGuid():N}");

    public Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default) =>
        media.ReadAsync(mediaId, cancellationToken);
}

/// <summary>Photos "sent" from the simulator phone. Lives beside the proof folder, so the Web app (which
/// writes) and the API (which reads) share it on one machine.</summary>
public sealed partial class SimulatorMediaStore(IOptions<MediaOptions> options)
{
    private readonly string _root = Path.GetFullPath(Path.Combine(options.Value.ResolveProofRoot(), "..", "whatsapp-sim"));

    public async Task<string> SaveAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var id = $"simmedia{Guid.NewGuid():N}";
        await File.WriteAllBytesAsync(Path.Combine(_root, id), content, cancellationToken);
        return id;
    }

    public async Task<(byte[] Content, string ContentType)?> ReadAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        if (!Id().IsMatch(mediaId)) return null;
        var path = Path.Combine(_root, mediaId);
        return File.Exists(path) ? (await File.ReadAllBytesAsync(path, cancellationToken), "image/jpeg") : null;
    }

    [GeneratedRegex("^simmedia[0-9a-f]{32}$")]
    private static partial Regex Id();
}
