using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Bassign.Field.Core.Api;

/// <summary>Uses a plain HttpClient (no auth handler) — these calls obtain the tokens.</summary>
public sealed class HttpFieldAuthApi(HttpClient http) : IFieldAuthApi
{
    public async Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("auth/login", new { email, password }, FieldJson.Options, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthTokens>(FieldJson.Options, cancellationToken);
    }

    public async Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("auth/refresh", new { refreshToken }, FieldJson.Options, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthTokens>(FieldJson.Options, cancellationToken);
    }
}

/// <summary>Uses the HttpClient whose pipeline includes <see cref="Auth.AuthRefreshHandler"/>.</summary>
public sealed class HttpFieldSyncApi(HttpClient http) : IFieldSyncApi
{
    public async Task<PullResponse> PullAsync(long since, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<PullResponse>(
            $"sync/tasks?since={since.ToString(CultureInfo.InvariantCulture)}", FieldJson.Options, cancellationToken)
        ?? new PullResponse(since, [], []);

    public async Task<IReadOnlyList<PushResult>> PushAsync(IReadOnlyList<PushOperation> operations, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("sync/push", new { operations }, FieldJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PushResult>>(FieldJson.Options, cancellationToken) ?? [];
    }

    public async Task<string> UploadProofAsync(Guid opId, string filePath, CancellationToken cancellationToken = default)
    {
        // Bytes (not a stream) so the request can be re-sent after a token refresh.
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(opId.ToString()), "opId" },
            { file, "file", Path.GetFileName(filePath) }
        };
        using var response = await http.PostAsync("sync/proof", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ProofUploaded>(FieldJson.Options, cancellationToken);
        return body?.Reference ?? throw new HttpRequestException("Upload returned no reference.");
    }

    private sealed record ProofUploaded(string Reference);
}
