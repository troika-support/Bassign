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

public sealed class WhatsAppOptionsValidator(IHostEnvironment env) : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions o)
    {
        var errors = new List<string>();
        if (o.IsSimulator)
        {
            if (!env.IsDevelopment()) errors.Add("WhatsApp:Provider 'simulator' is for development only — set it to '360dialog'.");
        }
        else
        {
            if (!string.Equals(o.Provider, "360dialog", StringComparison.OrdinalIgnoreCase)) errors.Add("WhatsApp:Provider must be 'simulator' or '360dialog'.");
            if (string.IsNullOrWhiteSpace(o.ApiKey)) errors.Add("WhatsApp:ApiKey is required (WhatsApp__ApiKey).");
            if (!PhoneNumbers.IsE164(o.BusinessNumber)) errors.Add("WhatsApp:BusinessNumber must be E.164, e.g. +263771234567.");
        }
        if (!env.IsDevelopment() && o.WebhookToken.Length < 24)
            errors.Add("WhatsApp:WebhookToken must be at least 24 random characters (WhatsApp__WebhookToken).");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class EmailOptionsValidator(IHostEnvironment env) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions o)
    {
        var errors = new List<string>();
        if (o.IsSendGrid && string.IsNullOrWhiteSpace(o.ApiKey)) errors.Add("Email:ApiKey is required for SendGrid (Email__ApiKey).");
        if (!o.IsDisabled && !o.FromAddress.Contains('@', StringComparison.Ordinal)) errors.Add("Email:FromAddress must be an email address.");
        if (!env.IsDevelopment())
        {
            if (!o.IsSendGrid && !o.IsDisabled) errors.Add("Email:Provider 'pickup' is for development — use 'sendgrid' (or 'none').");
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
