using Bassign.Infrastructure.Settings;
using Bassign.Infrastructure.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Bassign.Infrastructure.Jobs;
using Hangfire;
using System.Security.Claims;
using Bassign.Application;
using Bassign.Application.Common.Security;
using Bassign.Application.Media;
using Bassign.Application.Teams.AcceptInvite;
using Bassign.Domain.Common;
using Bassign.Infrastructure;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Seed;
using Bassign.Web.Common;
using Bassign.Web.Components;
using Bassign.Web.Security;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddBassignAdminSettings();   // Admin-set values from the database: above appsettings, below env vars

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()   // CorrelationId on every line of a request
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {CorrelationId}{NewLine}{Exception}", formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddBassignJobs(builder.Configuration, processJobs: false);   // dashboard only; the API runs jobs
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<ICurrentUser, WebCurrentUser>();   // overrides the HTTP-only default
builder.Services.AddScoped<IUiSender, UiSender>();   // one DI scope (and DbContext) per UI operation
builder.Services.AddScoped<OrgClock>();
builder.Services.AddScoped<TaskListEvents>();   // "tasks changed" signal within a circuit
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthStateProvider>();   // signs out deactivated users' open circuits
builder.Services.AddBassignHardening(HangfireSetup.IsEnabled(builder.Configuration), jobServerCheck: true);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/sign-in";
        options.AccessDeniedPath = "/sign-in";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "bassign.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Events.OnValidatePrincipal = CookieRevalidation.ValidateAsync;   // deactivated ⇒ signed out within 5 min
    })
    // Between "password OK" and "code OK" (decision #24): a 5-minute cookie that only says who is mid-sign-in.
    .AddCookie(TwoFactorSignIn.Scheme, options =>
    {
        options.Cookie.Name = "bassign.2fa";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    })
    // Identity keeps the passkey challenge here between "give me options" and "here is my credential" (single use).
    .AddCookie(IdentityConstants.TwoFactorUserIdScheme, options =>
    {
        options.Cookie.Name = "bassign.passkey";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    });
builder.Services.AddBassignAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await DevSeeder.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Dev DB migrate/seed skipped.");
    }
}

app.UseBassignCorrelationId();
app.UseSerilogRequestLogging(options =>
{
    // Invite links carry a secret token in the path: never write it to the logs.
    options.GetMessageTemplateProperties = (context, path, elapsed, status) =>
    [
        new Serilog.Events.LogEventProperty("RequestMethod", new Serilog.Events.ScalarValue(context.Request.Method)),
        new Serilog.Events.LogEventProperty("RequestPath", new Serilog.Events.ScalarValue(
            path.StartsWith("/invite/", StringComparison.OrdinalIgnoreCase) ? "/invite/[Redacted]" : path)),
        new Serilog.Events.LogEventProperty("StatusCode", new Serilog.Events.ScalarValue(status)),
        new Serilog.Events.LogEventProperty("Elapsed", new Serilog.Events.ScalarValue(elapsed))
    ];
});
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseBassignSecurityHeaders(HostKind.Web, app.Environment.IsDevelopment());
app.UseAuthentication();
app.UseTwoFactorSetupGate();   // someone who must set up two-factor can reach only the setup page
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();   // fingerprinted, compressed, long-cached static files
if (HangfireSetup.IsEnabled(app.Configuration))
{
    app.MapHangfireDashboard(HangfireSetup.DashboardPath, new DashboardOptions
    {
        Authorization = [new AdminOnlyDashboardFilter()],
        DashboardTitle = "Bassign jobs",
        AppPath = "/",
        DisplayStorageConnectionString = false
    });
}
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapBassignHealth();   // /health, /health/live, /health/ready

