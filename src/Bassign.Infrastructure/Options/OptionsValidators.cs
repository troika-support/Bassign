using System.Text;
using Bassign.Domain.Messaging;
using Bassign.Infrastructure.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Options;

// Startup validation (ValidateOnStart): a misconfigured install fails fast with a clear message instead of
// misbehaving later. Secrets are only checked for presence/strength — their values never appear in errors.

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions o)
    {
        var errors = new List<string>();
        if (Encoding.UTF8.GetByteCount(o.JwtSigningKey) < 32)
            errors.Add("Auth:JwtSigningKey must be at least 32 bytes (set Auth__JwtSigningKey; generate with a password manager).");
        if (string.IsNullOrWhiteSpace(o.Issuer) || string.IsNullOrWhiteSpace(o.Audience))
            errors.Add("Auth:Issuer and Auth:Audience are required.");
        if (o.AccessTokenMinutes is < 5 or > 60)
            errors.Add("Auth:AccessTokenMinutes must be 5–60.");
        if (o.RefreshTokenDays is < 1 or > 90)
            errors.Add("Auth:RefreshTokenDays must be 1–90.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>Only the server-level rule: the simulator is development-only. Provider credentials are Admin-editable,
/// so an incomplete setup means "WhatsApp off, everyone gets email" — never "the app won't start".</summary>
public sealed class WhatsAppOptionsValidator(IHostEnvironment env) : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions o) =>
        o.IsSimulator && !env.IsDevelopment()
            ? ValidateOptionsResult.Fail("WhatsApp:Provider 'simulator' is for development only.")
            : ValidateOptionsResult.Success;
}

public sealed class PublicOptionsValidator(IHostEnvironment env) : IValidateOptions<PublicOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicOptions o) =>
        !env.IsDevelopment() && !(Uri.TryCreate(o.ApiBaseUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
            ? ValidateOptionsResult.Fail("Public:ApiBaseUrl must be the API's https:// address, e.g. https://api.client.co.zw (Public__ApiBaseUrl).")
            : ValidateOptionsResult.Success;
}

/// <summary>Only checks what an Admin can't fix from Settings (server settings). Provider credentials are
/// Admin-editable, so an incomplete setup means "email off for now" — never "the app won't start".</summary>
public sealed class EmailOptionsValidator(IHostEnvironment env) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions o)
    {
        var errors = new List<string>();
        if (!env.IsDevelopment())
        {
            if (o.IsPickup) errors.Add("Email:Provider 'pickup' is for development only.");
            if (!Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                errors.Add("Email:PublicBaseUrl must be the Web app's https:// address (used in email links).");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions o)
    {
        var errors = new List<string>();
        if (o.MinConfidence is < 0 or > 1) errors.Add("Ai:MinConfidence must be between 0 and 1.");
        if (o.TimeoutSeconds is < 1 or > 120) errors.Add("Ai:TimeoutSeconds must be 1–120.");
        if (o.MaxOutputTokens is < 50 or > 4000) errors.Add("Ai:MaxOutputTokens must be 50–4000.");
        if (o.MaxInputChars is < 1000 or > 50_000) errors.Add("Ai:MaxInputChars must be 1000–50000.");
        if (!o.IsClaude && !o.IsOpenAiCompatible && !string.Equals(o.Provider, "none", StringComparison.OrdinalIgnoreCase))
            errors.Add("Ai:Provider must be 'claude', 'openai' or 'none'.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
