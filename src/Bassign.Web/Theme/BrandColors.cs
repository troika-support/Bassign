using System.Globalization;
using Bassign.Domain.Common;

namespace Bassign.Web.Theme;

/// <summary>Derives the tint (selected/active backgrounds) and pressed shade from the org accent.</summary>
public static class BrandColors
{
    public static string Tint(string hex, double towardWhite) => Mix(hex, 255, towardWhite);
    public static string Shade(string hex, double towardBlack) => Mix(hex, 0, towardBlack);

    private static string Mix(string hex, int target, double amount)
    {
        if (!ColorContrast.IsHex(hex)) return hex;
        int Channel(int i)
        {
            var c = int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (int)Math.Round(c + (target - c) * amount);
        }
        return $"#{Channel(0):X2}{Channel(1):X2}{Channel(2):X2}";
    }
}
