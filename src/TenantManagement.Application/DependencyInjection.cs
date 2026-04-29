using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace TenantManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            // Add Behaviors like validation here later
        });

        return services;
    }
}
