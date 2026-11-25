using System.Security.Cryptography;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Settings;
using Bassign.Domain.Common;
using Bassign.Domain.Messaging;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.Application.Messaging.Setup;

public sealed record WhatsAppSetupStatus(
    string Provider, bool Enabled, bool Ready, IReadOnlyList<string> Missing, string? BusinessNumber,
    bool WebhookGenerated, string? WebhookPathPattern, bool ApiAddressKnown);

/// <summary>The WhatsApp setup in effect right now, and where its webhook lives.</summary>
public interface IWhatsAppSetup
{
    WhatsAppSetupStatus Current();
    /// <summary>The full webhook address for a provider and token, or null when the API's public address isn't set.</summary>
    string? WebhookUrl(string provider, string token);
}

public sealed record GetWhatsAppStatusQuery : IRequest<WhatsAppSetupStatus>;

public sealed class GetWhatsAppStatusHandler(IWhatsAppSetup setup) : IRequestHandler<GetWhatsAppStatusQuery, WhatsAppSetupStatus>
{
    public ValueTask<WhatsAppSetupStatus> Handle(GetWhatsAppStatusQuery request, CancellationToken cancellationToken) => ValueTask.FromResult(setup.Current());
}

/// <summary>Shown ONCE after generating: paste them into the provider's console. Generating again replaces them.</summary>
public sealed record WebhookSecrets(string Url, string? VerifyToken);

public sealed record GenerateWhatsAppWebhookCommand(string Provider) : IRequest<Result<WebhookSecrets>>;

public sealed class GenerateWhatsAppWebhookHandler(
    IWhatsAppSetup setup, IAdminSettingsStore settings, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<GenerateWhatsAppWebhookCommand, Result<WebhookSecrets>>
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public async ValueTask<Result<WebhookSecrets>> Handle(GenerateWhatsAppWebhookCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("whatsapp.forbidden", "Only an Admin can change WhatsApp settings.");
        if (settings.IsLocked("WhatsApp:WebhookToken"))
            return new Error("whatsapp.locked", "The webhook address is set by your provider on the server.");

        var provider = request.Provider.Trim().ToLowerInvariant();
        var token = RandomNumberGenerator.GetString(Alphabet, 40);
        if (setup.WebhookUrl(provider, token) is not { } url)
            return new Error("whatsapp.no_api_address", "The API's public address isn't set on the server (Public__ApiBaseUrl). Ask whoever manages this installation.");

        var changes = new Dictionary<string, SettingChange> { ["WhatsApp:WebhookToken"] = new(token) };
        string? verify = null;
        if (provider == "meta" && !settings.IsLocked("WhatsApp:VerifyToken"))
        {
            verify = RandomNumberGenerator.GetString(Alphabet, 32);
            changes["WhatsApp:VerifyToken"] = new(verify);
        }
        await settings.SaveAsync(changes, currentUser.UserId, cancellationToken);
        return new WebhookSecrets(url, verify);
    }
}

/// <summary>Admin-only: one free-form message with the current settings. WhatsApp only allows these within 24 hours
/// of the person messaging the business number, so the screen asks them to send "hi" first.</summary>
public sealed record SendTestWhatsAppCommand(string ToE164) : IRequest<Result<Unit>>;

public sealed class SendTestWhatsAppHandler(
    IWhatsAppSetup setup, IWhatsAppClient client, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<SendTestWhatsAppCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SendTestWhatsAppCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("whatsapp.forbidden", "Only an Admin can send a test message.");
        var to = request.ToE164.Replace(" ", "", StringComparison.Ordinal);
        if (!PhoneNumbers.IsE164(to))
            return new Error("whatsapp.invalid", "Use an international number like +263771234567.");

        var status = setup.Current();
        if (!status.Enabled) return new Error("whatsapp.off", "WhatsApp is turned off. Choose a provider first.");
        if (!status.Ready) return new Error("whatsapp.incomplete", $"Still needed: {string.Join(", ", status.Missing)}.");

        try
        {
            await client.SendAsync(new WaText(to, "✅ Bassign test: WhatsApp is set up correctly."), cancellationToken);
            return Unit.Value;
        }
        catch (WhatsAppSendException ex)
        {
            return new Error("whatsapp.test_failed", ex.Message);
        }
    }
}
