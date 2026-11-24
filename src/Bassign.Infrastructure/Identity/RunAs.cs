using Bassign.Application.Common.Security;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.Infrastructure.Identity;

/// <summary>Per-scope override of the acting user — set by <see cref="RunAs"/>; read first by ICurrentUser.</summary>
public sealed class OperationUser
{
    public string? UserId { get; set; }
}

public sealed class RunAs(IServiceScopeFactory scopes) : IRunAs
{
    public async Task<TResponse> SendAsync<TResponse>(string userId, IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OperationUser>().UserId = userId;
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
    }
}
