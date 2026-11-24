using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Privacy;
using Bassign.Domain.Common;
using Bassign.UnitTests.Notifications;
using Mediator;

namespace Bassign.UnitTests.Hardening;

public class ErasureTests
{
    private sealed class Admin(IReadOnlyList<TeamMember> members, string adminId) : IIdentityAdmin
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default) => Task.FromResult(userId == adminId && role == "Admin");
        public Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default) => Task.FromResult(members);
        public Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Me(string id) : ICurrentUser { public string UserId => id; }

    private sealed class Eraser : IPersonalDataEraser
    {
        public List<string> Erased { get; } = [];
        public Task<ErasureReport> EraseAsync(string userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        {
            Erased.Add(userId);
            return Task.FromResult(new ErasureReport(3, 1, 2, 0, 1));
        }
    }

    private static readonly IReadOnlyList<TeamMember> Members =
    [
        new("adm-1", "Admin User", "admin@x.co.zw", "Admin", true),
        new("left-1", "Farai Dube", "farai@x.co.zw", "Staff", false),
        new("here-1", "Rudo Moyo", "rudo@x.co.zw", "Staff", true)
    ];

    private static async Task<(Result<ErasureReport> Result, Eraser Eraser)> Run(string actor, string target)
    {
        var eraser = new Eraser();
        var handler = new EraseUserDataHandler(new Admin(Members, "adm-1"), new Me(actor), eraser, new FakeClock(DateTimeOffset.UnixEpoch));
        return (await handler.Handle(new EraseUserDataCommand(target), TestContext.Current.CancellationToken), eraser);
    }

    [Fact]
    public async Task An_admin_can_erase_a_deactivated_member()
    {
        var (result, eraser) = await Run("adm-1", "left-1");
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "left-1" }, eraser.Erased);
    }

    [Fact]
    public async Task Active_members_must_be_deactivated_first()
    {
        var (result, eraser) = await Run("adm-1", "here-1");
        Assert.Equal("privacy.active", result.Error.Code);
        Assert.Empty(eraser.Erased);
    }

    [Fact]
    public async Task Only_admins_can_erase()
    {
        var (result, eraser) = await Run("here-1", "left-1");
        Assert.Equal("privacy.forbidden", result.Error.Code);
        Assert.Empty(eraser.Erased);
    }

    [Fact]
    public async Task Admins_cannot_erase_themselves()
    {
        var (result, _) = await Run("adm-1", "adm-1");
        Assert.Equal("privacy.self", result.Error.Code);
    }
}
