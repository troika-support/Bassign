namespace Bassign.Infrastructure.Options;

// Enforced in step 12 (WhatsApp); optional here.
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
}
