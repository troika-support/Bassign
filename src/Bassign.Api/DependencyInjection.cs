using Bassign.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddProblemDetails();
        services.AddExceptionHandler<ValidationExceptionHandler>();
        return services;
    }
}
