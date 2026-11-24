using Bassign.Domain.Tasks;

namespace Bassign.Application.Tasks.Contracts;

/// <summary>Full task read model returned by GetById.</summary>
public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Detail,
    DateTimeOffset? DueDate,
    string Status,
    string? AssigneeId,
    string CreatedById,
    bool RequiresProofPhoto,
    string? ProofPhotoRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Lighter row for the task list.</summary>
public sealed record TaskListItemDto(
    Guid Id,
    string Title,
    string Status,
    string? AssigneeId,
    string? AssigneeName,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt);

public static class TaskMappings
{
    public static TaskDto ToDto(this TaskItem t) => new(
        t.Id, t.Title, t.Detail, t.DueDate, t.Status.ToString(),
        t.AssigneeId, t.CreatedById, t.RequiresProofPhoto, t.ProofPhotoRef, t.CreatedAt, t.CompletedAt);

    public static TaskListItemDto ToListItem(this TaskItem t, string? assigneeName = null) => new(
        t.Id, t.Title, t.Status.ToString(), t.AssigneeId, assigneeName, t.DueDate, t.CreatedAt);
}
