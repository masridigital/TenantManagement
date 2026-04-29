using Microsoft.Extensions.DependencyInjection;
using TenantManagement.Application.Common.Interfaces;
using TenantManagement.Graph.Services;

namespace TenantManagement.Graph;

public static class DependencyInjection
{
    public static IServiceCollection AddGraphServices(this IServiceCollection services)
    {
        services.AddTransient<IGraphTenantClient, GraphTenantClientFactory>();

        return services;
    }
}
