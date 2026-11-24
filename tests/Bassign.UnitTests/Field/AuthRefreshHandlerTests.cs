using System.Net;
using Bassign.Field.Core.Auth;
using static Bassign.UnitTests.Field.FieldTestData;

namespace Bassign.UnitTests.Field;

public class AuthRefreshHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubServer(Func<HttpRequestMessage, HttpStatusCode> respond) : HttpMessageHandler
    {
        public List<string?> SeenTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SeenTokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(respond(request)));
        }
    }

    [Fact]
    public async Task A_401_refreshes_once_and_retries_with_the_new_token()
    {
        var tokens = new InMemoryTokenStore(Tokens("old-access", "old-refresh"));
        var auth = new FakeAuthApi { Refresh = r => r == "old-refresh" ? Tokens("new-access", "new-refresh") : null };
        var server = new StubServer(req =>
            req.Headers.Authorization?.Parameter == "new-access" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        using var http = new HttpClient(new AuthRefreshHandler(new FieldSession(tokens, auth)) { InnerHandler = server })
        {
            BaseAddress = new Uri("http://bassign.test/")
        };

        using var response = await http.GetAsync("sync/tasks", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new string?[] { "old-access", "new-access" }, server.SeenTokens);
        Assert.Equal(1, auth.RefreshCalls);
        Assert.Equal("new-refresh", tokens.Current!.RefreshToken);
    }

    [Fact]
    public async Task A_rejected_refresh_signs_out_without_retrying_again()
    {
        var tokens = new InMemoryTokenStore(Tokens("old-access", "old-refresh"));
        var session = new FieldSession(tokens, new FakeAuthApi { Refresh = _ => null });
        var signedOut = false;
        session.SignedOut += () => signedOut = true;
        var server = new StubServer(_ => HttpStatusCode.Unauthorized);
        using var http = new HttpClient(new AuthRefreshHandler(session) { InnerHandler = server })
        {
            BaseAddress = new Uri("http://bassign.test/")
        };

        using var response = await http.GetAsync("sync/tasks", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(server.SeenTokens);
        Assert.Null(tokens.Current);
        Assert.True(signedOut);
    }
}
