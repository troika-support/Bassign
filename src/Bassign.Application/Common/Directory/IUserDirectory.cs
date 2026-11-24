namespace Bassign.Application.Common.Directory;

public sealed record DirectoryUser(string Id, string Name);

public sealed record UserContact(string Id, string Name, string Email);

/// <summary>Resolves user ids to display names, and lists assignable (active) users. Over Identity.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DirectoryUser>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Name + email for an active user (email channel); null if unknown or deactivated.</summary>
    Task<UserContact?> GetContactAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<UserContact?>(null);

    /// <summary>Active users holding any of the roles (e.g. managers for escalations and digests).</summary>
    Task<IReadOnlyList<DirectoryUser>> ListActiveInRolesAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DirectoryUser>>([]);
}
