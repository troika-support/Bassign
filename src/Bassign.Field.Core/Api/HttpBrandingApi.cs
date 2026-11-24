using System.Net.Http.Json;

namespace Bassign.Field.Core.Api;

public sealed record OrgBranding(string Name, string AccentHex);

/// <summary>Unauthenticated: the organization's name + accent, so the phone matches the web app.</summary>
public sealed class HttpBrandingApi(HttpClient http)
{
    public Task<OrgBranding?> GetAsync(CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<OrgBranding>("organization/branding", FieldJson.Options, cancellationToken);
}
