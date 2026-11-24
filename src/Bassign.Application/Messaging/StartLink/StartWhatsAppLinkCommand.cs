using System.Globalization;
using System.Security.Cryptography;
using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Domain.Common;
using Bassign.Domain.Messaging;
using Mediator;

namespace Bassign.Application.Messaging.StartLink;

/// <summary>Issue a one-time code the current user sends from WhatsApp ("LINK 123456") to link their number.</summary>
public sealed record StartWhatsAppLinkCommand : IRequest<Result<WhatsAppLinkStarted>>, IAuditableRequest
{
    public string AuditAction => "whatsapp.link_start";
    public string AuditEntityType => "WhatsAppBinding";
    public string? AuditEntityId => null;
}

public sealed record WhatsAppLinkStarted(string Code, string MessageText, string DeepLink, string BusinessNumber, DateTimeOffset ExpiresAt);

public sealed class StartWhatsAppLinkHandler(
    IWhatsAppBindingRepository bindings,
    IWhatsAppSettings settings,
    ISecureTokens tokens,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<StartWhatsAppLinkCommand, Result<WhatsAppLinkStarted>>
{
    public async ValueTask<Result<WhatsAppLinkStarted>> Handle(StartWhatsAppLinkCommand request, CancellationToken cancellationToken)
    {
        if (settings.BusinessNumberE164 is not { } number)
            return new Error("whatsapp.not_configured", "WhatsApp isn't set up for this organization yet.");

        var now = clock.GetUtcNow();
        var binding = await bindings.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (binding is null)
        {
            binding = WhatsAppBinding.For(currentUser.UserId);
            bindings.Add(binding);
        }

        // 6 digits from a CSPRNG; re-roll on the (tiny) chance another active code has the same hash.
        string code, hash;
        do
        {
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            hash = LinkCodes.Hash(tokens, code);
        }
        while (await bindings.GetByActiveCodeHashAsync(hash, now, cancellationToken) is { } other && other.Id != binding.Id);

        binding.IssueLinkCode(hash, now);
        await uow.SaveChangesAsync(cancellationToken);

        var text = $"LINK {code}";
        var deepLink = $"https://wa.me/{number.TrimStart('+')}?text={Uri.EscapeDataString(text)}";
        return new WhatsAppLinkStarted(code, text, deepLink, number, binding.LinkCodeExpiresAt!.Value);
    }
}

public static class LinkCodes
{
    /// <summary>Domain-separated so a link code can never collide with another kind of token hash.</summary>
    public static string Hash(ISecureTokens tokens, string code) => tokens.Hash("whatsapp-link:" + code);
}
