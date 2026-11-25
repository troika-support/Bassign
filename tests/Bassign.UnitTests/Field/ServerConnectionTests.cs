using System.Net;
using System.Text;
using Bassign.Field.Core.Api;

namespace Bassign.UnitTests.Field;

public class ServerConnectionTests
{
    /// <summary>Answers by URL; anything unlisted is "unreachable".</summary>
    private sealed class FakeServers(Dictionary<string, (HttpStatusCode Status, string Body)> routes) : HttpMessageHandler
    {
        public List<Uri> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(request.RequestUri!);
            if (!routes.TryGetValue(request.RequestUri!.ToString(), out var r))
                throw new HttpRequestException("unreachable");
            return Task.FromResult(new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") });
        }
    }

    private const string Info = "https://tasks.client.co.zw/connect/info";
    private const string Branding = "https://api.client.co.zw/organization/branding";

    private static ConnectDiscovery Discovery(Dictionary<string, (HttpStatusCode, string)> routes, bool allowHttp = false) =>
        new(new HttpClient(new FakeServers(routes)), allowHttp);

    private static Task<ConnectResult> Discover(ConnectDiscovery d, string input) => d.DiscoverAsync(input, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("tasks.client.co.zw", "https://tasks.client.co.zw/")]
    [InlineData("  https://tasks.client.co.zw/connect?x=1 ", "https://tasks.client.co.zw/")]
    [InlineData("TASKS.Client.co.zw:8443", "https://tasks.client.co.zw:8443/")]
    public void Addresses_are_normalised_to_https_site_roots(string input, string expected)
    {
        Assert.True(ConnectDiscovery.TryNormalise(input, allowHttp: false, out var web, out _));
        Assert.Equal(expected, web.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://tasks.client.co.zw")]          // https only
    [InlineData("https://user:pw@tasks.client.co.zw")] // no credentials in links
    [InlineData("not an address")]
    [InlineData("localhost")]
    public void Bad_addresses_are_refused(string input) =>
        Assert.False(ConnectDiscovery.TryNormalise(input, allowHttp: false, out _, out _));

    [Fact]
    public async Task A_good_server_is_found_and_its_api_checked()
    {
        var d = Discovery(new()
        {
            [Info] = (HttpStatusCode.OK, """{"organizationName":"Minted Moments","apiBaseUrl":"https://api.client.co.zw","protocol":1}"""),
            [Branding] = (HttpStatusCode.OK, """{"name":"Minted Moments","accentHex":"#4F46E5"}"""),
        });

        var result = await Discover(d, "tasks.client.co.zw");

        Assert.Null(result.Problem);
        Assert.Equal("Minted Moments", result.Server!.OrganizationName);
        Assert.Equal("https://api.client.co.zw/", result.Server.ApiBase.ToString());
        Assert.Equal("tasks.client.co.zw", result.Server.WebHost);
    }

    [Fact]
    public async Task An_http_api_address_is_refused_in_release_builds()
    {
        var d = Discovery(new()
        {
            [Info] = (HttpStatusCode.OK, """{"organizationName":"Minted Moments","apiBaseUrl":"http://api.client.co.zw/","protocol":1}"""),
        });
        Assert.Contains("https", (await Discover(d, "tasks.client.co.zw")).Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_newer_protocol_asks_for_an_app_update()
    {
        var d = Discovery(new()
        {
            [Info] = (HttpStatusCode.OK, """{"organizationName":"Minted Moments","apiBaseUrl":"https://api.client.co.zw/","protocol":2}"""),
        });
        Assert.Contains("newer version", (await Discover(d, "tasks.client.co.zw")).Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreachable_servers_give_a_plain_message()
    {
        var result = await Discover(Discovery(new()), "tasks.client.co.zw");
        Assert.Null(result.Server);
        Assert.Contains("Couldn't reach tasks.client.co.zw", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dead_api_is_caught_before_connecting()
    {
        var d = Discovery(new()
        {
            [Info] = (HttpStatusCode.OK, """{"organizationName":"Minted Moments","apiBaseUrl":"https://api.client.co.zw/","protocol":1}"""),
        });
        Assert.Contains("couldn't reach its server", (await Discover(d, "tasks.client.co.zw")).Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://bassign.invalid/sync/tasks?since=5", "https://api.client.co.zw/", "https://api.client.co.zw/sync/tasks?since=5")]
    [InlineData("https://bassign.invalid/auth/login", "https://client.co.zw/api/", "https://client.co.zw/api/auth/login")]
    public void Requests_are_rebased_onto_the_connected_server(string request, string apiBase, string expected) =>
        Assert.Equal(expected, ApiBaseHandler.Rebase(new Uri(request), new Uri(apiBase)).ToString());
}
