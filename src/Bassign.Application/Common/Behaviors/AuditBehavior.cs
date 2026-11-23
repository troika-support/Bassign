using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Security;
using Mediator;

namespace Bassign.Application.Common.Behaviors;

/// <summary>After a command succeeds, records an audit entry when the request is auditable.</summary>
public sealed class AuditBehavior<TMessage, TResponse>(IAuditSink audit, ICurrentUser currentUser)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken);

        if (message is IAuditableRequest auditable)
        {
            await audit.RecordAsync(
                auditable.AuditAction, currentUser.UserId, auditable.AuditEntityType,
                auditable.AuditEntityId, cancellationToken);
        }

        return response;
    }
}
