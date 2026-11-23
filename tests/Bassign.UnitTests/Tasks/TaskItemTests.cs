using Bassign.Domain.Tasks;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.UnitTests.Tasks;

public class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static TaskItem NewTask(bool proof = false, DateTimeOffset? due = null) =>
        TaskItem.Create("Stock count", "admin-1", Now, detail: "north warehouse",
                        dueDate: due, requiresProofPhoto: proof);

    [Fact]
    public void Create_sets_todo_and_raises_created()
    {
        var t = NewTask();
        Assert.Equal(TaskStatus.Todo, t.Status);
        Assert.Equal("Stock count", t.Title);
        Assert.Contains(t.DomainEvents, e => e is TaskCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_title(string title) =>
        Assert.Throws<ArgumentException>(() => TaskItem.Create(title, "admin-1", Now));

    [Fact]
    public void Create_rejects_past_due_date() =>
        Assert.Throws<ArgumentException>(() =>
            TaskItem.Create("x", "admin-1", Now, dueDate: Now.AddHours(-1)));

    [Fact]
    public void Create_allows_future_due_date()
    {
        var t = TaskItem.Create("x", "admin-1", Now, dueDate: Now.AddDays(1));
        Assert.Equal(Now.AddDays(1), t.DueDate);
    }

    [Fact]
    public void Accept_from_todo_sets_accepted()
    {
        var t = NewTask();
        t.Accept();
        Assert.Equal(TaskStatus.Accepted, t.Status);
        Assert.Contains(t.DomainEvents, e => e is TaskAccepted);
    }

    [Fact]
    public void Accept_twice_throws()
    {
        var t = NewTask();
        t.Accept();
        Assert.Throws<InvalidOperationException>(() => t.Accept());
    }

    [Fact]
    public void Complete_from_accepted_sets_done()
    {
        var t = NewTask();
        t.Accept();
        t.Complete(Now.AddHours(1));
        Assert.Equal(TaskStatus.Done, t.Status);
        Assert.Equal(Now.AddHours(1), t.CompletedAt);
        Assert.Contains(t.DomainEvents, e => e is TaskCompleted);
    }

    [Fact]
    public void Complete_from_todo_throws()
    {
        var t = NewTask();
        Assert.Throws<InvalidOperationException>(() => t.Complete(Now));
    }

    [Fact]
    public void Complete_twice_is_a_noop()
    {
        var t = NewTask();
        t.Accept();
        t.Complete(Now.AddHours(1));
        t.Complete(Now.AddHours(2));
        Assert.Equal(TaskStatus.Done, t.Status);
        Assert.Equal(Now.AddHours(1), t.CompletedAt);                 // unchanged by the second call
        Assert.Single(t.DomainEvents, e => e is TaskCompleted);       // only one completion event
    }

    [Fact]
    public void Complete_requires_proof_when_configured()
    {
        var t = NewTask(proof: true);
        t.Accept();
        Assert.Throws<InvalidOperationException>(() => t.Complete(Now.AddHours(1)));
        t.Complete(Now.AddHours(1), "photo-ref");
        Assert.Equal(TaskStatus.Done, t.Status);
    }

    [Fact]
    public void Assign_on_todo_sets_assignee()
    {
        var t = NewTask();
        t.AssignTo("user-9");
        Assert.Equal("user-9", t.AssigneeId);
        Assert.Contains(t.DomainEvents, e => e is TaskAssigned);
    }

    [Fact]
    public void Assign_on_closed_task_throws()
    {
        var t = NewTask();
        t.Accept();
        t.Complete(Now.AddHours(1));
        Assert.Throws<InvalidOperationException>(() => t.AssignTo("user-9"));
    }

    [Fact]
    public void Decline_from_todo_sets_declined()
    {
        var t = NewTask();
        t.Decline("not mine");
        Assert.Equal(TaskStatus.Declined, t.Status);
        Assert.Contains(t.DomainEvents, e => e is TaskDeclined);
    }

    [Fact]
    public void Decline_after_accept_throws()
    {
        var t = NewTask();
        t.Accept();
        Assert.Throws<InvalidOperationException>(() => t.Decline());
    }
}
