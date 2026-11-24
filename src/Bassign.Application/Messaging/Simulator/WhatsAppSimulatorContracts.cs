using Bassign.Application.Common.Directory;
using Bassign.Domain.Common;
using Bassign.Domain.Messaging;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Messaging.Simulator;

public sealed record ConversationEntry(
    string Direction, string Kind, string Body, IReadOnlyList<WaButton> Actions, string Status, DateTimeOffset At);

/// <summary>Development-only: inject messages as if WhatsApp delivered them, and read the conversation log.</summary>
public interface IWhatsAppSimulator
{
    Task InjectAsync(string fromE164, string? text, string? replyId, string? replyTitle, string? mediaId, CancellationToken cancellationToken = default);
    Task<string> SavePhotoAsync(byte[] content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationEntry>> GetConversationAsync(string phoneE164, CancellationToken cancellationToken = default);
}

public sealed record SimulateInboundCommand(string FromE164, string? Text, string? ReplyId, string? ReplyTitle, byte[]? Photo)
    : IRequest<Result<Unit>>;

public sealed class SimulateInboundCommandValidator : AbstractValidator<SimulateInboundCommand>
{
    public SimulateInboundCommandValidator()
    {
        RuleFor(x => x.FromE164).Must(PhoneNumbers.IsE164).WithMessage("Use an international number like +263771234567.");
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Text) || x.ReplyId is not null || x.Photo is { Length: > 0 })
            .WithMessage("Type a message, tap a button, or attach a photo.");
        RuleFor(x => x.Photo).Must(p => p is null || p.Length <= 5 * 1024 * 1024).WithMessage("Photos must be under 5 MB.");
    }
}

public sealed class SimulateInboundHandler(IWhatsAppSimulator simulator, IWhatsAppSettings settings)
    : IRequestHandler<SimulateInboundCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SimulateInboundCommand request, CancellationToken cancellationToken)
    {
        if (!settings.IsSimulator)
            return new Error("whatsapp.not_simulator", "The simulator only works when WhatsApp:Provider is \"simulator\".");

        string? mediaId = null;
        if (request.Photo is { Length: > 0 } photo)
            mediaId = await simulator.SavePhotoAsync(photo, cancellationToken);

        await simulator.InjectAsync(request.FromE164, request.Text, request.ReplyId, request.ReplyTitle, mediaId, cancellationToken);
        return Unit.Value;
    }
}

public sealed record GetSimulatorConversationQuery(string PhoneE164) : IRequest<SimulatorConversationVm>;

public sealed record SimulatorConversationVm(bool Enabled, string? BusinessNumber, string? LinkedTo, IReadOnlyList<ConversationEntry> Entries);

public sealed class GetSimulatorConversationHandler(
    IWhatsAppSimulator simulator, IWhatsAppSettings settings, IWhatsAppBindingRepository bindings, IUserDirectory users)
    : IRequestHandler<GetSimulatorConversationQuery, SimulatorConversationVm>
{
    public async ValueTask<SimulatorConversationVm> Handle(GetSimulatorConversationQuery request, CancellationToken cancellationToken)
    {
        if (!settings.IsSimulator || !PhoneNumbers.IsE164(request.PhoneE164))
            return new SimulatorConversationVm(settings.IsSimulator, settings.BusinessNumberE164, null, []);

        var binding = await bindings.GetVerifiedByPhoneAsync(request.PhoneE164, cancellationToken);
        string? linkedTo = null;
        if (binding is not null)
            linkedTo = (await users.GetDisplayNamesAsync([binding.UserId], cancellationToken)).GetValueOrDefault(binding.UserId);

        var entries = await simulator.GetConversationAsync(request.PhoneE164, cancellationToken);
        return new SimulatorConversationVm(true, settings.BusinessNumberE164, linkedTo, entries);
    }
}
