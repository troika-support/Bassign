using System.Globalization;
using System.Text.RegularExpressions;
using Bassign.Field.Core.Api;
using Microsoft.Maui.Storage;

namespace Bassign.Mobile.Services;

/// <summary>The organization's name + accent, so the phone matches the web app. Cached in Preferences
/// (works offline and on first paint), refreshed from the API on launch and resume.</summary>
public sealed partial class BrandingService(HttpBrandingApi api)
{
    private const string NameKey = "bassign.org.name";
    private const string AccentKey = "bassign.org.accent";
    private const string DefaultAccent = "#4F46E5";

    public event Action? Changed;

    public string Name { get; private set; } = Preferences.Default.Get(NameKey, "Bassign");
    public string Accent { get; private set; } = ValidOr(Preferences.Default.Get(AccentKey, DefaultAccent));

    public string Initial => Name.Trim() is { Length: > 0 } n ? n[..1].ToUpperInvariant() : "B";

    public string Css =>
        $":root{{--ba-accent:{Accent};--ba-accent-600:{Mix(Accent, 0, 0.15)};--ba-accent-tint:{Mix(Accent, 255, 0.90)};}}";

    /// <summary>A different organization was connected: show its name with the default accent until branding loads.</summary>
    public void ResetTo(string organizationName)
    {
        Name = organizationName;
        Accent = DefaultAccent;
        Preferences.Default.Set(NameKey, Name);
        Preferences.Default.Set(AccentKey, Accent);
        Changed?.Invoke();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var branding = await api.GetAsync();
            if (branding is null || !Hex().IsMatch(branding.AccentHex)) return;
            if (branding.Name == Name && string.Equals(branding.AccentHex, Accent, StringComparison.OrdinalIgnoreCase)) return;

            Name = branding.Name;
            Accent = branding.AccentHex;
            Preferences.Default.Set(NameKey, Name);
            Preferences.Default.Set(AccentKey, Accent);
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Offline: keep the cached branding.
        }
    }

    private static string ValidOr(string hex) => Hex().IsMatch(hex) ? hex : DefaultAccent;

    // Blend toward white (tint) or black (pressed shade) — same maths as the web's BrandColors.
    private static string Mix(string hex, int target, double amount)
    {
        int Channel(int i)
        {
            var c = int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (int)Math.Round(c + (target - c) * amount);
        }
        return $"#{Channel(0):X2}{Channel(1):X2}{Channel(2):X2}";
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();
}
