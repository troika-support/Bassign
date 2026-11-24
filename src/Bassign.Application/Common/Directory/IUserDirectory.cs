namespace Bassign.Application.Common.Directory;

public sealed record DirectoryUser(string Id, string Name);

/// <summary>Resolves user ids to display names, and lists assignable (active) users. Over Identity.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DirectoryUser>> ListActiveAsync(CancellationToken cancellationToken = default);
}