app.MapPost("/account/login", async (
    HttpContext http,
    UserManager<ApplicationUser> users,
    [FromForm] string email,
    [FromForm] string password,
    [FromForm] string? returnUrl) =>
{
    var user = await users.FindByEmailAsync(email);
    if (user is null || !user.IsActive || await users.IsLockedOutAsync(user) || !await users.CheckPasswordAsync(user, password))
    {
        if (user is not null && user.IsActive) await users.AccessFailedAsync(user);   // 5 wrong tries → 5-minute lockout
        return Results.Redirect($"/sign-in?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }
    await users.ResetAccessFailedCountAsync(user);

    var safeReturn = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? "/" : returnUrl;
    if (await users.GetTwoFactorEnabledAsync(user))
    {
        await TwoFactorSignIn.BeginAsync(http, user.Id);
        var twoFactor = http.RequestServices.GetRequiredService<Bassign.Application.Identity.ITwoFactor>();
        if (await twoFactor.MethodAsync(user.Id) != Bassign.Application.Identity.TwoFactorMethods.Email)
            return Results.LocalRedirect($"/sign-in/two-factor?returnUrl={Uri.EscapeDataString(safeReturn)}");

        // Email codes: send one now. A "just sent" cooldown is fine — that code is still valid.
        var sent = await twoFactor.SendEmailCodeAsync(user.Id);
        var problem = sent.IsSuccess || sent.Error.Code == "2fa.email_cooldown" ? "" : $"&error={Uri.EscapeDataString(sent.Error.Message)}";
        return Results.LocalRedirect($"/sign-in/two-factor?method=email&returnUrl={Uri.EscapeDataString(safeReturn)}{problem}");
    }

    await SignInAsync(http, users, user);
    return Results.LocalRedirect(safeReturn);
}).RequireRateLimiting(Hardening.AuthPolicy);

// Invite acceptance: create the account via the Application command, then sign the new member in.
app.MapPost("/account/accept-invite", async (
    HttpContext http,
    ISender sender,
    UserManager<ApplicationUser> users,
    [FromForm] string token,
    [FromForm] string displayName,
    [FromForm] string password) =>
{
    string Back(string message) => $"/invite/{Uri.EscapeDataString(token)}?error={Uri.EscapeDataString(message)}";

    Result<AcceptedInvite> result;
    try
    {
        result = await sender.Send(new AcceptInviteCommand(token, displayName, password));
    }
    catch (FluentValidation.ValidationException ex)
    {
        return Results.LocalRedirect(Back(string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct())));
    }

    if (!result.IsSuccess)
        return Results.LocalRedirect(Back(result.Error.Message));

    var user = await users.FindByIdAsync(result.Value!.UserId);
    if (user is null)
        return Results.LocalRedirect("/sign-in");

    await SignInAsync(http, users, user);
    return Results.LocalRedirect("/connect?welcome=1");   // new members: get the phone app first (decision #16)
}).RequireRateLimiting(Hardening.AuthPolicy);

// Right of access / portability: download everything Bassign holds about you (JSON).
// First Admin on a fresh install (decision #15): only while no Admin exists, only with the server's setup code.
app.MapPost("/account/first-admin", async (
    HttpContext http,
    ISender sender,
    UserManager<ApplicationUser> users,
    [FromForm] string setupCode,
    [FromForm] string displayName,
    [FromForm] string email,
    [FromForm] string password) =>
{
    static string Back(string message) => $"/first-admin?error={Uri.EscapeDataString(message)}";

    Result<string> result;
    try
    {
        result = await sender.Send(new Bassign.Application.FirstRun.CreateFirstAdminCommand(setupCode, displayName, email, password));
    }
    catch (FluentValidation.ValidationException ex)
    {
        return Results.LocalRedirect(Back(string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct())));
    }

    if (!result.IsSuccess)
    {
        Log.Warning("First-admin setup refused: {Code}", result.Error.Code);   // never the code or password
        return Results.LocalRedirect(result.Error.Code == "setup.closed" ? "/sign-in" : Back(result.Error.Message));
    }

    var user = await users.FindByIdAsync(result.Value!);
    if (user is null) return Results.LocalRedirect("/sign-in");
    await SignInAsync(http, users, user);
    Log.Information("First Admin created; remove Setup__Code from the server environment.");
    return Results.LocalRedirect("/setup");   // name, colour, time zone
}).RequireRateLimiting(Hardening.AuthPolicy);

