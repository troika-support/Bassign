using Bassign.Application.Common.Directory;
using Bassign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Identity;

public sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<string, string>();

        return await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryUser>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new DirectoryUser(u.Id, u.DisplayName))
            .ToListAsync(cancellationToken);

    public async Task<UserContact?> GetContactAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && u.Email != null)
            .Select(u => new UserContact(u.Id, u.DisplayName, u.Email!))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DirectoryUser>> ListActiveInRolesAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default) =>
        await (from u in db.Users.AsNoTracking()
               join ur in db.UserRoles on u.Id equals ur.UserId
               join r in db.Roles on ur.RoleId equals r.Id
               where u.IsActive && r.Name != null && roles.Contains(r.Name)
               select new DirectoryUser(u.Id, u.DisplayName))
            .Distinct()
            .ToListAsync(cancellationToken);
}
