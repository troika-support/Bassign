using Bassign.Application.Common.Security;
using Bassign.Domain.Messaging;
using Mediator;

namespace Bassign.Application.Messaging.GetMyWhatsApp;

public sealed record GetMyWhatsAppQuery : IRequest<WhatsAppStatusVm>;

public sealed record WhatsAppStatusVm(bool Configured, bool Linked, string? MaskedPhone, DateTimeOffset? PendingCodeExpiresAt);

public sealed class GetMyWhatsAppHandler(
    IWhatsAppBindingRepository bindings, IWhatsAppSettings settings, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<GetMyWhatsAppQuery, WhatsAppStatusVm>
{
    public async ValueTask<WhatsAppStatusVm> Handle(GetMyWhatsAppQuery request, CancellationToken cancellationToken)
    {
        var binding = await bindings.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        var linked = binding is { Verified: true, PhoneE164: not null };
        var pending = binding?.LinkCodeExpiresAt is { } exp && exp > clock.GetUtcNow() ? exp : (DateTimeOffset?)null;
        return new WhatsAppStatusVm(
            settings.BusinessNumberE164 is not null,
            linked,
            linked ? PhoneNumbers.Mask(binding!.PhoneE164!) : null,
            pending);
    }
}