// Second step of sign-in: an authenticator code or a recovery code (decision #24). Every sign-in; no "remember me".
app.MapPost("/account/two-factor", async (
    HttpContext http,
    UserManager<ApplicationUser> users,
    [FromForm] string code,
    [FromForm] bool? recovery,
    [FromForm] string? returnUrl) =>
{
    var safeReturn = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? "/" : returnUrl;
    var userId = await TwoFactorSignIn.PendingUserAsync(http);
    var user = userId is null ? null : await users.FindByIdAsync(userId);
    if (user is null || !user.IsActive)
        return Results.LocalRedirect("/sign-in?expired=1");

    // The method comes from the account, never from the form.
    var twoFactor = http.RequestServices.GetRequiredService<Bassign.Application.Identity.ITwoFactor>();
    var byEmail = await twoFactor.MethodAsync(user.Id) == Bassign.Application.Identity.TwoFactorMethods.Email;
    string Back(string error) =>
        $"/sign-in/two-factor?error={Uri.EscapeDataString(error)}&returnUrl={Uri.EscapeDataString(safeReturn)}{(recovery == true ? "&recovery=1" : "")}{(byEmail ? "&method=email" : "")}";

    if (await users.IsLockedOutAsync(user))
        return Results.LocalRedirect(Back("Too many wrong codes. Wait 5 minutes and try again."));

    var ok = recovery == true ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, code ?? "")).Succeeded
        : byEmail ? await twoFactor.VerifyEmailCodeAsync(user.Id, code ?? "")
        : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, string.Concat((code ?? "").Where(char.IsDigit)));
    if (!ok)
    {
        await users.AccessFailedAsync(user);
        return Results.LocalRedirect(Back(
            recovery == true ? "That recovery code isn't valid (each works once)."
            : byEmail ? "That code isn't right or has expired. Use the newest email, or send another code."
            : "That code didn't match. Enter the newest code from your app."));
    }

    await users.ResetAccessFailedCountAsync(user);
    await TwoFactorSignIn.EndAsync(http);
    await SignInAsync(http, users, user, secondFactor: true);
    if (recovery == true && await users.CountRecoveryCodesAsync(user) <= 3)
        return Results.LocalRedirect("/profile?recoveryLow=1");
    return Results.LocalRedirect(safeReturn);
}).RequireRateLimiting(Hardening.AuthPolicy);

// "Send another code" on the email-code step. Cooldown (1/minute) and lockout are enforced server-side.
app.MapPost("/account/two-factor/resend", async (HttpContext http, UserManager<ApplicationUser> users, [FromForm] string? returnUrl) =>
{
    var safeReturn = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? "/" : returnUrl;
    var userId = await TwoFactorSignIn.PendingUserAsync(http);
    var user = userId is null ? null : await users.FindByIdAsync(userId);
    if (user is null || !user.IsActive) return Results.LocalRedirect("/sign-in?expired=1");

    var twoFactor = http.RequestServices.GetRequiredService<Bassign.Application.Identity.ITwoFactor>();
    var page = $"/sign-in/two-factor?method=email&returnUrl={Uri.EscapeDataString(safeReturn)}";
    if (await twoFactor.MethodAsync(user.Id) != Bassign.Application.Identity.TwoFactorMethods.Email || await users.IsLockedOutAsync(user))
        return Results.LocalRedirect(page);
    var sent = await twoFactor.SendEmailCodeAsync(user.Id);
    return Results.LocalRedirect(sent.IsSuccess ? page + "&sent=1" : page + $"&error={Uri.EscapeDataString(sent.Error.Message)}");
}).RequireRateLimiting(Hardening.AuthPolicy);

// ---- Passkeys (decision #24): sign in with a fingerprint, face or device PIN instead of password + code ----
// The two "options" calls are fetched by wwwroot/js/passkeys.js (antiforgery token in a header); the two
// that change anything are ordinary form posts.

