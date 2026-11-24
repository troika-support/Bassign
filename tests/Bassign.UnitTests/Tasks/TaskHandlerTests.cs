using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Tasks;
using Bassign.Application.Tasks.AcceptTask;
using Bassign.Application.Tasks.CompleteTask;
using Bassign.Application.Tasks.CreateTask;
using Bassign.Domain.Tasks;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.UnitTests.Tasks;

public class TaskHandlerTests
{
    private sealed class FakeTaskRepository : ITaskRepository
    {
        public readonly Dictionary<Guid, TaskItem> Store = [];
        public void Add(TaskItem task) => Store[task.Id] = task;
        public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.GetValueOrDefault(id));
        public Task<IReadOnlyList<TaskItem>> ListAsync(TaskStatus? status, string? assigneeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(Store.Values.ToList());
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class FakeCurrentUser(string id) : ICurrentUser { public string UserId => id; }

    private sealed class FakeTaskAccess(bool allow) : ITaskAccess
    {
        public Task<bool> CanWorkOnAsync(TaskItem task, CancellationToken ct = default) => Task.FromResult(allow);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_persists_task_and_returns_id()
    {
        var repo = new FakeTaskRepository();
        var uow = new FakeUnitOfWork();
        var handler = new CreateTaskHandler(repo, uow, new FakeCurrentUser("mgr-1"), TimeProvider.System);

        var result = await handler.Handle(
            new CreateTaskCommand("Fix the gate", null, DateTimeOffset.UtcNow.AddDays(1), false, "staff-7"), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(repo.Store.ContainsKey(result.Value));
        Assert.Equal("staff-7", repo.Store[result.Value].AssigneeId);
        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task Create_in_the_past_returns_invalid_without_saving()
    {
        var repo = new FakeTaskRepository();
        var uow = new FakeUnitOfWork();
        var handler = new CreateTaskHandler(repo, uow, new FakeCurrentUser("mgr-1"), TimeProvider.System);

        var result = await handler.Handle(
            new CreateTaskCommand("Late task", null, DateTimeOffset.UtcNow.AddDays(-1), false, null), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal("task.invalid", result.Error.Code);
        Assert.Empty(repo.Store);
        Assert.Equal(0, uow.Saves);
    }

    [Fact]
    public async Task Complete_returns_not_found_when_task_missing()
    {
        var handler = new CompleteTaskHandler(new FakeTaskRepository(), new FakeUnitOfWork(), TimeProvider.System, new FakeTaskAccess(allow: true));
        var result = await handler.Handle(new CompleteTaskCommand(Guid.NewGuid(), null), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal("task.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Accept_on_a_done_task_returns_invalid_state()
    {
        var repo = new FakeTaskRepository();
        var task = TaskItem.Create("T", "mgr-1", DateTimeOffset.UtcNow);
        task.Accept();
        task.Complete(DateTimeOffset.UtcNow);
        repo.Add(task);

        var handler = new AcceptTaskHandler(repo, new FakeUnitOfWork(), new FakeTaskAccess(allow: true));
        var result = await handler.Handle(new AcceptTaskCommand(task.Id), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal("task.invalid_state", result.Error.Code);
    }

    [Fact]
    public async Task Staff_cannot_complete_someone_elses_task()
    {
        var repo = new FakeTaskRepository();
        var task = TaskItem.Create("T", "mgr-1", DateTimeOffset.UtcNow);
        task.AssignTo("staff-2");
        task.Accept();
        repo.Add(task);
        var uow = new FakeUnitOfWork();

        var handler = new CompleteTaskHandler(repo, uow, TimeProvider.System, new FakeTaskAccess(allow: false));
        var result = await handler.Handle(new CompleteTaskCommand(task.Id, null), Ct);

        Assert.Equal("task.forbidden", result.Error.Code);
        Assert.Equal(TaskStatus.Accepted, task.Status);
        Assert.Equal(0, uow.Saves);
    }
}
