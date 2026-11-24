using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Bassign.Infrastructure.WhatsApp;
using Microsoft.EntityFrameworkCore;

namespace Bassign.IntegrationTests;

[Collection(ApiCollection.Name)]
public class WhatsAppWebhookTests(BassignApiFactory api)
{
    private static string Body(string id, string fromDigits, string text) =>
        CloudApiWebhook.BuildInbound(id, "+" + fromDigits, DateTimeOffset.UtcNow, text, null, null, null);

    private Task<HttpResponseMessage> PostAsync(string token, string body, string? signature)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/whatsapp/{token}") { Content = content };
        if (signature is not null) request.Headers.Add(WebhookSignature.Header, signature);
        return api.CreateClient().SendAsync(request, Api.Ct);
    }

    [Fact]
    public async Task Wrong_path_token_looks_like_nothing_is_there()
    {
        var body = Body($"wamid.T{Guid.NewGuid():N}", "263779990001", "hi");
        var response = await PostAsync("wrong-token", body, WebhookSignature.Sign(BassignApiFactory.AppSecret, Encoding.UTF8.GetBytes(body)));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bad_or_missing_signature_is_rejected_and_nothing_is_stored()
    {
        var id = $"wamid.T{Guid.NewGuid():N}";
        var body = Body(id, "263779990002", "hi");

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(BassignApiFactory.WebhookToken, body, WebhookSignature.Sign("not-the-secret", Encoding.UTF8.GetBytes(body)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(BassignApiFactory.WebhookToken, body, null)).StatusCode);
        Assert.Equal(0, await api.DbAsync(db => db.InboundWebhookMessages.CountAsync(m => m.ProviderMessageId == id, Api.Ct)));
    }

    [Fact]
    public async Task A_retried_delivery_is_processed_once_and_an_unknown_number_gets_the_enrollment_reply()
    {
        var id = $"wamid.T{Guid.NewGuid():N}";
        const string phone = "263779990003";
        var body = Body(id, phone, "hi");
        var signature = WebhookSignature.Sign(BassignApiFactory.AppSecret, Encoding.UTF8.GetBytes(body));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(BassignApiFactory.WebhookToken, body, signature)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(BassignApiFactory.WebhookToken, body, signature)).StatusCode);   // provider retry

        Assert.Equal(1, await api.DbAsync(db => db.InboundWebhookMessages.CountAsync(m => m.ProviderMessageId == id, Api.Ct)));

        // The inbox worker handles it once: one inbound logged, one reply — the enrollment prompt, no task data.
        var replies = await BassignApiFactory.EventuallyAsync(
            () => api.DbAsync(db => db.WhatsAppMessages.Where(m => m.PhoneE164 == "+" + phone && m.Direction == "out").ToListAsync(Api.Ct)),
            list => list.Count > 0);
        var reply = Assert.Single(replies);
        Assert.Contains("isn't linked", reply.Body, StringComparison.Ordinal);
        Assert.Equal(1, await api.DbAsync(db => db.WhatsAppMessages.CountAsync(m => m.PhoneE164 == "+" + phone && m.Direction == "in", Api.Ct)));
    }
}
