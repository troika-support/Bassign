using Bassign.Domain.Organizations;

namespace Bassign.UnitTests.Organizations;

public class SecuritySettingsTests
{
    [Fact]
    public void Defaults_allow_any_domain_for_seven_days()
    {
        var org = OrganizationSettings.CreateDefault();
        Assert.Equal(7, org.InviteLifetimeDays);
        Assert.Empty(org.InviteDomains);
        Assert.True(org.AllowsInviteTo("anyone@example.com"));
    }

    [Fact]
    public void Domains_are_cleaned_and_matched_exactly()
    {
        var org = OrganizationSettings.CreateDefault();
        org.ConfigureSecurity(14, [" @MintedMoments.co.zw ", "gmail.com", "", "gmail.com"], " DPO ", "DPO@MintedMoments.co.zw", " +263 77 123 4567 ");

        Assert.Equal(new[] { "mintedmoments.co.zw", "gmail.com" }, org.InviteDomains);
        Assert.True(org.AllowsInviteTo("rudo@mintedmoments.co.zw"));
        Assert.False(org.AllowsInviteTo("rudo@evil-mintedmoments.co.zw"));
        Assert.False(org.AllowsInviteTo("rudo@mail.mintedmoments.co.zw"));   // sub-domains aren't included
        Assert.Equal("DPO", org.PrivacyContactName);
        Assert.Equal("dpo@mintedmoments.co.zw", org.PrivacyContactEmail);
        Assert.Equal("+263 77 123 4567", org.PrivacyContactPhone);
    }

    [Fact]
    public void Clearing_the_domains_allows_everyone_again()
    {
        var org = OrganizationSettings.CreateDefault();
        org.ConfigureSecurity(7, ["gmail.com"], null, null, null);
        org.ConfigureSecurity(7, [], null, null, null);
        Assert.Null(org.AllowedInviteDomains);
        Assert.True(org.AllowsInviteTo("x@anything.co.zw"));
    }

    [Theory]
    [InlineData(0, "gmail.com", null)]
    [InlineData(31, "gmail.com", null)]
    [InlineData(7, "not a domain", null)]
    [InlineData(7, "localhost", null)]
    [InlineData(7, "gmail.com", "not-an-email")]
    public void Bad_values_are_rejected(int days, string domain, string? email) =>
        Assert.ThrowsAny<ArgumentException>(() => OrganizationSettings.CreateDefault().ConfigureSecurity(days, [domain], null, email, null));
}
