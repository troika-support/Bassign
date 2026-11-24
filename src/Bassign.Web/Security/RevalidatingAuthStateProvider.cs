using System.Security.Claims;
using Bassign.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Web.Security;

/// <summary>
/// An open Blazor circuit keeps its sign-in for hours; this re-checks every 5 minutes that the person is
/// still active (not deactivated or erased) and signs the circuit out if not. The cookie is re-checked
/// separately (CookieRevalidation) for new page loads.
/// </summary>
public sealed class RevalidatingAuthStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var userId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return false;
        await using var scope = scopes.CreateAsyncScope();
        return await CookieRevalidation.IsActiveAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), userId, cancellationToken);
    }
}

public static class CookieRevalidation
{
    private const string CheckedKey = "bassign.checked";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static Task<bool> IsActiveAsync(AppDbContext db, string userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);

    /// <summary>Cookie OnValidatePrincipal: at most every 5 minutes, refuse the cookie of a deactivated user.</summary>
    public static async Task ValidateAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieValidatePrincipalContext context)
    {
        var now = DateTimeOffset.UtcNow;
        if (context.Properties.Items.TryGetValue(CheckedKey, out var raw)
            && DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var last)
            && now - last < Interval)
            return;

        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        if (string.IsNullOrEmpty(userId) || !await IsActiveAsync(db, userId, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        context.Properties.Items[CheckedKey] = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }
}
