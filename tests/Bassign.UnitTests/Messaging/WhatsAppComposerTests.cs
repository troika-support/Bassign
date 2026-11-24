using Bassign.Application.Messaging;

namespace Bassign.UnitTests.Messaging;

public class WhatsAppComposerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TaskId = Guid.Parse("0192f7a0-0000-7000-8000-000000000001");

    [Fact]
    public void Inside_the_24h_window_an_assignment_uses_interactive_buttons()
    {
        var message = WhatsAppComposer.TaskAssigned("+263771234567", TaskId, "Fix the gate", "Wed 25 Nov",
            lastInboundAt: Now.AddHours(-3), Now, "en");

        var buttons = Assert.IsType<WaButtons>(message);
        Assert.Equal(new[] { $"accept:{TaskId}", $"decline:{TaskId}" }, buttons.Buttons.Select(b => b.Id));
    }

    [Fact]
    public void Outside_the_24h_window_an_assignment_uses_the_approved_template()
    {
        var message = WhatsAppComposer.TaskAssigned("+263771234567", TaskId, "Fix the gate", "Wed 25 Nov",
            lastInboundAt: Now.AddHours(-25), Now, "en");

        var template = Assert.IsType<WaTemplate>(message);
        Assert.Equal(WhatsAppComposer.TaskAssignedTemplate, template.Name);
        Assert.Equal(new[] { "Fix the gate", "Wed 25 Nov" }, template.BodyParameters);
        Assert.Equal($"accept:{TaskId}", template.QuickReplies[0].Id);   // template quick replies carry the same commands
    }

    [Fact]
    public void Someone_who_never_messaged_us_gets_the_template() =>
        Assert.IsType<WaTemplate>(WhatsAppComposer.TaskAssigned("+263771234567", TaskId, "T", null, lastInboundAt: null, Now, "en"));

    [Fact]
    public void A_date_only_due_shows_without_a_time_in_the_org_zone()
    {
        // 23:59 in Harare (UTC+2) = 21:59 UTC
        var due = new DateTimeOffset(2026, 11, 25, 21, 59, 0, TimeSpan.Zero);
        Assert.Equal("Wed 25 Nov", DueText.Format(due, "Africa/Harare"));
    }
}
