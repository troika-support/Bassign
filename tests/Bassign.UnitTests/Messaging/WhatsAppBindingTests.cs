using Bassign.Domain.Messaging;

namespace Bassign.UnitTests.Messaging;

public class WhatsAppBindingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_link_code_is_active_for_15_minutes_only()
    {
        var binding = WhatsAppBinding.For("user-1");
        binding.IssueLinkCode("hash", Now);

        Assert.True(binding.HasActiveCode("hash", Now.AddMinutes(14)));
        Assert.False(binding.HasActiveCode("hash", Now.AddMinutes(15)));
        Assert.False(binding.HasActiveCode("other", Now));
    }

    [Fact]
    public void Verifying_binds_the_number_and_burns_the_code()
    {
        var binding = WhatsAppBinding.For("user-1");
        binding.IssueLinkCode("hash", Now);

        binding.Verify("+263771234567", Now);

        Assert.True(binding.Verified);
        Assert.Equal("+263771234567", binding.PhoneE164);
        Assert.False(binding.HasActiveCode("hash", Now));   // single use
    }

    [Theory]
    [InlineData("263771234567")]
    [InlineData("+0771234567")]
    [InlineData("+26377")]
    public void Only_E164_numbers_can_be_verified(string phone) =>
        Assert.Throws<ArgumentException>(() => WhatsAppBinding.For("u").Verify(phone, Now));

    [Fact]
    public void WhatsApp_sender_ids_are_normalised_to_E164_and_masked_for_display()
    {
        Assert.Equal("+263771234567", PhoneNumbers.FromWhatsAppId("263771234567"));
        Assert.Equal("+263 •••• 4567", PhoneNumbers.Mask("+263771234567"));
    }
}
