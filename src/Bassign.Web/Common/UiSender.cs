using System.Security.Claims;
using Bassign.Application.Common.Persistence;
using Bassign.Domain.Common;
using Bassign.Web.Security;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Components.Authorization;

namespace Bassign.Web.Common;

/// <summary>
/// How Blazor components talk to the Application layer. A Blazor Server circuit is long-lived, so
/// circuit-scoped services (DbContext, repositories, unit of work) would be shared by every component
/// on the page — concurrent loads then collide, and tracked entities go stale. Instead, each message
/// runs in its own DI scope, exactly like an HTTP request, with the signed-in user carried across.
/// </summary>
public interface IUiSender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>As Send, but a pipeline ValidationException comes back as a failed Result instead of
    /// throwing inside an event handler (which would kill the circuit).</summary>
    Task<Result<T>> TrySend<T>(IRequest<Result<T>> request, CancellationToken cancellationToken = default);
}

public sealed class UiSender(
    IServiceScopeFactory scopes,
    AuthenticationStateProvider auth,
    IHttpContextAccessor http) : IUiSender
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var userId = await CurrentUserIdAsync();

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContext>().UserId = userId;
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(request, cancellationToken);
    }

    public async Task<Result<T>> TrySend<T>(IRequest<Result<T>> request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Send(request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return new Error("validation", string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct()));
        }
        catch (ConcurrencyConflictException ex)
        {
            return new Error("conflict", ex.Message);
        }
    }

    private async Task<string?> CurrentUserIdAsync()
    {
        // Prerender / endpoints: the HTTP request carries the cookie principal.
        if (http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } fromHttp)
            return fromHttp;

        // Interactive circuit: no HttpContext, use the circuit's authentication state.
        try
        {
            var state = await auth.GetAuthenticationStateAsync();
            return state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
