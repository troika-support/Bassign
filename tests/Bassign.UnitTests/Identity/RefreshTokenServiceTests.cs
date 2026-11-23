using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Bassign.UnitTests.Identity;

public class RefreshTokenServiceTests
{
    // Minimal in-memory store so rotation/reuse logic is tested without a database.
    private sealed class FakeStore : IRefreshTokenStore
    {
        public readonly List<RefreshToken> Tokens = [];

        public Task AddAsync(RefreshToken token, CancellationToken ct)
        {
            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<RefreshToken?> FindAsync(string tokenHash, CancellationToken ct) =>
            Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

        public Task RevokeAllActiveForUserAsync(string userId, DateTimeOffset now, CancellationToken ct)
        {
            foreach (var t in Tokens.Where(t => t.UserId == userId && t.RevokedAt == null))
                t.RevokedAt = now;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static RefreshTokenService CreateSut(FakeStore store) =>
        new(store, Options.Create(new AuthOptions { RefreshTokenDays = 30 }), TimeProvider.System);

    [Fact]
    public async Task Rotate_issues_new_token_and_revokes_the_old()
    {
        var store = new FakeStore();
        var sut = CreateSut(store);
        var raw = await sut.IssueAsync("u1", TestContext.Current.CancellationToken);

        var result = await sut.RotateAsync(raw, TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.NotNull(result.NewRawToken);
        Assert.NotEqual(raw, result.NewRawToken);
        Assert.Equal("u1", result.UserId);
        Assert.Single(store.Tokens, t => t.RevokedAt != null); // the old one
    }

    [Fact]
    public async Task Rotate_rejects_unknown_token()
    {
        var sut = CreateSut(new FakeStore());
        var result = await sut.RotateAsync("not-a-real-token", TestContext.Current.CancellationToken);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Reuse_of_a_revoked_token_revokes_the_whole_chain()
    {
        var store = new FakeStore();
        var sut = CreateSut(store);
        var raw = await sut.IssueAsync("u1", TestContext.Current.CancellationToken);

        var first = await sut.RotateAsync(raw, TestContext.Current.CancellationToken);   // raw now revoked, new issued
        var reuse = await sut.RotateAsync(raw, TestContext.Current.CancellationToken);    // presenting raw again = reuse

        Assert.True(first.Ok);
        Assert.False(reuse.Ok);
        Assert.All(store.Tokens, t => Assert.NotNull(t.RevokedAt)); // entire chain revoked
    }
}
