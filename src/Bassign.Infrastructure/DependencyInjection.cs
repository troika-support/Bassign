using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Common.Directory;
using Bassign.Application.Identity;
using Bassign.Application.Media;
using Bassign.Application.Sync;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Application.Teams;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Auditing;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Media;
using Bassign.Infrastructure.Sync;
using Bassign.Infrastructure.Persistence.Repositories;
using Bassign.Infrastructure.Teams;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetSection(DatabaseOptions.Section)["ConnectionString"]
            ?? throw new InvalidOperationException("Database:ConnectionString is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddHttpContextAccessor();

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditSink, AuditSink>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<JwtTokenGenerator>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IIdentityAdmin, IdentityAdmin>();
        services.AddScoped<IInviteNotifier, LoggingInviteNotifier>();
        services.AddSingleton<ISecureTokens, SecureTokens>();
        services.AddScoped<ITaskSyncReader, TaskSyncReader>();
        services.AddScoped<IOperationLog, OperationLog>();
        services.AddOptions<MediaOptions>().Bind(configuration.GetSection(MediaOptions.Section));
        services.AddSingleton<IProofStore, LocalDiskProofStore>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
