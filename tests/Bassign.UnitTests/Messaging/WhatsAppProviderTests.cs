using Bassign.Application.Messaging;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.WhatsApp;
using Microsoft.Extensions.Options;

namespace Bassign.UnitTests.Messaging;

public class WhatsAppProviderTests
{
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private const string Token = "0123456789abcdefghijklmnopqrstuv";

    [Fact]
    public void Each_provider_lists_what_is_missing()
    {
        Assert.Empty(new WhatsAppOptions().Missing());   // off: nothing required
        Assert.False(new WhatsAppOptions().IsReady);
        Assert.Contains("Phone number ID", new WhatsAppOptions { Provider = "meta" }.Missing());
        Assert.Contains("App secret", new WhatsAppOptions { Provider = "meta" }.Missing());
        Assert.Contains("Auth token", new WhatsAppOptions { Provider = "twilio" }.Missing());
        Assert.True(new WhatsAppOptions
        {
            Provider = "meta", BusinessNumber = "+263780000001", WebhookToken = Token, ApiKey = "EAAG...", AppSecret = "s",
            VerifyToken = "v", Meta = new() { PhoneNumberId = "1234567890" }
        }.IsReady);
    }

    [Fact]
    public void Only_meta_format_providers_use_the_cloud_webhook()
    {
        Assert.True(new WhatsAppOptions { Provider = "meta" }.UsesCloudWebhook);
        Assert.True(new WhatsAppOptions { Provider = "360dialog" }.UsesCloudWebhook);
        Assert.False(new WhatsAppOptions { Provider = "twilio" }.UsesCloudWebhook);
        Assert.False(new WhatsAppOptions().UsesCloudWebhook);
    }

    [Fact]
    public void Twilio_buttons_become_a_numbered_menu()
    {
        var text = TwilioTransport.RenderText(new WaButtons("+263771234567", "New task: Fix the gate",
            [new WaButton("accept:1", "Accept"), new WaButton("decline:1", "Decline")]));
        Assert.Equal("New task: Fix the gate\n\nReply with a number:\n1 — Accept\n2 — Decline", text);
    }

    [Fact]
    public void Twilio_signature_matches_twilios_algorithm()
    {
        // Expected value computed independently (Python hmac/sha1) from Twilio's documented algorithm.
        var form = new Dictionary<string, string>
        {
            ["MessageSid"] = "SM123", ["From"] = "whatsapp:+263771234567", ["Body"] = "1", ["To"] = "whatsapp:+263780000001"
        };
        const string url = "https://api.client.co.zw/webhooks/twilio/tok123";
        Assert.Equal("Xu4r2yyP9cjvqivhlaJ1ROCYTFw=", TwilioSignature.Sign("twilio-auth-token", url, form));
        Assert.True(TwilioSignature.IsValid("twilio-auth-token", url, form, "Xu4r2yyP9cjvqivhlaJ1ROCYTFw="));
        Assert.False(TwilioSignature.IsValid("wrong-token", url, form, "Xu4r2yyP9cjvqivhlaJ1ROCYTFw="));
        Assert.False(TwilioSignature.IsValid("twilio-auth-token", url + "x", form, "Xu4r2yyP9cjvqivhlaJ1ROCYTFw="));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("meta")]   // chosen but not set up
    public async Task Off_or_incomplete_whatsapp_fails_without_retrying(string provider)
    {
        var router = new RoutingWhatsAppClient([], new Monitor<WhatsAppOptions>(new WhatsAppOptions { Provider = provider }));
        var ex = await Assert.ThrowsAsync<WhatsAppSendException>(() => router.SendAsync(new WaText("+263771234567", "hi"), TestContext.Current.CancellationToken));
        Assert.False(ex.Transient);
    }

    [Fact]
    public void Twilio_status_callbacks_feed_the_same_inbox()
    {
        var item = Assert.Single(CloudApiWebhook.Split(CloudApiWebhook.BuildStatus("SM123", "delivered")));
        Assert.Equal("SM123:delivered", item.Key);
        Assert.Equal(("SM123", "delivered"), CloudApiWebhook.ToStatus(item.PayloadJson));
    }

    [Fact]
    public void Live_settings_hide_the_business_number_until_ready()
    {
        var settings = new WhatsAppSettings(new Monitor<WhatsAppOptions>(new WhatsAppOptions { Provider = "meta", BusinessNumber = "+263780000001" }));
        Assert.Null(settings.BusinessNumberE164);   // not ready → linking and notifications stay off
        Assert.False(settings.IsReady);
    }
}
