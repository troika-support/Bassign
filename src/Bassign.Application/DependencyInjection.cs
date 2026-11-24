using Bassign.Application.Common.Behaviors;
using Bassign.Application.Common.Events;
using Bassign.Application.Notifications;
using Bassign.Domain.Tasks;
using Bassign.Application.Tasks;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Order = execution order: validation (outermost, fails fast) then audit (after the handler).
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));

        services.AddScoped<ITaskAccess, TaskAccess>();
        services.AddScoped<Notifications.NotificationMessageBuilder>();

        // Domain-event handlers (run inside the unit of work; they only record follow-up work).
        services.AddScoped<IDomainEventHandler<TaskAssigned>, NotifyAssigneeOnTaskAssigned>();

        return services;
    }
}
