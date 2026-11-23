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
}
