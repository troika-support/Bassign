using Bassign.Domain.Common;
using Mediator;

namespace Bassign.Application.Identity;

public sealed record TeamMember(string Id, string Name, string Email, string Role, bool IsActive);

/// <summary>User-account administration over ASP.NET Identity (implemented in Infrastructure).</summary>
public interface IIdentityAdmin
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default);
    Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default);
}
