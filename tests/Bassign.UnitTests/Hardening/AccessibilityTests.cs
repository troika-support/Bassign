using Bassign.Domain.Common;

namespace Bassign.UnitTests.Hardening;

/// <summary>WCAG 2.2 AAA (1.4.6): 7:1 for text. Keep these pairs in sync with tokens.css / BassignTheme.</summary>
public class AccessibilityTests
{
    private static double Ratio(string a, string b)
    {
        var (la, lb) = (ColorContrast.RelativeLuminance(a), ColorContrast.RelativeLuminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    // light: text tokens on card (#FFFFFF), canvas (#F6F7F9) and surface (#EEF1F4)
    [InlineData("#16191D")] [InlineData("#44505E")] [InlineData("#4B515B")]
    [InlineData("#085D2D")] [InlineData("#823D07")] [InlineData("#9D1616")] [InlineData("#1B49AE")] [InlineData("#423BC0")]
    public void Light_text_tokens_are_AAA_on_every_light_background(string text)
    {
        foreach (var bg in new[] { "#FFFFFF", "#F6F7F9", "#EEF1F4" })
            Assert.True(Ratio(text, bg) >= ColorContrast.Aaa, $"{text} on {bg} is {Ratio(text, bg):0.00}:1");
    }

    [Theory]
    [InlineData("#E7EAEE")] [InlineData("#AEB6C0")] [InlineData("#AEB6BF")]
    [InlineData("#56D08A")] [InlineData("#F1A551")] [InlineData("#FF9999")] [InlineData("#94B5FF")] [InlineData("#A5B4FC")]
    public void Dark_text_tokens_are_AAA_on_every_dark_background(string text)
    {
        foreach (var bg in new[] { "#1A1F26", "#121418", "#232A33" })
            Assert.True(Ratio(text, bg) >= ColorContrast.Aaa, $"{text} on {bg} is {Ratio(text, bg):0.00}:1");
    }

    [Theory]
    [InlineData("#085D2D", "#E6F4EC")]   // Done chip
    [InlineData("#9D1616", "#FBEAEA")]   // Overdue chip
    [InlineData("#823D07", "#FBF0E6")]   // In progress chip
    public void Status_chips_are_AAA(string text, string background) =>
        Assert.True(Ratio(text, background) >= ColorContrast.Aaa);

    [Theory]
    [InlineData("#4F46E5")] [InlineData("#0F766E")] [InlineData("#E11D48")] [InlineData("#2563EB")] [InlineData("#16A34A")] [InlineData("#F59E0B")]
    public void Any_brand_accent_is_rendered_AAA_with_white_text(string brand)
    {
        var accent = ColorContrast.AccessibleOnWhite(brand);
        Assert.True(ColorContrast.WithWhite(accent) >= ColorContrast.Aaa, $"{brand} → {accent}");
    }

    [Fact]
    public void An_already_dark_accent_is_left_alone() =>
        Assert.Equal("#1E1B4B", ColorContrast.AccessibleOnWhite("#1E1B4B"));
}
