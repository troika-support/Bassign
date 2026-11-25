using System.Net.Http.Json;

namespace Bassign.Field.Core.Api;

/// <summary>The organization this phone is connected to (decision #16: one at a time).</summary>
public sealed record ServerInfo(string OrganizationName, Uri ApiBase, string WebHost);

/// <summary>Where the connection is kept (Preferences on the phone). Changing it never happens silently:
/// the Connect screen shows the organization and server and asks first.</summary>
public interface IServerConnection
{
    ServerInfo? Current { get; }
    void Save(ServerInfo server);
    void Clear();
}

/// <summary>
/// Sends every API request to the connected organization's server. HttpClients are built once with a
/// placeholder base address; this swaps in the real one per request, so connecting or switching needs no
/// restart. With no connection, requests fail as "offline" — sync and branding already handle that.
/// </summary>
public sealed class ApiBaseHandler(IServerConnection connection) : DelegatingHandler
{
    public static readonly Uri Placeholder = new("https://bassign.invalid/");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (connection.Current is not { } server)
            throw new HttpRequestException("This phone isn't connected to an organisation yet.");
        if (request.RequestUri is { } uri && uri.Host == Placeholder.Host)
            request.RequestUri = Rebase(uri, server.ApiBase);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>https://bassign.invalid/sync/tasks?since=5 + https://api.client.co.zw/ → https://api.client.co.zw/sync/tasks?since=5.
    /// Keeps a path prefix on the API base (https://host/api/).</summary>
    public static Uri Rebase(Uri placeholderUri, Uri apiBase) => new(apiBase, placeholderUri.PathAndQuery.TrimStart('/'));
}

public sealed record ConnectInfoDto(string OrganizationName, string ApiBaseUrl, int Protocol);

public sealed record ConnectResult(ServerInfo? Server, string? Problem)
{
    public static ConnectResult Ok(ServerInfo server) => new(server, null);
    public static ConnectResult Fail(string problem) => new(null, problem);
}

/// <summary>
/// Turns what the person scanned or typed ("tasks.client.co.zw") into a confirmed server: reads
/// https://tasks.client.co.zw/connect/info, then checks the API it names actually answers. https only —
/// <paramref name="allowHttp"/> exists for debug builds talking to a dev PC.
/// </summary>
public sealed class ConnectDiscovery(HttpClient http, bool allowHttp = false)
{
    public const int SupportedProtocol = 1;

    public static bool TryNormalise(string? input, bool allowHttp, out Uri webBase, out string? problem)
    {
        webBase = ApiBaseHandler.Placeholder;
        problem = null;
        var text = (input ?? "").Trim();
        if (text.Length == 0) { problem = "Enter your organisation's Bassign address."; return false; }
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) || (!uri.Host.Contains('.', StringComparison.Ordinal) && !allowHttp))
        { problem = "That doesn't look like a web address. It's usually something like tasks.yourcompany.co.zw."; return false; }
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp))
        { problem = "Only secure (https) addresses can be used."; return false; }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        { problem = "That address isn't allowed."; return false; }

        webBase = new Uri($"{uri.Scheme}://{uri.Authority}/");
        return true;
    }

    public async Task<ConnectResult> DiscoverAsync(string? input, CancellationToken cancellationToken = default)
    {
        if (!TryNormalise(input, allowHttp, out var web, out var problem)) return ConnectResult.Fail(problem!);

        ConnectInfoDto? info;
        try
        {
            using var response = await http.GetAsync(new Uri(web, "connect/info"), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ConnectResult.Fail($"{web.Host} didn't answer as a Bassign server. Check the address with your manager.");
            info = await response.Content.ReadFromJsonAsync<ConnectInfoDto>(FieldJson.Options, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return ConnectResult.Fail($"Couldn't reach {web.Host}. Check the address and that you're online.");
        }

        if (info is null || string.IsNullOrWhiteSpace(info.OrganizationName) || info.Protocol > SupportedProtocol)
            return ConnectResult.Fail(info?.Protocol > SupportedProtocol
                ? "This organisation needs a newer version of the Bassign app. Update it from the Play Store."
                : $"{web.Host} didn't answer as a Bassign server.");
        if (!Uri.TryCreate(info.ApiBaseUrl, UriKind.Absolute, out var api)
            || (api.Scheme != Uri.UriSchemeHttps && !(allowHttp && api.Scheme == Uri.UriSchemeHttp)))
            return ConnectResult.Fail("This organisation's server isn't set up for the phone app yet (it needs a secure https address). Tell your manager.");

        var apiBase = new Uri(api.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        try
        {
            using var check = await http.GetAsync(new Uri(apiBase, "organization/branding"), cancellationToken);
            if (!check.IsSuccessStatusCode)
                return ConnectResult.Fail("Found the organisation, but its server isn't answering. Try again later.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ConnectResult.Fail("Found the organisation, but couldn't reach its server. Check you're online and try again.");
        }

        return ConnectResult.Ok(new ServerInfo(info.OrganizationName.Trim(), apiBase, web.Authority));
    }
}
