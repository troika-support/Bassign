namespace Bassign.Field.Core.Api;

/// <summary>Sign-in endpoints (called without a bearer token). Null = credentials/refresh rejected.
/// Network failures throw HttpRequestException / TaskCanceledException.</summary>
public interface IFieldAuthApi
{
    Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>Authenticated sync endpoints. Network failures throw; a final 401 throws HttpRequestException
/// with StatusCode = Unauthorized.</summary>
public interface IFieldSyncApi
{
    Task<PullResponse> PullAsync(long since, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PushResult>> PushAsync(IReadOnlyList<PushOperation> operations, CancellationToken cancellationToken = default);
    Task<string> UploadProofAsync(Guid opId, string filePath, CancellationToken cancellationToken = default);
}
