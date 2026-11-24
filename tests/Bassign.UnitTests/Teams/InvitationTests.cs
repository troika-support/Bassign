using Bassign.Domain.Teams;

namespace Bassign.UnitTests.Teams;

public class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);

    private static Invitation New(string role = TeamRoles.Staff) =>
        Invitation.Create("  Tendai@Example.COM ", role, "hash", "admin-1", Now);

    [Fact]
    public void Create_normalises_email_and_sets_a_seven_day_expiry()
    {
        var invite = New();
        Assert.Equal("tendai@example.com", invite.Email);
        Assert.Equal(Now.AddDays(7), invite.ExpiresAt);
        Assert.True(invite.IsPending(Now));
    }

    [Fact]
    public void Create_rejects_an_unknown_role() =>
        Assert.Throws<ArgumentException>(() => New("Owner"));

    [Fact]
    public void Is_not_pending_after_expiry() =>
        Assert.False(New().IsPending(Now.AddDays(7)));

    [Fact]
    public void Can_only_be_accepted_once()
    {
        var invite = New();
        invite.Accept("user-1", Now);
        Assert.Throws<InvalidOperationException>(() => invite.Accept("user-2", Now));
    }

    [Fact]
    public void A_revoked_invite_cannot_be_accepted()
    {
        var invite = New();
        invite.Revoke(Now);
        Assert.Throws<InvalidOperationException>(() => invite.Accept("user-1", Now));
    }

    [Fact]
    public void An_accepted_invite_cannot_be_revoked()
    {
        var invite = New();
        invite.Accept("user-1", Now);
        Assert.Throws<InvalidOperationException>(() => invite.Revoke(Now));
    }
}
