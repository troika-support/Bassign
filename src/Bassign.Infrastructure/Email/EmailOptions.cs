namespace Bassign.Infrastructure.Email;

/// <summary>Transactional email. Secrets (Email__ApiKey) via user-secrets / environment variables — never source.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>"pickup" (development: writes .eml files you can open), "sendgrid", or "none".</summary>
    public string Provider { get; set; } = "pickup";
    public string FromAddress { get; set; } = "no-reply@bassign.local";
    public string FromName { get; set; } = "Bassign";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Folder for "pickup". Default: %LOCALAPPDATA%\Bassign\mail.</summary>
    public string? PickupDirectory { get; set; }

    /// <summary>The Web app's public address, for links in emails (e.g. https://tasks.client.co.zw).</summary>
    public string PublicBaseUrl { get; set; } = "https://localhost:7090";

    public string ResolvePickupDirectory() =>
        string.IsNullOrWhiteSpace(PickupDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bassign", "mail")
            : PickupDirectory;

    public bool IsSendGrid => string.Equals(Provider, "sendgrid", StringComparison.OrdinalIgnoreCase);
    public bool IsDisabled => string.Equals(Provider, "none", StringComparison.OrdinalIgnoreCase);
}
