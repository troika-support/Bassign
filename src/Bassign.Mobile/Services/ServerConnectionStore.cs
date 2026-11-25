using Bassign.Field.Core.Api;
using Microsoft.Maui.Storage;

namespace Bassign.Mobile.Services;

/// <summary>The connected organization, kept in Preferences (not secret: a name and two addresses).
/// Debug builds fall back to the dev PC so the usual F5 workflow needs no connect step.</summary>
public sealed class ServerConnectionStore : IServerConnection
{
    private const string NameKey = "bassign.server.name";
    private const string ApiKey = "bassign.server.api";
    private const string WebKey = "bassign.server.web";

    public ServerConnectionStore()
    {
        var api = Preferences.Default.Get(ApiKey, "");
        if (Uri.TryCreate(api, UriKind.Absolute, out var uri))
            Current = new ServerInfo(Preferences.Default.Get(NameKey, "Bassign"), uri, Preferences.Default.Get(WebKey, uri.Authority));
#if DEBUG
        Current ??= new ServerInfo("Development", new Uri(MobileConfig.DevApiBaseUrl), "dev");
#endif
    }

    public ServerInfo? Current { get; private set; }

    public void Save(ServerInfo server)
    {
        Preferences.Default.Set(NameKey, server.OrganizationName);
        Preferences.Default.Set(ApiKey, server.ApiBase.ToString());
        Preferences.Default.Set(WebKey, server.WebHost);
        Current = server;
    }

    public void Clear()
    {
        Preferences.Default.Remove(NameKey);
        Preferences.Default.Remove(ApiKey);
        Preferences.Default.Remove(WebKey);
        Current = null;
    }
}

/// <summary>bassign://connect?server=… links arrive in MainActivity before (or while) Blazor runs; the
/// router picks them up from here. Only the host is taken — the Connect screen still asks to confirm.</summary>
public static class DeepLinks
{
    private static string? _pending;
    public static event Action? Arrived;

    public static void Receive(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)
            || !Uri.TryCreate(uri, UriKind.Absolute, out var link)
            || link.Scheme != "bassign" || link.Host != "connect") return;
        var server = System.Web.HttpUtility.ParseQueryString(link.Query)["server"];
        if (string.IsNullOrWhiteSpace(server) || server.Length > 253) return;
        _pending = server.Trim();
        Arrived?.Invoke();
    }

    /// <summary>The pending server host, once.</summary>
    public static string? Take()
    {
        var server = _pending;
        _pending = null;
        return server;
    }
}
