using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using Bassign.Infrastructure.Persistence;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Identity;

public sealed class IdentityAdmin(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IRefreshTokenStore refreshTokens,
    TimeProvider clock) : IIdentityAdmin
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        await users.FindByEmailAsync(email) is not null;

    public async Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId);
        return user is not null && await users.IsInRoleAsync(user, role);
    }

    public async Task<Result<string>> CreateUserAsync(
        string email, string displayName, string password, string role, CancellationToken cancellationToken = default)
    {
        if (!await roles.RoleExistsAsync(role))
            await roles.CreateAsync(new IdentityRole(role));

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,   // they proved the address by opening the invite link
            DisplayName = displayName.Trim(),
            IsActive = true
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            return new Error("identity.create", string.Join(" ", created.Errors.Select(e => e.Description)));

        var inRole = await users.AddToRoleAsync(user, role);
        if (!inRole.Succeeded)
        {
            await users.DeleteAsync(user);
            return new Error("identity.role", string.Join(" ", inRole.Errors.Select(e => e.Description)));
        }

        return user.Id;
    }

    public async Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default)
    {
        var people = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.IsActive })
            .ToListAsync(cancellationToken);

        var userRoles = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                select new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);

        var roleByUser = userRoles
            .Where(x => x.Name != null)
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).OrderBy(RoleRank).First());

        return people
            .Select(p => new TeamMember(p.Id, p.DisplayName, p.Email ?? string.Empty,
                roleByUser.GetValueOrDefault(p.Id, "—"), p.IsActive))
            .OrderBy(m => RoleRank(m.Role)).ThenBy(m => m.Name)
            .ToList();
    }

    public async Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
            return new Error("team.not_found", "That person isn't on the team.");

        if (!isActive && await users.IsInRoleAsync(user, TeamRoles.Admin))
        {
            var admins = await users.GetUsersInRoleAsync(TeamRoles.Admin);
            if (!admins.Any(a => a.IsActive && a.Id != user.Id))
                return new Error("team.last_admin", "There must be at least one active Admin.");
        }

        user.IsActive = isActive;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
            return new Error("identity.update", string.Join(" ", updated.Errors.Select(e => e.Description)));

        if (!isActive)   // cut off API/mobile sessions immediately
            await refreshTokens.RevokeAllActiveForUserAsync(user.Id, clock.GetUtcNow(), cancellationToken);

        return Unit.Value;
    }

    private static int RoleRank(string role) => role switch
    {
        TeamRoles.Admin => 0, TeamRoles.Manager => 1, TeamRoles.Staff => 2, _ => 3
    };
}
