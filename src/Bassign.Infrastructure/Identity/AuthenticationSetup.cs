using System.Security.Claims;
using System.Text;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Bassign.Infrastructure.Identity;

public static class AuthenticationSetup
{
    /// <summary>JWT bearer for the API, plus the role policies.</summary>
    public static IServiceCollection AddBassignJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        if (Encoding.UTF8.GetByteCount(auth.JwtSigningKey) < 32)
            throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 bytes.");
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddOptions<AuthOptions>().ValidateOnStart();
        services.AddMemoryCache();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.Issuer,
                    ValidateAudience = true,
                    ValidAudience = auth.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.JwtSigningKey)),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.NameIdentifier,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],   // no "alg" switching
                    RequireExpirationTime = true,
                    RequireSignedTokens = true
                };
                options.Events = new JwtBearerEvents
                {
                    // A deactivated or erased user is refused within a minute, not when the token expires.
                    OnTokenValidated = async ctx =>
                    {
                        var userId = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (string.IsNullOrEmpty(userId)) { ctx.Fail("No subject."); return; }
                        var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                        var active = await cache.GetOrCreateAsync($"user-active:{userId}", async entry =>
                        {
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                            return await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, ctx.HttpContext.RequestAborted);
                        });
                        if (!active) ctx.Fail("Account is deactivated.");
                    }
                };
            });

        return services.AddBassignAuthorization();
    }

    /// <summary>Role policies, shared by API and web.</summary>
    public static IServiceCollection AddBassignAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy("Admin", policy => policy.RequireRole("Admin"))
            .AddPolicy("Managers", policy => policy.RequireRole("Admin", "Manager"));
        return services;
    }
}
