using System.Globalization;

namespace Bassign.Domain.Common;

/// <summary>WCAG contrast maths for brand colours (design-system §2: accent must carry white button text).</summary>
public static class ColorContrast
{
    public const double MinButtonContrast = 4.5;

    /// <summary>WCAG 2.2 AAA for normal-size text (1.4.6).</summary>
    public const double Aaa = 7.0;

    /// <summary>
    /// The organization's accent, darkened just enough that white text on it — and it as text on white —
    /// meets AAA. Admins pick any brand colour ≥ 4.5:1; the UI always renders this version, so the
    /// brand stays recognisable while every button and link passes 7:1.
    /// </summary>
    public static string AccessibleOnWhite(string hex, double minimum = Aaa)
    {
        if (!IsHex(hex)) return hex;
        var r = Channel(hex, 0); var g = Channel(hex, 1); var b = Channel(hex, 2);
        for (var t = 0.0; t <= 1.0; t += 0.01)
        {
            var candidate = $"#{Mix(r, t):X2}{Mix(g, t):X2}{Mix(b, t):X2}";
            if (WithWhite(candidate) >= minimum) return candidate;
        }
        return "#000000";

        static int Mix(int channel, double t) => (int)Math.Round(channel * (1 - t));
    }

    private static int Channel(string hex, int index) =>
        int.Parse(hex.AsSpan(1 + index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public static bool IsHex(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#'
        && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    /// <summary>Contrast ratio of white text on the given background colour.</summary>
    public static double WithWhite(string hex) => 1.05 / (RelativeLuminance(hex) + 0.05);

    public static double RelativeLuminance(string hex)
    {
        if (!IsHex(hex))
            throw new ArgumentException("Expected a #RRGGBB colour.", nameof(hex));

        double Channel(int index)
        {
            var c = int.Parse(hex.AsSpan(1 + index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }
}
