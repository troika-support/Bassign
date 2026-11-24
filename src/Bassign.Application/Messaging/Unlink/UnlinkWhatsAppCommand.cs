using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Domain.Common;
using Mediator;

namespace Bassign.Application.Messaging.Unlink;

public sealed record UnlinkWhatsAppCommand : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "whatsapp.unlink";
    public string AuditEntityType => "WhatsAppBinding";
    public string? AuditEntityId => null;
}

public sealed class UnlinkWhatsAppHandler(
    IWhatsAppBindingRepository bindings, IUnitOfWork uow, ICurrentUser currentUser, Privacy.IConsentRegister consents, TimeProvider clock)
    : IRequestHandler<UnlinkWhatsAppCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(UnlinkWhatsAppCommand request, CancellationToken cancellationToken)
    {
        var binding = await bindings.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (binding is null)
            return Unit.Value;   // nothing linked: already in the desired state

        binding.Unlink();
        consents.Record(currentUser.UserId, Privacy.ConsentPurposes.WhatsApp, granted: false, source: "profile:unlink", at: clock.GetUtcNow());
        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