static async Task<bool> AntiforgeryOkAsync(HttpContext http)
{
    try
    {
        await http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>().ValidateRequestAsync(http);
        return true;
    }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return false; }
}

app.MapPost("/account/passkey/creation-options", async (HttpContext http, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
{
    if (!await AntiforgeryOkAsync(http)) return Results.BadRequest();
    var user = await users.GetUserAsync(http.User);
    if (user is null || !user.IsActive) return Results.Unauthorized();
    if ((await users.GetPasskeysAsync(user)).Count >= Bassign.Application.Identity.IPasskeys.MaxPerUser)
        return Results.Problem($"You can have up to {Bassign.Application.Identity.IPasskeys.MaxPerUser} passkeys. Remove one first.", statusCode: StatusCodes.Status409Conflict);

    var json = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
    {
        Id = user.Id,
        Name = user.Email ?? user.UserName ?? user.Id,
        DisplayName = user.DisplayName
    });
    return Results.Content(json, "application/json");
}).RequireAuthorization().DisableAntiforgery().RequireRateLimiting(Hardening.AuthPolicy);

app.MapPost("/account/passkey/register", async (
    HttpContext http, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    [FromForm] string credentialJson, [FromForm] string? name) =>
{
    static string Back(string error) => $"/profile?passkeyError={Uri.EscapeDataString(error)}";
    var user = await users.GetUserAsync(http.User);
    if (user is null || !user.IsActive) return Results.LocalRedirect("/sign-in");

    PasskeyAttestationResult result;
    try { result = await signIn.PerformPasskeyAttestationAsync(credentialJson); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Log.Warning("Passkey registration could not be checked: {Error}", ex.GetType().Name);
        return Results.LocalRedirect(Back("That took too long. Please try adding the passkey again."));
    }
    if (!result.Succeeded || result.UserEntity.Id != user.Id)   // made for this signed-in person, in this session
        return Results.LocalRedirect(Back("That passkey couldn't be added. Please try again."));
    if ((await users.GetPasskeysAsync(user)).Count >= Bassign.Application.Identity.IPasskeys.MaxPerUser)
        return Results.LocalRedirect(Back("You've reached the limit of passkeys. Remove one first."));

    var label = string.IsNullOrWhiteSpace(name) ? "Passkey" : name.Trim();
    result.Passkey.Name = label.Length > 40 ? label[..40] : label;
    var saved = await users.AddOrUpdatePasskeyAsync(user, result.Passkey);
    if (!saved.Succeeded) return Results.LocalRedirect(Back("That passkey couldn't be saved. Please try again."));

    Log.Information("Passkey added for user {UserId}", user.Id);
    return Results.LocalRedirect("/profile?passkeyAdded=1");
}).RequireAuthorization().RequireRateLimiting(Hardening.AuthPolicy);

app.MapPost("/account/passkey/request-options", async (HttpContext http, SignInManager<ApplicationUser> signIn) =>
{
    if (!await AntiforgeryOkAsync(http)) return Results.BadRequest();
    // No user named: the phone or computer offers whichever passkeys it holds for this site, so nothing
    // here reveals whether an email address has an account.
    return Results.Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json");
}).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(Hardening.AuthPolicy);

app.MapPost("/account/passkey/sign-in", async (
    HttpContext http, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    [FromForm] string credentialJson, [FromForm] string? returnUrl) =>
{
    var safeReturn = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? "/" : returnUrl;
    var failed = $"/sign-in?passkey=failed&returnUrl={Uri.EscapeDataString(safeReturn)}";

    PasskeyAssertionResult<ApplicationUser> result;
    try { result = await signIn.PerformPasskeyAssertionAsync(credentialJson); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Log.Warning("Passkey sign-in could not be checked: {Error}", ex.GetType().Name);
        return Results.LocalRedirect(failed);
    }
    if (!result.Succeeded || !result.User.IsActive || await users.IsLockedOutAsync(result.User))
        return Results.LocalRedirect(failed);

    await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey);   // stores the new use counter (clone detection)
    await SignInAsync(http, users, result.User, secondFactor: true);    // device + fingerprint/PIN = two factors
    return Results.LocalRedirect(safeReturn);
}).AllowAnonymous().RequireRateLimiting(Hardening.AuthPolicy);

