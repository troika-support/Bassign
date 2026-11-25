using Bassign.Domain.Common;
using Mediator;

namespace Bassign.Application.Identity;

public sealed record TeamMember(string Id, string Name, string Email, string Role, bool IsActive, bool TwoFactor = false);

/// <summary>User-account administration over ASP.NET Identity (implemented in Infrastructure).</summary>
public interface IIdentityAdmin
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default);
    Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>First-run check: is there anyone who can administer this install?</summary>
    async Task<bool> AnyActiveAdminAsync(CancellationToken cancellationToken = default) =>
        (await ListMembersAsync(cancellationToken)).Any(m => m.IsActive && m.Role == Bassign.Domain.Teams.TeamRoles.Admin);

    /// <summary>Right to rectification: a person corrects their own name.</summary>
    Task<Result<Unit>> UpdateDisplayNameAsync(string userId, string displayName, CancellationToken cancellationToken = default) =>
        Task.FromResult<Result<Unit>>(new Error("identity.unsupported", "Not supported."));
}
