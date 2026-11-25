using Bassign.Application.Messaging;
using Bassign.Application.Tasks;
using Bassign.Application.Tasks.DeclineTask;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using Bassign.UnitTests.Notifications;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.UnitTests.Tasks;

public class TaskDefaultsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);

    private sealed class Repo(TaskItem task) : ITaskRepository
    {
        public void Add(TaskItem t) { }
        public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<TaskItem?>(id == task.Id ? task : null);
        public Task<IReadOnlyList<TaskItem>> ListAsync(TaskStatus? status, string? assigneeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([task]);
    }

    private sealed class Uow : Bassign.Application.Common.Persistence.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class Allow : ITaskAccess
    {
        public Task<bool> CanWorkOnAsync(TaskItem task, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static async Task<(Bassign.Domain.Common.Result<Mediator.Unit> Result, TaskItem Task)> DeclineAsync(string? reason, bool required)
    {
        var task = TaskItem.Create("Fix the gate", "mgr-1", Now);
        task.AssignTo("staff-1");
        var org = Seed.Org();
        org.ConfigureTaskDefaults(proofRequiredByDefault: false, declineReasonRequired: required);
        var handler = new DeclineTaskHandler(new Repo(task), new Uow(), new Allow(), new OrgStore(org));
        return (await handler.Handle(new DeclineTaskCommand(task.Id, reason), TestContext.Current.CancellationToken), task);
    }

    [Fact]
    public async Task A_reason_is_required_by_default_setting()
    {
        var (result, task) = await DeclineAsync("   ", required: true);
        Assert.False(result.IsSuccess);
        Assert.Equal("task.reason_required", result.Error.Code);
        Assert.Equal(TaskStatus.Todo, task.Status);
    }

    [Fact]
    public async Task The_reason_is_stored_on_the_task()
    {
        var (result, task) = await DeclineAsync(" Too busy ", required: true);
        Assert.True(result.IsSuccess);
        Assert.Equal(TaskStatus.Declined, task.Status);
        Assert.Equal("Too busy", task.DeclineReason);
    }

    [Fact]
    public async Task With_the_rule_off_no_reason_is_fine()
    {
        var (result, task) = await DeclineAsync(null, required: false);
        Assert.True(result.IsSuccess);
        Assert.Null(task.DeclineReason);
    }

    [Fact]
    public void Defaults_are_proof_off_reason_on_due_at_end_of_working_day()
    {
        var org = OrganizationSettings.CreateDefault();
        Assert.False(org.ProofRequiredByDefault);
        Assert.True(org.DeclineReasonRequired);
        Assert.Equal(TimeSpan.FromHours(17), org.DefaultDueTime);

        org.ConfigureCalendar(OrganizationSettings.MondayToFriday, 7, 24, true, true);
        Assert.Equal(new TimeSpan(23, 59, 0), org.DefaultDueTime);   // "until midnight" → 23:59, still that day
    }

    [Fact]
    public void Category_names_are_tidied_and_limited()
    {
        Assert.Equal("Site visits", TaskCategory.Create("  Site    visits ", Now).Name);
        Assert.Throws<ArgumentException>(() => TaskCategory.Create(" ", Now));
        Assert.Throws<ArgumentException>(() => TaskCategory.Create(new string('x', 41), Now));
    }

    [Fact]
    public void Changing_the_category_works_on_a_closed_task_too()
    {
        var task = TaskItem.Create("Fix the gate", "mgr-1", Now);
        task.Decline("Too busy");
        var id = Guid.NewGuid();
        task.ChangeCategory(id);
        Assert.Equal(id, task.CategoryId);
    }

    [Fact]
    public void Long_reasons_are_cut_to_fit()
    {
        var task = TaskItem.Create("Fix the gate", "mgr-1", Now);
        task.Decline(new string('a', 900));
        Assert.Equal(TaskItem.DeclineReasonMaxLength, task.DeclineReason!.Length);
    }

    [Theory]
    [InlineData("busy", "Too busy")]
    [InlineData("deadline", "Can't make the deadline")]
    [InlineData("other", null)]
    [InlineData("nonsense", null)]
    public void Reason_codes_map_to_labels(string code, string? label) => Assert.Equal(label, DeclineReasons.LabelFor(code));

    [Fact]
    public void Reply_ids_carry_an_optional_argument()
    {
        var id = Guid.NewGuid();
        Assert.True(WhatsAppReplyIds.TryParse($"decline:{id}:busy", out var action, out var taskId, out var arg));
        Assert.Equal(("decline", id, "busy"), (action, taskId, arg));

        Assert.True(WhatsAppReplyIds.TryParse($"accept:{id}", out action, out taskId, out arg));
        Assert.Equal(("accept", id, (string?)null), (action, taskId, arg));

        Assert.False(WhatsAppReplyIds.TryParse($"explode:{id}:x", out _, out _, out _));
    }
}
