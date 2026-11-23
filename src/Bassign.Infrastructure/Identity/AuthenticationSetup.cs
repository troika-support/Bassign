using System.Security.Claims;
using System.Text;
using Bassign.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        if (string.IsNullOrWhiteSpace(auth.JwtSigningKey))
            throw new InvalidOperationException("Auth:JwtSigningKey is not configured.");

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
                    ClockSkew = TimeSpan.FromSeconds(30)
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
