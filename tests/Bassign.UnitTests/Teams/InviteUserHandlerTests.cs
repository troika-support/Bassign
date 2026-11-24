using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Teams;
using Bassign.Application.Teams.InviteUser;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.UnitTests.Teams;

public class InviteUserHandlerTests
{
    private sealed class FakeInvitations : IInvitationRepository
    {
        public readonly List<Invitation> Items = [];
        public void Add(Invitation invitation) => Items.Add(invitation);
        public Task<Invitation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(i => i.Id == id));
        public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(i => i.TokenHash == tokenHash));
        public Task<IReadOnlyList<Invitation>> ListPendingAsync(DateTimeOffset nowUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Invitation>>(Items.Where(i => i.IsPending(nowUtc)).ToList());
        public Task<bool> HasPendingForEmailAsync(string email, DateTimeOffset nowUtc, CancellationToken ct = default) =>
            Task.FromResult(Items.Any(i => i.Email == email && i.IsPending(nowUtc)));
    }

    private sealed class FakeIdentity(HashSet<string> existingEmails, HashSet<string> adminIds) : IIdentityAdmin
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(existingEmails.Contains(email));
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct = default) =>
            Task.FromResult(role == TeamRoles.Admin && adminIds.Contains(userId));
        public Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken ct = default) =>
            Task.FromResult<Result<string>>("new-user");
        public Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TeamMember>>([]);
        public Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken ct = default) =>
            Task.FromResult<Result<Unit>>(Unit.Value);
    }

    private sealed class FakeTokens : ISecureTokens
    {
        public (string Raw, string Hash) Create() => ("raw-token", "hash-of-raw-token");
        public string Hash(string raw) => $"hash-of-{raw}";
    }

    private sealed class FakeNotifier : IInviteNotifier
    {
        public int Calls { get; private set; }
        public Task InviteCreatedAsync(string email, string role, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class FakeCurrentUser(string id) : ICurrentUser { public string UserId => id; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (InviteUserHandler Handler, FakeInvitations Invites, FakeNotifier Notifier, FakeUnitOfWork Uow) Build(
        string currentUserId, string[]? existingEmails = null)
    {
        var invites = new FakeInvitations();
        var notifier = new FakeNotifier();
        var uow = new FakeUnitOfWork();
        var handler = new InviteUserHandler(
            invites,
            new FakeIdentity([.. existingEmails ?? []], ["admin-1"]),
            new FakeTokens(), notifier, uow, new FakeCurrentUser(currentUserId), TimeProvider.System);
        return (handler, invites, notifier, uow);
    }

    [Fact]
    public async Task A_manager_cannot_invite_a_manager()
    {
        var (handler, invites, _, _) = Build("manager-1");
        var result = await handler.Handle(new InviteUserCommand("new@acme.co", TeamRoles.Manager), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal("team.forbidden", result.Error.Code);
        Assert.Empty(invites.Items);
    }

    [Fact]
    public async Task An_existing_member_is_rejected()
    {
        var (handler, _, _, _) = Build("admin-1", ["tendai@acme.co"]);
        var result = await handler.Handle(new InviteUserCommand("Tendai@Acme.co", TeamRoles.Staff), Ct);

        Assert.Equal("team.already_member", result.Error.Code);
    }

    [Fact]
    public async Task Success_stores_only_the_hash_and_returns_the_raw_token_once()
    {
        var (handler, invites, notifier, uow) = Build("admin-1");
        var result = await handler.Handle(new InviteUserCommand("Rudo@Acme.co", TeamRoles.Staff), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("raw-token", result.Value!.Token);
        var stored = Assert.Single(invites.Items);
        Assert.Equal("hash-of-raw-token", stored.TokenHash);
        Assert.Equal("rudo@acme.co", stored.Email);
        Assert.Equal(1, notifier.Calls);
        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task A_second_pending_invite_for_the_same_email_is_rejected()
    {
        var (handler, _, _, _) = Build("admin-1");
        await handler.Handle(new InviteUserCommand("rudo@acme.co", TeamRoles.Staff), Ct);
        var again = await handler.Handle(new InviteUserCommand("rudo@acme.co", TeamRoles.Staff), Ct);

        Assert.Equal("team.already_invited", again.Error.Code);
    }
}
