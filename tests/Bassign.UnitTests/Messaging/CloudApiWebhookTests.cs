using Bassign.Infrastructure.WhatsApp;

namespace Bassign.UnitTests.Messaging;

public class CloudApiWebhookTests
{
    private static string Wrap(string messages, string statuses = "[]") =>
        """{"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","messages":"""
        + messages + ""","statuses":""" + statuses + "}}]}]}";

    [Fact]
    public void Splits_messages_and_statuses_with_distinct_keys()
    {
        var body = Wrap(
            """[{"from":"263771234567","id":"wamid.A","timestamp":"1764000000","type":"text","text":{"body":"hi"}}]""",
            """[{"id":"wamid.OUT","status":"delivered"},{"id":"wamid.OUT","status":"read"}]""");

        var items = CloudApiWebhook.Split(body);

        Assert.Equal(new[] { "wamid.A", "wamid.OUT:delivered", "wamid.OUT:read" }, items.Select(i => i.Key).ToArray());
        Assert.Equal(new[] { "message", "status", "status" }, items.Select(i => i.Kind).ToArray());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"entry":"oops"}""")]
    public void Malformed_bodies_yield_nothing(string body) => Assert.Empty(CloudApiWebhook.Split(body));

    [Fact]
    public void Text_message_maps_to_inbound()
    {
        var m = CloudApiWebhook.ToInbound("""{"from":"263771234567","id":"wamid.A","timestamp":"1764000000","type":"text","text":{"body":"TASKS"}}""");

        Assert.NotNull(m);
        Assert.Equal("+263771234567", m.FromE164);
        Assert.Equal("TASKS", m.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1764000000), m.At);
    }

    [Theory]
    [InlineData("""{"from":"263771234567","id":"w1","type":"interactive","interactive":{"type":"button_reply","button_reply":{"id":"accept:abc","title":"Accept"}}}""", "accept:abc")]
    [InlineData("""{"from":"263771234567","id":"w2","type":"interactive","interactive":{"type":"list_reply","list_reply":{"id":"task:abc","title":"Fix tap"}}}""", "task:abc")]
    [InlineData("""{"from":"263771234567","id":"w3","type":"button","button":{"payload":"decline:abc","text":"Decline"}}""", "decline:abc")]
    public void Button_list_and_template_replies_carry_the_reply_id(string json, string expected) =>
        Assert.Equal(expected, CloudApiWebhook.ToInbound(json)?.ReplyId);

    [Fact]
    public void Image_carries_media_id()
    {
        var m = CloudApiWebhook.ToInbound("""{"from":"263771234567","id":"w4","type":"image","image":{"id":"media-1","mime_type":"image/jpeg"}}""");
        Assert.Equal("image", m?.Type);
        Assert.Equal("media-1", m?.MediaId);
    }

    [Fact]
    public void Simulator_body_round_trips_through_the_parser()
    {
        var body = CloudApiWebhook.BuildInbound("wamid.SIM1", "+263771234567", DateTimeOffset.UtcNow, null, "done:abc", "Mark done", null);

        var item = Assert.Single(CloudApiWebhook.Split(body));
        var m = CloudApiWebhook.ToInbound(item.PayloadJson);

        Assert.Equal("wamid.SIM1", item.Key);
        Assert.Equal("done:abc", m?.ReplyId);
        Assert.Equal("+263771234567", m?.FromE164);
    }
}
