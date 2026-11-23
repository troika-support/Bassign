namespace Bassign.Infrastructure.Options;

// Enforced in step 13 (AI); optional here.
public sealed class AiOptions
{
    public const string Section = "Ai";
    public string Provider { get; set; } = "claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
