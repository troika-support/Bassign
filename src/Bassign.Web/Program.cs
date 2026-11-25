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
    if (user is null || !user.IsActive || !await users.CheckPasswordAsync(user, password))
        return Results.Redirect($"/sign-in?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

    await SignInAsync(http, users, user);
    return Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
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
    return Results.LocalRedirect("/");
}).RequireRateLimiting(Hardening.AuthPolicy);

// Right of access / portability: download everything Bassign holds about you (JSON).
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
static async Task SignInAsync(HttpContext http, UserManager<ApplicationUser> users, ApplicationUser user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id),
        new(ClaimTypes.Name, user.DisplayName),
        new(ClaimTypes.Email, user.Email ?? string.Empty)
    };
    claims.AddRange((await users.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

    // Session length is Admin-editable (Settings → Security & privacy), so it's read at sign-in rather than
    // fixed in the cookie options. Sliding renewal keeps this span: signed out after N hours without use.
    var hours = http.RequestServices.GetRequiredService<IConfiguration>().GetValue<int?>("Auth:WebSessionHours") is >= 1 and <= 72 and var h ? h : 8;
    var now = DateTimeOffset.UtcNow;
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
        new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddHours(hours) });
}
