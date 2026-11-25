using Bassign.Application.Common.Persistence;
using Bassign.Application.FirstRun;
using Bassign.Application.Identity;
using Bassign.Application.Privacy;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.UnitTests.FirstRun;

public class FirstAdminTests
{
    private sealed class People : IIdentityAdmin
    {
        public List<TeamMember> Members { get; } = [];
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Members.Any(m => m.Email == email));
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default) =>
            Task.FromResult(Members.Any(m => m.Id == userId && m.Role == role));
        public Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken cancellationToken = default)
        {
            var id = $"user-{Members.Count + 1}";
            Members.Add(new TeamMember(id, displayName, email, role, true));
            return Task.FromResult<Result<string>>(id);
        }
        public Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TeamMember>>(Members);
        public Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<Unit>>(Unit.Value);
    }

    private sealed class Code(string? value) : ISetupCode { public string? Value => value; }

    private sealed class Consents : IConsentRegister
    {
        public int Count { get; private set; }
        public void Record(string userId, string purpose, bool granted, string source, DateTimeOffset at, string version = ConsentPurposes.CurrentNoticeVersion) => Count++;
        public Task<IReadOnlyList<ConsentEntry>> ForUserAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConsentEntry>>([]);
    }

    private sealed class Uow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private static (CreateFirstAdminHandler Sut, People People) Create(string? code = "ABCD-EFGH-JKMN")
    {
        var people = new People();
        return (new CreateFirstAdminHandler(people, new Code(code), new Consents(), new Uow(), TimeProvider.System), people);
    }

    private static ValueTask<Result<string>> Run(CreateFirstAdminHandler sut, string code) =>
        sut.Handle(new CreateFirstAdminCommand(code, "Rudo Moyo", "Rudo@MintedMoments.co.zw", "long-enough-password"), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("ABCD-EFGH-JKMN")]
    [InlineData(" abcdefghjkmn ")]   // case, dashes and spaces don't matter
    public async Task The_right_code_creates_an_admin(string typed)
    {
        var (sut, people) = Create();
        var result = await Run(sut, typed);

        Assert.True(result.IsSuccess);
        var admin = Assert.Single(people.Members);
        Assert.Equal((TeamRoles.Admin, "rudo@mintedmoments.co.zw"), (admin.Role, admin.Email));
    }

    [Fact]
    public async Task A_wrong_code_creates_nothing()
    {
        var (sut, people) = Create();
        Assert.Equal("setup.bad_code", (await Run(sut, "ABCD-EFGH-JKMX")).Error.Code);
        Assert.Empty(people.Members);
    }

    [Fact]
    public async Task Without_a_server_code_setup_is_refused()
    {
        var (sut, people) = Create(code: null);
        Assert.Equal("setup.no_code", (await Run(sut, "anything")).Error.Code);
        Assert.Empty(people.Members);
    }

    [Fact]
    public async Task Once_an_admin_exists_the_door_is_shut_even_with_the_code()
    {
        var (sut, people) = Create();
        Assert.True((await Run(sut, "ABCD-EFGH-JKMN")).IsSuccess);

        var second = await Run(sut, "ABCD-EFGH-JKMN");

        Assert.Equal("setup.closed", second.Error.Code);
        Assert.Single(people.Members);
    }

    [Fact]
    public void Codes_compare_safely() =>
        Assert.False(CreateFirstAdminHandler.CodesMatch("ABCD", "ABCD-EFGH-JKMN"));
}
