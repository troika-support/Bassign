using Bassign.Domain.Common;
using Bassign.Infrastructure.Email;
using Bassign.Infrastructure.Hosting;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Privacy;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Bassign.UnitTests.Hardening;

public class SecurityTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Bassign";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static readonly Env Production = new("Production");
    private static readonly Env Development = new("Development");

    [Theory]
    [InlineData("tendai.ncube@example.co.zw", "te***@example.co.zw")]
    [InlineData("a@b.co", "a***@b.co")]
    [InlineData(null, "(none)")]
    [InlineData("not-an-email", "***")]
    public void Emails_are_masked_for_logs(string? email, string expected) => Assert.Equal(expected, Pii.MaskEmail(email));

    [Fact]
    public void A_short_jwt_key_fails_startup()
    {
        var result = new AuthOptionsValidator().Validate(null, new AuthOptions { JwtSigningKey = "too-short", Issuer = "bassign", Audience = "bassign" });
        Assert.True(result.Failed);
        Assert.DoesNotContain("too-short", result.FailureMessage, StringComparison.Ordinal);   // never echo secrets
    }

    [Fact]
    public void A_strong_jwt_config_passes() =>
        Assert.True(new AuthOptionsValidator().Validate(null, new AuthOptions
        {
            JwtSigningKey = new string('k', 48), Issuer = "bassign", Audience = "bassign", AccessTokenMinutes = 15, RefreshTokenDays = 30
        }).Succeeded);

    [Fact]
    public void The_whatsapp_simulator_is_refused_in_production() =>
        Assert.True(new WhatsAppOptionsValidator(Production).Validate(null, new WhatsAppOptions { Provider = "simulator", WebhookToken = new string('t', 32) }).Failed);

    [Fact]
    public void Live_whatsapp_needs_a_key_number_and_long_webhook_token()
    {
        var result = new WhatsAppOptionsValidator(Production).Validate(null, new WhatsAppOptions { Provider = "360dialog", WebhookToken = "short" });
        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Fact]
    public void The_simulator_is_fine_in_development() =>
        Assert.True(new WhatsAppOptionsValidator(Development).Validate(null, new WhatsAppOptions { Provider = "simulator" }).Succeeded);

    [Fact]
    public void Pickup_email_is_refused_in_production() =>
        Assert.True(new EmailOptionsValidator(Production).Validate(null, new EmailOptions { Provider = "pickup", PublicBaseUrl = "https://tasks.example.co.zw" }).Failed);

    [Fact]
    public void Production_email_links_must_be_https() =>
        Assert.True(new EmailOptionsValidator(Production).Validate(null, new EmailOptions { Provider = "sendgrid", ApiKey = "SG.x", PublicBaseUrl = "http://tasks.example.co.zw" }).Failed);

    [Theory]
    [InlineData("abc-123_XYZ", "abc-123_XYZ")]
    [InlineData("has space", null)]
    [InlineData("<script>", null)]
    [InlineData("", null)]
    public void Correlation_ids_from_callers_are_sanitised(string input, string? expected) =>
        Assert.Equal(expected, CorrelationIdMiddleware.Sanitize(input));

    [Fact]
    public void Correlation_ids_are_length_capped() =>
        Assert.Null(CorrelationIdMiddleware.Sanitize(new string('a', 65)));

    [Fact]
    public void Retention_defaults_match_the_privacy_notice()
    {
        var policy = new RetentionOptions().ToPolicy();
        Assert.Equal(90, policy.WhatsAppMessageDays);
        Assert.Equal(90, policy.NotificationDays);
        Assert.Equal(365, policy.ProofPhotoDays);
    }
}
