using System.Globalization;

namespace Bassign.Domain.Common;

/// <summary>WCAG contrast maths for brand colours (design-system §2: accent must carry white button text).</summary>
public static class ColorContrast
{
    public const double MinButtonContrast = 4.5;

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
