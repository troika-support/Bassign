using Bassign.Domain.Common;

namespace Bassign.Domain.Tasks;

public sealed record TaskCreated(Guid TaskId, string Title) : IDomainEvent;
public sealed record TaskAssigned(Guid TaskId, string AssigneeId) : IDomainEvent;
public sealed record TaskAccepted(Guid TaskId) : IDomainEvent;
public sealed record TaskCompleted(Guid TaskId, string? ProofPhotoRef) : IDomainEvent;
public sealed record TaskDeclined(Guid TaskId, string? Reason) : IDomainEvent;
public sealed record TaskUpdated(Guid TaskId) : IDomainEvent;
