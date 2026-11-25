namespace Bassign.Field.Core.Api;

/// <summary>Sign-in endpoints (called without a bearer token). Null = credentials/refresh rejected.
/// Network failures throw HttpRequestException / TaskCanceledException.</summary>
public interface IFieldAuthApi
{
    /// <summary>Throws <see cref="TwoFactorNeededException"/> when the password was right and a code is needed
    /// next, and <see cref="SignInRefusedException"/> when the server gives a reason to show (locked out,
    /// two-factor not set up yet).</summary>
    Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Second step of sign-in. Throws <see cref="SignInRefusedException"/> with the reason when refused.</summary>
    Task<AuthTokens> VerifyTwoFactorAsync(string ticket, string code, bool recovery, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>Email codes: send another. Throws <see cref="SignInRefusedException"/> when refused.</summary>
    Task ResendTwoFactorCodeAsync(string ticket, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>The password was accepted; the server now wants a code. <paramref name="method"/> is "app" or "email".</summary>
public sealed class TwoFactorNeededException(string ticket, string method) : Exception("Two-factor code needed.")
{
    public string Ticket { get; } = ticket;
    public string Method { get; } = method;
}

/// <summary>The server refused and said why. <paramref name="code"/> is the server's error code (e.g. auth.ticket_expired).</summary>
public sealed class SignInRefusedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Authenticated sync endpoints. Network failures throw; a final 401 throws HttpRequestException
/// with StatusCode = Unauthorized.</summary>
public interface IFieldSyncApi
{
    Task<PullResponse> PullAsync(long since, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PushResult>> PushAsync(IReadOnlyList<PushOperation> operations, CancellationToken cancellationToken = default);
    Task<string> UploadProofAsync(Guid opId, string filePath, CancellationToken cancellationToken = default);
}
