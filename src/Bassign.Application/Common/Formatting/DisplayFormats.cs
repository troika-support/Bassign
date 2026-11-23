using System.Globalization;

namespace Bassign.Application.Common.Formatting;

/// <summary>Fixed display formats from design-system section 5. Times are org-local at the edge.</summary>
public static class DisplayFormats
{
    public const string Date = "ddd d MMM";
    public const string DateTime = "ddd d MMM, h:mm tt";

    public static string FormatDate(DateTimeOffset value) =>
        value.ToString(Date, CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTimeOffset value) =>
        value.ToString(DateTime, CultureInfo.InvariantCulture);

    public static string FormatMoney(decimal amount, string currencyCode) =>
        $"{currencyCode} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
