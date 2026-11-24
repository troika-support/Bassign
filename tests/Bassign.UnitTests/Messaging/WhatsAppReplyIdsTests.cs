using Bassign.Application.Messaging;

namespace Bassign.UnitTests.Messaging;

public class WhatsAppReplyIdsTests
{
    [Fact]
    public void Parses_action_and_task_id()
    {
        var id = Guid.NewGuid();
        Assert.True(WhatsAppReplyIds.TryParse($"accept:{id}", out var action, out var taskId));
        Assert.Equal(WhatsAppReplyIds.Accept, action);
        Assert.Equal(id, taskId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("accept")]
    [InlineData("accept:not-a-guid")]
    [InlineData("delete:3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public void Rejects_anything_else(string? replyId) =>
        Assert.False(WhatsAppReplyIds.TryParse(replyId, out _, out _));
}
