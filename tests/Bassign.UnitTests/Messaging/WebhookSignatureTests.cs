using System.Text;
using Bassign.Infrastructure.WhatsApp;

namespace Bassign.UnitTests.Messaging;

public class WebhookSignatureTests
{
    private const string Secret = "test-app-secret";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"entry":[]}""");

    [Fact]
    public void Valid_signature_is_accepted() =>
        Assert.True(WebhookSignature.IsValid(Secret, Body, WebhookSignature.Sign(Secret, Body)));

    [Fact]
    public void Tampered_body_is_rejected()
    {
        var header = WebhookSignature.Sign(Secret, Body);
        Assert.False(WebhookSignature.IsValid(Secret, Encoding.UTF8.GetBytes("""{"entry":[1]}"""), header));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=nothex")]
    [InlineData("md5=abcdef")]
    public void Missing_or_malformed_header_is_rejected(string? header) =>
        Assert.False(WebhookSignature.IsValid(Secret, Body, header));

    [Fact]
    public void Signature_from_another_secret_is_rejected() =>
        Assert.False(WebhookSignature.IsValid(Secret, Body, WebhookSignature.Sign("other-secret", Body)));

    [Fact]
    public void Path_token_must_match_exactly()
    {
        Assert.True(WebhookSignature.TokenMatches("abc123", "abc123"));
        Assert.False(WebhookSignature.TokenMatches("abc123", "abc124"));
        Assert.False(WebhookSignature.TokenMatches("abc123", null));
        Assert.False(WebhookSignature.TokenMatches("", ""));   // unconfigured token never matches
    }
}
