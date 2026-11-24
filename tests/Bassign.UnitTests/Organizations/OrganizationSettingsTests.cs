using Bassign.Domain.Common;
using Bassign.Domain.Organizations;

namespace Bassign.UnitTests.Organizations;

public class OrganizationSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("#4F46E5", 6.0, 6.4)]   // default indigo — AAA for white text
    [InlineData("#FFFFFF", 1.0, 1.01)]
    public void ContrastWithWhite_matches_wcag(string hex, double min, double max) =>
        Assert.InRange(ColorContrast.WithWhite(hex), min, max);

    [Theory]
    [InlineData("4F46E5")]
    [InlineData("#XYZXYZ")]
    [InlineData("#4F46E")]
    public void IsHex_rejects_malformed_colours(string hex) => Assert.False(ColorContrast.IsHex(hex));

    [Fact]
    public void Configure_rejects_an_accent_too_light_for_white_text()
    {
        var org = OrganizationSettings.CreateDefault();
        Assert.Throws<ArgumentException>(() => org.Configure("Acme", "#FFD700", "Africa/Harare", Now));
    }

    [Fact]
    public void Configure_saves_branding_and_marks_setup_complete_once()
    {
        var org = OrganizationSettings.CreateDefault();
        org.Configure("  Acme Logistics ", "#0f766e", "Africa/Harare", Now);
        org.Configure("Acme Logistics", "#0F766E", "Africa/Harare", Now.AddDays(1));

        Assert.Equal("Acme Logistics", org.Name);
        Assert.Equal("#0F766E", org.AccentHex);
        Assert.Equal(Now, org.SetupCompletedAt);   // first completion is kept
    }
}
