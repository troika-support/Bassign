using Bassign.Application.Ai;
using Bassign.Application.Common.Directory;
using Bassign.Application.Notifications;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.UnitTests.Notifications;

/// <summary>A clock the test moves by hand.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class TaskStore : ITaskRepository
{
    public List<TaskItem> All { get; } = [];
    public void Add(TaskItem task) => All.Add(task);
    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(All.FirstOrDefault(t => t.Id == id));
    public Task<IReadOnlyList<TaskItem>> ListAsync(TaskStatus? status, string? assigneeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(All.Where(t => (status is null || t.Status == status) && (assigneeId is null || t.AssigneeId == assigneeId)).ToList());
}

public sealed class OrgStore(OrganizationSettings org) : IOrganizationRepository
{
    public List<OrgClosure> Closures { get; } = [];
    public Task<OrganizationSettings?> FindAsync(CancellationToken cancellationToken = default) => Task.FromResult<OrganizationSettings?>(org);
    public void Add(OrganizationSettings settings) { }
    public Task<IReadOnlyList<OrgClosure>> ListClosuresAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OrgClosure>>(Closures.Where(c => c.Date >= from && c.Date <= to).OrderBy(c => c.Date).ToList());
    public Task<OrgClosure?> FindClosureAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Closures.FirstOrDefault(c => c.Id == id));
    public void AddClosure(OrgClosure closure) => Closures.Add(closure);
    public void RemoveClosure(OrgClosure closure) => Closures.Remove(closure);
}

public sealed class People : IUserDirectory
{
    public List<(string Id, string Name, string Role)> All { get; } =
    [
        ("mgr-1", "Rudo Moyo", "Manager"),
        ("adm-1", "Admin User", "Admin"),
        ("staff-1", "Tendai Ncube", "Staff")
    ];

    public Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(All.Where(p => userIds.Contains(p.Id)).ToDictionary(p => p.Id, p => p.Name));
    public Task<IReadOnlyList<DirectoryUser>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DirectoryUser>>(All.Select(p => new DirectoryUser(p.Id, p.Name)).ToList());
    public Task<IReadOnlyList<DirectoryUser>> ListActiveInRolesAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DirectoryUser>>(All.Where(p => roles.Contains(p.Role)).Select(p => new DirectoryUser(p.Id, p.Name)).ToList());
}

public sealed class FakeOutbox : INotificationOutbox
{
    public List<NotificationRequest> Queued { get; } = [];

    public void Enqueue(string kind, string userId, Guid? taskId, DateTimeOffset nowUtc) =>
        Queued.Add(new NotificationRequest(kind, userId, taskId, $"plain:{Guid.NewGuid()}", null, nowUtc));

    public Task<bool> EnqueueOnceAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (Queued.Any(q => q.DedupeKey == request.DedupeKey)) return Task.FromResult(false);
        Queued.Add(request);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Queued.Any(q => q.DedupeKey == dedupeKey));

    public Task<DateTimeOffset?> LastQueuedAtAsync(string kind, Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Queued.Where(q => q.Kind == kind && q.TaskId == taskId).Select(q => (DateTimeOffset?)q.NowUtc).Max());
}

public sealed class FakeSummaries : ISummaryGenerator
{
    public int Calls { get; private set; }
    public Task<SummaryResult> SummarizeDigestAsync(DigestFacts facts, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new SummaryResult(AiOutcome.Ok, $"{facts.Overdue} overdue — chase them first."));
    }
}

public static class Seed
{
    /// <summary>Org in Harare (UTC+2, no DST) with the spec's default policy.</summary>
    public static OrganizationSettings Org()
    {
        var org = OrganizationSettings.CreateDefault();
        org.Configure("Minted Moments", "#4F46E5", "Africa/Harare", DateTimeOffset.UnixEpoch);
        return org;
    }

    public static TaskItem Task(DateTimeOffset createdAt, DateTimeOffset due, string? assignee = "staff-1", string title = "Fix the gate")
    {
        var t = TaskItem.Create(title, "mgr-1", createdAt, dueDate: due);
        if (assignee is not null) t.AssignTo(assignee);
        return t;
    }
}
