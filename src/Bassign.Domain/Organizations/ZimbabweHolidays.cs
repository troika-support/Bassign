namespace Bassign.Domain.Organizations;

/// <summary>A public holiday or closure on a given date.</summary>
public sealed record ClosedDay(DateOnly Date, string Name, bool IsPublicHoliday);

/// <summary>
/// Zimbabwe's public holidays under the Public Holidays and Prohibition of Business Act [Chapter 10:21], as
/// listed for 2026 in General Notice 1361 of 2025, plus Munhumutapa Day (15 September) from 2026 (SI 148 of 2026).
/// When a fixed-date holiday falls on a Sunday, the next day that isn't already a holiday is observed instead
/// (so Christmas on a Sunday → Boxing Day Monday, then Tuesday off). There is no Saturday substitute.
/// Days the Government declares at short notice aren't predictable: Admins add those as closures.
/// </summary>
public static class ZimbabweHolidays
{
    public const int MunhumutapaDayFirstYear = 2026;

    public static IReadOnlyList<ClosedDay> For(int year)
    {
        var easter = EasterSunday(year);
        var heroes = NthWeekday(year, 8, DayOfWeek.Monday, 2);

        var fixedDates = new List<(DateOnly Date, string Name)>
        {
            (new DateOnly(year, 1, 1), "New Year's Day"),
            (new DateOnly(year, 2, 21), "Robert Gabriel Mugabe National Youth Day"),
            (new DateOnly(year, 4, 18), "Independence Day"),
            (new DateOnly(year, 5, 1), "Workers' Day"),
            (new DateOnly(year, 5, 25), "Africa Day"),
        };
        if (year >= MunhumutapaDayFirstYear) fixedDates.Add((new DateOnly(year, 9, 15), "Munhumutapa Day"));
        fixedDates.Add((new DateOnly(year, 12, 22), "National Unity Day"));
        fixedDates.Add((new DateOnly(year, 12, 25), "Christmas Day"));
        fixedDates.Add((new DateOnly(year, 12, 26), "Boxing Day"));

        var days = new List<ClosedDay>
        {
            new(easter.AddDays(-2), "Good Friday", true),
            new(easter.AddDays(-1), "Holy Saturday", true),
            new(easter, "Easter Sunday", true),
            new(easter.AddDays(1), "Easter Monday", true),
            new(heroes, "Heroes' Day", true),
            new(heroes.AddDays(1), "Defence Forces Day", true),
        };
        days.AddRange(fixedDates.Select(f => new ClosedDay(f.Date, f.Name, true)));

        // Sunday rule, in date order so a chain (Christmas Sunday → Boxing Day Monday → Tuesday) resolves.
        foreach (var (date, name) in fixedDates.Where(f => f.Date.DayOfWeek == DayOfWeek.Sunday).OrderBy(f => f.Date))
        {
            var observed = date.AddDays(1);
            while (days.Any(d => d.Date == observed)) observed = observed.AddDays(1);
            days.Add(new ClosedDay(observed, $"{name} (observed)", true));
        }

        return days.OrderBy(d => d.Date).ToList();
    }

    /// <summary>Holidays from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public static IEnumerable<ClosedDay> Between(DateOnly from, DateOnly to) =>
        Enumerable.Range(from.Year, to.Year - from.Year + 1)
            .SelectMany(For)
            .Where(d => d.Date >= from && d.Date <= to);

    /// <summary>Western (Gregorian) Easter Sunday — the anonymous Gregorian algorithm.</summary>
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }
}