// After setting up two-factor in the browser, swap the restricted session for a full one (re-checked server-side).
app.MapGet("/account/refresh-session", async (HttpContext http, UserManager<ApplicationUser> users, string? returnUrl) =>
{
    var safeReturn = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? "/" : returnUrl;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var user = userId is null ? null : await users.FindByIdAsync(userId);
    if (user is null || !user.IsActive) return Results.LocalRedirect("/sign-in");
    await SignInAsync(http, users, user, secondFactor: await users.GetTwoFactorEnabledAsync(user));
    return Results.LocalRedirect(safeReturn);
}).RequireAuthorization();

// Phone connect (decision #16): public, read-only — the organization's name and its API's address.
app.MapGet("/connect/info", async (ISender sender, Microsoft.Extensions.Options.IOptionsMonitor<PublicOptions> publicUrls, CancellationToken ct) =>
{
    var api = publicUrls.CurrentValue.ApiBaseUrl;
    if (string.IsNullOrWhiteSpace(api))
        return Results.Problem("This server's API address (Public:ApiBaseUrl) isn't set yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var org = await sender.Send(new Bassign.Application.Organizations.GetOrganization.GetOrganizationQuery(), ct);
    return Results.Ok(new ConnectInfo(org.Name, api.EndsWith('/') ? api : api + "/", PhoneConnect.Protocol));
}).AllowAnonymous();

app.MapGet("/account/my-data", async (HttpContext http, IServiceScopeFactory scopes) =>
{
    await using var scope = scopes.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<UserContext>().UserId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var export = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Bassign.Application.Privacy.ExportMyDataQuery());
    if (export is null) return Results.NotFound();
    var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(export, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
    return Results.File(json, "application/json", $"bassign-my-data-{DateTime.UtcNow:yyyyMMdd}.json");
}).RequireAuthorization();

// Proof-of-work photos (stored by the API's /sync/proof) for managers reviewing a task.
app.MapGet("/proof/{reference}", async (string reference, IProofStore store, CancellationToken ct) =>
{
    var file = await store.OpenAsync(reference, ct);
    return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType);
}).RequireAuthorization("Managers");

app.MapPost("/account/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/sign-in");
});

Log.Information("Bassign Web starting");
app.Run();

// Mint the claims cookie used by the web app (no SignInManager scheme coupling).
static async Task SignInAsync(HttpContext http, UserManager<ApplicationUser> users, ApplicationUser user, bool secondFactor = false)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id),
        new(ClaimTypes.Name, user.DisplayName),
        new(ClaimTypes.Email, user.Email ?? string.Empty)
    };
    claims.AddRange((await users.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));

    // Decision #24: someone whose role requires two-factor but who hasn't set it up gets a session that can only
    // reach the setup page (TwoFactorSetupGate + MainLayout) until they do.
    var status = await http.RequestServices.GetRequiredService<ISender>().Send(new Bassign.Application.Identity.GetTwoFactorStatusQuery(user.Id));
    if (status.MustSetUp) claims.Add(new Claim(TwoFactorSignIn.SetupRequiredClaim, "true"));
    claims.Add(new Claim("amr", secondFactor ? "mfa" : "pwd"));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

    // Session length is Admin-editable (Settings → Security & privacy), so it's read at sign-in rather than
    // fixed in the cookie options. Sliding renewal keeps this span: signed out after N hours without use.
    var hours = http.RequestServices.GetRequiredService<IConfiguration>().GetValue<int?>("Auth:WebSessionHours") is >= 1 and <= 72 and var h ? h : 8;
    var now = DateTimeOffset.UtcNow;
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
        new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddHours(hours) });
}
