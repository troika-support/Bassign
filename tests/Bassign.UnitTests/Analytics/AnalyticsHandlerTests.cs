using Bassign.Application.Analytics;
using Bassign.Application.Common.Directory;
using Bassign.Application.Tasks;
using Bassign.Domain.Tasks;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.UnitTests.Analytics;

public class AnalyticsHandlerTests
{
    private sealed class FakeTaskRepository(IEnumerable<TaskItem> seed) : ITaskRepository
    {
        private readonly List<TaskItem> _items = seed.ToList();
        public void Add(TaskItem task) => _items.Add(task);
        public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.FirstOrDefault(t => t.Id == id));
        public Task<IReadOnlyList<TaskItem>> ListAsync(TaskStatus? status, string? assigneeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(_items);
    }

    private sealed class FakeUserDirectory : IUserDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(ids.ToDictionary(i => i, i => i.ToUpperInvariant()));
        public Task<IReadOnlyList<DirectoryUser>> ListActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DirectoryUser>>([]);
    }

    private static TaskItem DoneTask(string assignee, DateTimeOffset created, DateTimeOffset due, DateTimeOffset completed)
    {
        var t = TaskItem.Create("T", "mgr", created, dueDate: due);
        t.AssignTo(assignee);
        t.Accept();
        t.Complete(completed);
        return t;
    }

    [Fact]
    public async Task Computes_completed_count_and_on_time_percentage()
    {
        var now = DateTimeOffset.UtcNow;
        var onTime = DoneTask("s1", now.AddHours(-2), now.AddHours(5), now);   // completed before due
        var late = DoneTask("s2", now.AddHours(-10), now.AddHours(-1), now);   // completed after due

        var handler = new GetAnalyticsHandler(
            new FakeTaskRepository([onTime, late]), new FakeUserDirectory(), TimeProvider.System);

        var vm = await handler.Handle(new GetAnalyticsQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(2, vm.Metrics.Completed);
        Assert.Equal(50.0, vm.Metrics.OnTimePct);
        Assert.Equal(2, vm.CompletionByPerson.Count);
    }
}
