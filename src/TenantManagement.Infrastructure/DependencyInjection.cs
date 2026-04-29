using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenantManagement.Application.Common.Interfaces;
using TenantManagement.Infrastructure.Persistence;

namespace TenantManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection") ?? "Data Source=TenantManagement.db",
                builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        // Since Docker is not available, we use DistributedMemoryCache instead of Redis for local dev
        services.AddDistributedMemoryCache();

        // Configure L1 Cache (Memory)
        services.AddMemoryCache();

        return services;
    }
}
