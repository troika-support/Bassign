using Bassign.Application.Ai;
using Bassign.Application.Common.Auditing;
using Bassign.Application.Privacy;
using Bassign.Application.Common.Email;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Common.Directory;
using Bassign.Application.Identity;
using Bassign.Application.Common.Events;
using Bassign.Application.Media;
using Bassign.Application.Messaging;
using Bassign.Application.Messaging.Simulator;
using Bassign.Application.Notifications;
using Bassign.Application.Sync;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Application.Teams;
using Bassign.Infrastructure.Ai;
using Bassign.Infrastructure.Email;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Privacy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Auditing;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Events;
using Bassign.Infrastructure.Media;
using Bassign.Infrastructure.Notifications;
using Bassign.Infrastructure.WhatsApp;
using Bassign.Infrastructure.Sync;
using Bassign.Infrastructure.Persistence.Repositories;
using Bassign.Infrastructure.Teams;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

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

        // Data Protection keys (cookies, antiforgery): persisted so restarts don't sign everyone out, and
        // encrypted at rest with DPAPI (machine key) on Windows/IIS. Set DataProtection:KeysPath on IIS.
        var keysPath = configuration["DataProtection:KeysPath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bassign", "keys");
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Bassign")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        if (OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);

        // Startup validation: misconfiguration fails fast with a readable message (secrets never echoed).
        services.AddSingleton<IValidateOptions<WhatsAppOptions>, WhatsAppOptionsValidator>();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
        services.AddOptions<WhatsAppOptions>().ValidateOnStart();
        services.AddOptions<EmailOptions>().ValidateOnStart();
        services.AddOptions<AiOptions>().ValidateOnStart();

        // Privacy (Cyber and Data Protection Act): consent register, access/erasure, retention.
        services.AddScoped<IConsentRegister, ConsentRegister>();
        services.AddScoped<IPersonalDataExporter, PersonalDataExporter>();
        services.AddScoped<IPersonalDataEraser, PersonalDataEraser>();
        services.AddScoped<IRetentionStore, RetentionStore>();
        services.AddOptions<RetentionOptions>().Bind(configuration.GetSection(RetentionOptions.Section));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RetentionOptions>>().Value.ToPolicy());

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditSink, AuditSink>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<OperationUser>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IRunAs, RunAs>();
        services.AddScoped<JwtTokenGenerator>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IIdentityAdmin, IdentityAdmin>();
        services.AddScoped<IInviteNotifier, EmailInviteNotifier>();
        services.AddSingleton<ISecureTokens, SecureTokens>();
        services.AddScoped<ITaskSyncReader, TaskSyncReader>();
        services.AddScoped<IOperationLog, OperationLog>();
        services.AddOptions<MediaOptions>().Bind(configuration.GetSection(MediaOptions.Section));
        services.AddSingleton<IProofStore, LocalDiskProofStore>();

        // Domain events + notification outbox
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<INotificationOutbox, NotificationOutbox>();
        services.AddScoped<NotificationDispatcher>();

        // WhatsApp (docs/whatsapp.md): provider behind IWhatsAppClient; simulator in development.
        services.AddOptions<WhatsAppOptions>().Bind(configuration.GetSection(WhatsAppOptions.Section));
        services.AddSingleton<IWhatsAppSettings, WhatsAppSettings>();
        services.AddScoped<IWhatsAppBindingRepository, WhatsAppBindingRepository>();
        services.AddScoped<IWhatsAppLog, WhatsAppLog>();
        services.AddSingleton<SimulatorMediaStore>();
        var whatsApp = configuration.GetSection(WhatsAppOptions.Section).Get<WhatsAppOptions>() ?? new WhatsAppOptions();
        if (whatsApp.IsSimulator)
        {
            services.AddScoped<IWhatsAppClient, SimulatorWhatsAppClient>();
        }
        else
        {
            services.AddHttpClient<IWhatsAppClient, Dialog360WhatsAppClient>(client =>
                {
                    client.BaseAddress = new Uri(whatsApp.BaseUrl);
                    client.DefaultRequestHeaders.Add("D360-API-KEY", whatsApp.ApiKey);
                })
                .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
        }
        services.AddScoped<INotificationChannel, WhatsAppChannel>();   // tried first …

        // Email (spec §3.C.1 fallback; invites). Provider "pickup" in development writes .eml files.
        var emailSection = configuration.GetSection(EmailOptions.Section);
        services.AddOptions<EmailOptions>().Bind(emailSection);
        var email = emailSection.Get<EmailOptions>() ?? new EmailOptions();
        if (email.IsSendGrid)
            services.AddHttpClient<IEmailSender, SendGridEmailSender>(client =>
                {
                    client.BaseAddress = new Uri("https://api.sendgrid.com/");
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", email.ApiKey);
                })
                .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
        else if (email.IsDisabled)
            services.AddSingleton<IEmailSender, DisabledEmailSender>();
        else
            services.AddSingleton<IEmailSender, PickupFolderEmailSender>();
        services.AddScoped<INotificationChannel, EmailChannel>();      // … then email

        // Inbound: webhook → durable inbox (dedupe) → worker → conversation (docs/whatsapp.md)
        services.AddScoped<IWebhookInboxStore, EfWebhookInboxStore>();
        services.AddScoped<WhatsAppWebhookIntake>();
        services.AddScoped<WhatsAppInboxProcessor>();
        services.AddSingleton<InboxSignal>();
        services.AddScoped<IWhatsAppSimulator, WhatsAppSimulator>();
        services.AddSingleton(TimeProvider.System);

        // AI (spec §3.F): provider is config only. No key ⇒ clients are null and features fall back.
        var aiSection = configuration.GetSection(AiOptions.Section);
        services.AddOptions<AiOptions>().Bind(aiSection);
        var ai = aiSection.Get<AiOptions>() ?? new AiOptions();
        services.AddSingleton(_ => new AiClients(AiClientFactory.CreateChatClient(ai), AiClientFactory.CreateEmbeddingGenerator(ai)));
        services.AddSingleton<IAiUsageMeter, AiUsageMeter>();
        services.AddScoped<ChatAiAssistant>();
        services.AddScoped<ITaskAssignmentAssistant>(sp => sp.GetRequiredService<ChatAiAssistant>());
        services.AddScoped<ISummaryGenerator>(sp => sp.GetRequiredService<ChatAiAssistant>());
        services.AddScoped<ITaskSimilarity, EmbeddingTaskSimilarity>();

        return services;
    }

    /// <summary>Background workers. Register in ONE host only (the API) so messages are sent once.</summary>
    public static IServiceCollection AddBassignWorkers(this IServiceCollection services)
    {
        services.AddHostedService<NotificationWorker>();
        services.AddHostedService<WhatsAppInboxWorker>();
        return services;
    }
}
