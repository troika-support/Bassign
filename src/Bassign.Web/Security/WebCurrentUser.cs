using System.Security.Claims;
using Bassign.Application.Common.Security;
using Bassign.Infrastructure.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace Bassign.Web.Security;

/// <summary>
/// ICurrentUser for the Blazor host. Prefers the user id UiSender placed in this operation's scope;
/// falls back to the HTTP request (endpoints, prerender) and then the circuit's auth state.
/// </summary>
public sealed class WebCurrentUser(
    UserContext context, OperationUser operation, IHttpContextAccessor http, AuthenticationStateProvider auth) : ICurrentUser
{
    public string UserId =>
        operation.UserId is { Length: > 0 } runAs ? runAs
        : context.UserId is { Length: > 0 } id ? id
        : FromHttp() ?? FromCircuit() ?? "system";

    private string? FromHttp() =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } id ? id : null;

    private string? FromCircuit()
    {
        try
        {
            var state = auth.GetAuthenticationStateAsync();
            return state.IsCompletedSuccessfully ? state.Result.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        }
        catch (InvalidOperationException)
        {
            return null;   // not inside a circuit (e.g. a fresh operation scope)
        }
    }
}
