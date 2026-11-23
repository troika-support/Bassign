namespace Bassign.Application.Common.Directory;

/// <summary>Resolves user ids to display names for read models. Implemented over Identity in Infrastructure.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
}
