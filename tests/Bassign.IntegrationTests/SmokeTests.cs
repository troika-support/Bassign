namespace Bassign.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SmokeTests(BassignApiFactory api)
{
    [Fact]
    public async Task Api_is_live_and_ready()
    {
        var client = api.CreateClient();
        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.True(live.IsSuccessStatusCode);
        Assert.True(ready.IsSuccessStatusCode, await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_a_correlation_id()
    {
        var response = await api.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }
}
