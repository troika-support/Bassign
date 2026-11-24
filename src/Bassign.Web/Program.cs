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

builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<ICurrentUser, WebCurrentUser>();   // overrides the HTTP-only default
builder.Services.AddScoped<IUiSender, UiSender>();
builder.Services.AddScoped<OrgClock>();             // one DI scope (and DbContext) per UI operation

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/sign-in";
        options.AccessDeniedPath = "/sign-in";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
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

app.UseSerilogRequestLogging();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();   // fingerprinted, compressed, long-cached static files
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

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
});

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
});

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
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
}
