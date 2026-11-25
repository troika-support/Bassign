using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Bassign.Web.Security;

/// <summary>
/// Two-factor sign-in on the website (decision #24). After the password is right, the person holds only a
/// short "mid-sign-in" cookie naming them; the real session cookie is issued once their code is right.
/// </summary>
public static class TwoFactorSignIn
{
    public const string Scheme = "Bassign.TwoFactor";

    /// <summary>On a session whose owner must still set up two-factor: it can only reach the setup page.</summary>
    public const string SetupRequiredClaim = "bassign:2fa_setup";
    public const string SetupPath = "/two-factor/setup";

    public static Task BeginAsync(HttpContext http, string userId) =>
        http.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], Scheme)));

    public static async Task<string?> PendingUserAsync(HttpContext http)
    {
        var result = await http.AuthenticateAsync(Scheme);
        return result.Succeeded ? result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) : null;
    }

    public static Task EndAsync(HttpContext http) => http.SignOutAsync(Scheme);

    public static bool MustSetUp(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.HasClaim(SetupRequiredClaim, "true");

    /// <summary>Server side of the gate (the layout handles in-app navigation): any other page or download
    /// redirects to setup. Blazor's own plumbing, static files, sign-out and privacy stay reachable.</summary>
    public static IApplicationBuilder UseTwoFactorSetupGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (MustSetUp(context.User) && !Allowed(context.Request.Path))
        {
            context.Response.Redirect(SetupPath);
            return;
        }
        await next();
    });

    private static bool Allowed(PathString path) =>
        !(path.StartsWithSegments("/proof") || path.StartsWithSegments("/hangfire") || path.StartsWithSegments("/account/my-data"))
        && (path.StartsWithSegments(SetupPath)
        || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/_content")
        || path.StartsWithSegments("/account/logout")
        || path.StartsWithSegments("/account/refresh-session")
        || path.StartsWithSegments("/privacy")
        || path.StartsWithSegments("/health")
        || Path.HasExtension(path.Value));   // css, js, images, fonts
}
