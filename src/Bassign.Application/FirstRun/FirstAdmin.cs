using System.Security.Cryptography;
using System.Text;
using Bassign.Application.Common.Auditing;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using FluentValidation;
using Mediator;

namespace Bassign.Application.FirstRun;

/// <summary>The one-time setup code (Setup__Code on the server, decision #15). Null/empty = not set.</summary>
public interface ISetupCode
{
    string? Value { get; }
}

public sealed record FirstRunState(bool AdminExists, bool SetupCodeConfigured);

/// <summary>Anonymous: drives the sign-in page (send a fresh install to setup) and the setup page itself.</summary>
public sealed record GetFirstRunStateQuery : IRequest<FirstRunState>;

public sealed class GetFirstRunStateHandler(IIdentityAdmin identity, ISetupCode setupCode) : IRequestHandler<GetFirstRunStateQuery, FirstRunState>
{
    public async ValueTask<FirstRunState> Handle(GetFirstRunStateQuery request, CancellationToken cancellationToken) =>
        new(await identity.AnyActiveAdminAsync(cancellationToken), !string.IsNullOrWhiteSpace(setupCode.Value));
}

/// <summary>
/// Creates the first Admin on a fresh install. Works only while there is no active Admin and only with the
/// setup code the installer set on the server — so a stranger who finds a fresh install can't claim it, and
/// once an Admin exists this door is shut for good (new people join by invite).
/// </summary>
public sealed record CreateFirstAdminCommand(string SetupCode, string DisplayName, string Email, string Password)
    : IRequest<Result<string>>, IAuditableRequest
{
    public string AuditAction => "setup.first_admin";
    public string AuditEntityType => "User";
    public string? AuditEntityId => Email.Trim().ToLowerInvariant();
}

public sealed class CreateFirstAdminValidator : AbstractValidator<CreateFirstAdminCommand>
{
    public CreateFirstAdminValidator()
    {
        RuleFor(x => x.SetupCode).NotEmpty().WithMessage("Enter the setup code.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100).WithMessage("Enter your name.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).WithMessage("Use a password of at least 10 characters.");
    }
}

public sealed class CreateFirstAdminHandler(IIdentityAdmin identity, ISetupCode setupCode, Privacy.IConsentRegister consents,
    Common.Persistence.IUnitOfWork uow, TimeProvider clock)
    : IRequestHandler<CreateFirstAdminCommand, Result<string>>
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);   // two people racing on a fresh install

    public static readonly Error Closed = new("setup.closed", "This Bassign is already set up. Sign in, or ask your Admin for an invite.");

    public async ValueTask<Result<string>> Handle(CreateFirstAdminCommand request, CancellationToken cancellationToken)
    {
        var expected = setupCode.Value;
        if (string.IsNullOrWhiteSpace(expected))
            return new Error("setup.no_code", "The setup code hasn't been set on the server yet (Setup__Code). Ask whoever installed Bassign.");

        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            if (await identity.AnyActiveAdminAsync(cancellationToken))
                return Closed;
            if (!CodesMatch(request.SetupCode, expected))
                return new Error("setup.bad_code", "That setup code isn't right. Check it with whoever installed Bassign.");

            var email = request.Email.Trim().ToLowerInvariant();
            var created = await identity.CreateUserAsync(email, request.DisplayName, request.Password, TeamRoles.Admin, cancellationToken);
            if (!created.IsSuccess) return created.Error;

            consents.Record(created.Value!, Privacy.ConsentPurposes.PrivacyNotice, granted: true, source: "setup", at: clock.GetUtcNow());
            await uow.SaveChangesAsync(cancellationToken);
            return created.Value!;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>Constant-time, ignoring surrounding spaces and dashes/case so "ABCD-1234" and "abcd1234" both work.</summary>
    public static bool CodesMatch(string given, string expected)
    {
        static byte[] Hash(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant()));
        return CryptographicOperations.FixedTimeEquals(Hash(given), Hash(expected));
    }
}
