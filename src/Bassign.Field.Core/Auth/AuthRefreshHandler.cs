using System.Net;
using System.Net.Http.Headers;

namespace Bassign.Field.Core.Auth;

/// <summary>Adds the bearer token; on a 401, refreshes once and retries the request once.</summary>
public sealed class AuthRefreshHandler(FieldSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await session.GetAccessTokenAsync();
        Authorize(request, token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
            return response;

        if (!await session.TryRefreshAsync(token, cancellationToken))
            return response;   // surface the 401: offline or signed out

        response.Dispose();
        using var retry = Clone(request);
        Authorize(retry, await session.GetAccessTokenAsync());
        return await base.SendAsync(retry, cancellationToken);
    }

    private static void Authorize(HttpRequestMessage request, string? token) =>
        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);

    // A sent HttpRequestMessage can't be sent again; the content (JSON / bytes) is safely re-serialisable.
    private static HttpRequestMessage Clone(HttpRequestMessage original)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Content = original.Content,
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };
        foreach (var header in original.Headers.Where(h => h.Key != "Authorization"))
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }
}
