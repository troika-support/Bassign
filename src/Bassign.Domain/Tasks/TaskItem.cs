using Bassign.Domain.Common;

namespace Bassign.Domain.Tasks;

/// <summary>
/// The task aggregate. Business rules live here as guarded methods; illegal transitions throw,
/// so invariants cannot be violated from a handler. No EF/ASP.NET — tenancy does not exist in
/// this single-organization app. "Now" is passed in (never DateTime.Now) to keep the domain pure.
/// </summary>
public sealed class TaskItem : IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; private init; }
    public string Title { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public TaskStatus Status { get; private set; }
    public string? AssigneeId { get; private set; }
    public string CreatedById { get; private init; } = string.Empty;
    public bool RequiresProofPhoto { get; private init; }
    public string? ProofPhotoRef { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    public void ClearDomainEvents() => _events.Clear();

    private TaskItem() { }   // EF (step 03)

    public static TaskItem Create(
        string title,
        string createdById,
        DateTimeOffset nowUtc,
        string? detail = null,
        DateTimeOffset? dueDate = null,
        bool requiresProofPhoto = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(createdById))
            throw new ArgumentException("CreatedById is required.", nameof(createdById));
        if (dueDate is { } due && due <= nowUtc)
            throw new ArgumentException("Due date must be in the future.", nameof(dueDate));

        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            Title = title.Trim(),
            Detail = detail,
            DueDate = dueDate,
            Status = TaskStatus.Todo,
            CreatedById = createdById,
            RequiresProofPhoto = requiresProofPhoto,
            CreatedAt = nowUtc
        };
        task._events.Add(new TaskCreated(task.Id, task.Title));
        return task;
    }

    public void UpdateDetails(string title, string? detail, DateTimeOffset? dueDate, DateTimeOffset nowUtc)
    {
        if (Status is TaskStatus.Done or TaskStatus.Declined)
            throw new InvalidOperationException("Cannot edit a closed task.");
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (dueDate is { } due && due <= nowUtc)
            throw new ArgumentException("Due date must be in the future.", nameof(dueDate));

        Title = title.Trim();
        Detail = detail;
        DueDate = dueDate;
        _events.Add(new TaskUpdated(Id));
    }

    public void AssignTo(string userId)
    {
        if (Status is TaskStatus.Done or TaskStatus.Declined)
            throw new InvalidOperationException("Cannot reassign a closed task.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));

        AssigneeId = userId;
        _events.Add(new TaskAssigned(Id, userId));
    }

    public void Accept()
    {
        if (Status != TaskStatus.Todo)
            throw new InvalidOperationException("Only a to-do task can be accepted.");

        Status = TaskStatus.Accepted;
        _events.Add(new TaskAccepted(Id));
    }

    public void Complete(DateTimeOffset nowUtc, string? proofPhotoRef = null)
    {
        if (Status == TaskStatus.Done)
            return;   // idempotent — completing an already-done task is a no-op
        if (Status != TaskStatus.Accepted)
            throw new InvalidOperationException("Only an accepted task can be completed.");
        if (RequiresProofPhoto && string.IsNullOrWhiteSpace(proofPhotoRef))
            throw new InvalidOperationException("A proof-of-work photo is required to complete this task.");

        Status = TaskStatus.Done;
        ProofPhotoRef = proofPhotoRef;
        CompletedAt = nowUtc;
        _events.Add(new TaskCompleted(Id, proofPhotoRef));
    }

    public void Decline(string? reason = null)
    {
        if (Status != TaskStatus.Todo)
            throw new InvalidOperationException("Only a to-do task can be declined.");

        Status = TaskStatus.Declined;
        _events.Add(new TaskDeclined(Id, reason));
    }
}
