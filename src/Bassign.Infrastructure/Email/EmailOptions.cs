namespace Bassign.Infrastructure.Email;

/// <summary>
/// Transactional email. Admins choose the provider in Settings → Email (step 16b); a server environment variable
/// (e.g. Email__Provider) overrides and locks it. Read live (IOptionsMonitor), so a change applies without a restart.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>"none" (default: email off), "smtp", "gmail" (SMTP preset), "m365" (Microsoft Graph), "sendgrid",
    /// or "pickup" (development only: writes .eml files).</summary>
    public string Provider { get; set; } = "none";
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Bassign";
    /// <summary>SendGrid API key (secret).</summary>
    public string ApiKey { get; set; } = string.Empty;

    public SmtpSettings Smtp { get; set; } = new();
    public GraphSettings Graph { get; set; } = new();

    /// <summary>Folder for "pickup". Default: %LOCALAPPDATA%\Bassign\mail.</summary>
    public string? PickupDirectory { get; set; }

    /// <summary>The Web app's public address, for links in emails (server setting, e.g. https://tasks.client.co.zw).</summary>
    public string PublicBaseUrl { get; set; } = "https://localhost:7090";

    public string ResolvePickupDirectory() =>
        string.IsNullOrWhiteSpace(PickupDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bassign", "mail")
            : PickupDirectory;

    public string Normalized => (Provider ?? "none").Trim().ToLowerInvariant();
    public bool IsSendGrid => Normalized == "sendgrid";
    public bool IsPickup => Normalized == "pickup";
    public bool IsDisabled => Normalized is "none" or "";

    /// <summary>The address mail is sent from: the Microsoft 365 mailbox for "m365", otherwise FromAddress.</summary>
    public string EffectiveFromAddress => Normalized == "m365" && !string.IsNullOrWhiteSpace(Graph.Sender) ? Graph.Sender : FromAddress;

    /// <summary>The SMTP connection in effect; "gmail" fills in Google's server and uses the From address to sign in.</summary>
    public SmtpSettings EffectiveSmtp() => Normalized == "gmail"
        ? new SmtpSettings
        {
            Host = "smtp.gmail.com",
            Port = 587,
            Security = "starttls",
            Username = string.IsNullOrWhiteSpace(Smtp.Username) ? FromAddress : Smtp.Username,
            Password = Smtp.Password
        }
        : Smtp;

    /// <summary>What still has to be filled in for the chosen provider (empty = ready). Shown in Settings; while
    /// anything is missing, email is treated as off rather than failing.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        void Need(bool ok, string what) { if (!ok) missing.Add(what); }
        switch (Normalized)
        {
            case "smtp":
                Need(!string.IsNullOrWhiteSpace(Smtp.Host), "SMTP server");
                Need(FromAddress.Contains('@', StringComparison.Ordinal), "From address");
                break;
            case "gmail":
                Need(FromAddress.Contains('@', StringComparison.Ordinal), "Gmail address (From address)");
                Need(!string.IsNullOrWhiteSpace(Smtp.Password), "App password");
                break;
            case "m365":
                Need(!string.IsNullOrWhiteSpace(Graph.TenantId), "Tenant ID");
                Need(!string.IsNullOrWhiteSpace(Graph.ClientId), "Client ID");
                Need(!string.IsNullOrWhiteSpace(Graph.ClientSecret), "Client secret");
                Need(Graph.Sender.Contains('@', StringComparison.Ordinal), "Sending mailbox");
                break;
            case "sendgrid":
                Need(!string.IsNullOrWhiteSpace(ApiKey), "SendGrid API key");
                Need(FromAddress.Contains('@', StringComparison.Ordinal), "From address (a verified SendGrid sender)");
                break;
        }
        return missing;
    }

    public bool IsReady => !IsDisabled && Missing().Count == 0;
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    /// <summary>"starttls" (587), "ssl" (465) or "none" (internal relays only).</summary>
    public string Security { get; set; } = "starttls";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Microsoft 365 via Graph: an Azure app registration with the Mail.Send application permission
/// (admin-consented). Avoids SMTP AUTH, which many Microsoft 365 tenants disable.</summary>
public sealed class GraphSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>The mailbox mail is sent as, e.g. tasks@client.co.zw.</summary>
    public string Sender { get; set; } = string.Empty;
}
