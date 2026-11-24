using Bassign.Infrastructure.Persistence.Messaging;
using Bassign.Infrastructure.WhatsApp;

namespace Bassign.UnitTests.Messaging;

public class WhatsAppWebhookIntakeTests
{
    private sealed class FakeInboxStore : IWebhookInboxStore
    {
        public List<InboundWebhookMessage> Items { get; } = [];

        public Task<bool> TryAddAsync(InboundWebhookMessage message, CancellationToken cancellationToken = default)
        {
            if (Items.Any(i => i.ProviderMessageId == message.ProviderMessageId)) return Task.FromResult(false);
            Items.Add(message);
            return Task.FromResult(true);
        }
    }

    private const string Body =
        """{"entry":[{"changes":[{"value":{"messages":[{"from":"263771234567","id":"wamid.DUP","timestamp":"1764000000","type":"text","text":{"body":"hi"}}]}}]}]}""";

    [Fact]
    public async Task Duplicate_delivery_is_queued_once()
    {
        var store = new FakeInboxStore();
        var intake = new WhatsAppWebhookIntake(store, TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;

        var first = await intake.AcceptAsync(Body, ct);
        var retry = await intake.AcceptAsync(Body, ct);

        Assert.Equal(1, first);
        Assert.Equal(0, retry);
        var item = Assert.Single(store.Items);
        Assert.Equal(InboxStatus.Pending, item.Status);
    }
}
