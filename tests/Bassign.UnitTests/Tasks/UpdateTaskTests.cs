using Bassign.Domain.Tasks;

namespace Bassign.UnitTests.Tasks;

public class UpdateTaskTests
{
    [Fact]
    public void UpdateDetails_changes_title_detail_and_due_on_an_open_task()
    {
        var task = TaskItem.Create("Old", "mgr-1", DateTimeOffset.UtcNow);
        var newDue = DateTimeOffset.UtcNow.AddDays(3);

        task.UpdateDetails("New title", "New detail", newDue, DateTimeOffset.UtcNow);

        Assert.Equal("New title", task.Title);
        Assert.Equal("New detail", task.Detail);
        Assert.Equal(newDue, task.DueDate);
    }

    [Fact]
    public void UpdateDetails_rejects_a_closed_task()
    {
        var task = TaskItem.Create("T", "mgr-1", DateTimeOffset.UtcNow);
        task.Accept();
        task.Complete(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(
            () => task.UpdateDetails("X", null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateDetails_rejects_a_past_due_date()
    {
        var task = TaskItem.Create("T", "mgr-1", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(
            () => task.UpdateDetails("T", null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
    }
}
