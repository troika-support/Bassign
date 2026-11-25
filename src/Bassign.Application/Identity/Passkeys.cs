using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Security;
using Bassign.Domain.Common;
using Mediator;

namespace Bassign.Application.Identity;

// Passkeys (decision #24): sign in on the website with a fingerprint, face or device PIN instead of
// password + code. Creating one and signing in with one are browser ceremonies handled by the Web host;
// listing and removing go through here.

public sealed record PasskeyInfo(string Id, string Name, DateTimeOffset CreatedAt);

public interface IPasskeys
{
    public const int MaxPerUser = 10;

    Task<IReadOnlyList<PasskeyInfo>> ListAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<Unit>> RemoveAsync(string userId, string passkeyId, CancellationToken cancellationToken = default);
    Task RemoveAllAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed record GetMyPasskeysQuery : IRequest<IReadOnlyList<PasskeyInfo>>;

public sealed class GetMyPasskeysHandler(IPasskeys passkeys, ICurrentUser currentUser) : IRequestHandler<GetMyPasskeysQuery, IReadOnlyList<PasskeyInfo>>
{
    public async ValueTask<IReadOnlyList<PasskeyInfo>> Handle(GetMyPasskeysQuery request, CancellationToken cancellationToken) =>
        await passkeys.ListAsync(currentUser.UserId, cancellationToken);
}

public sealed record RemoveMyPasskeyCommand(string PasskeyId) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "user.passkey.remove";
    public string AuditEntityType => "User";
    public string? AuditEntityId => null;
}

public sealed class RemoveMyPasskeyHandler(IPasskeys passkeys, ICurrentUser currentUser) : IRequestHandler<RemoveMyPasskeyCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(RemoveMyPasskeyCommand request, CancellationToken cancellationToken) =>
        await passkeys.RemoveAsync(currentUser.UserId, request.PasskeyId, cancellationToken);
}
