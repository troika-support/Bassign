using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Tasks;
using Bassign.Application.Tasks.AcceptTask;
using Bassign.Application.Tasks.CompleteTask;
using Bassign.Application.Tasks.CreateTask;
using Bassign.Application.Tasks.Settings;
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

    private sealed class FakeCategories : ITaskCategoryRepository
    {
        public List<TaskCategory> All { get; } = [];
        public Task<IReadOnlyList<TaskCategory>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskCategory>>(All.Where(c => includeArchived || !c.IsArchived).ToList());
        public Task<TaskCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(All.FirstOrDefault(c => c.Id == id));
        public void Add(TaskCategory category) => All.Add(category);
    }

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
        var handler = new CreateTaskHandler(repo, new FakeCategories(), uow, new FakeCurrentUser("mgr-1"), TimeProvider.System);

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
        var handler = new CreateTaskHandler(repo, new FakeCategories(), uow, new FakeCurrentUser("mgr-1"), TimeProvider.System);

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

    [Fact]
    public async Task Create_saves_the_category()
    {
        var repo = new FakeTaskRepository();
        var categories = new FakeCategories();
        var maintenance = TaskCategory.Create("Maintenance", DateTimeOffset.UtcNow);
        categories.Add(maintenance);
        var handler = new CreateTaskHandler(repo, categories, new FakeUnitOfWork(), new FakeCurrentUser("mgr-1"), TimeProvider.System);

        var result = await handler.Handle(new CreateTaskCommand("Fix the gate", null, null, false, null, maintenance.Id), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(maintenance.Id, repo.Store[result.Value].CategoryId);
    }

    [Fact]
    public async Task Create_rejects_an_archived_or_unknown_category()
    {
        var categories = new FakeCategories();
        var old = TaskCategory.Create("Old", DateTimeOffset.UtcNow);
        old.Archive();
        categories.Add(old);
        var handler = new CreateTaskHandler(new FakeTaskRepository(), categories, new FakeUnitOfWork(), new FakeCurrentUser("mgr-1"), TimeProvider.System);

        Assert.Equal("task.invalid_category", (await handler.Handle(new CreateTaskCommand("A", null, null, false, null, old.Id), Ct)).Error.Code);
        Assert.Equal("task.invalid_category", (await handler.Handle(new CreateTaskCommand("B", null, null, false, null, Guid.NewGuid()), Ct)).Error.Code);
    }
}
