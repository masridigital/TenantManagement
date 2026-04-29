using Hangfire;
using Hangfire.MemoryStorage;
using TenantManagement.Application;
using TenantManagement.Graph;
using TenantManagement.Infrastructure;
using TenantManagement.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Add Application and Infrastructure layer services
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddGraphServices();

// Configure Hangfire to use Memory
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseMemoryStorage());

// Add the Hangfire processing server as IHostedService
builder.Services.AddHangfireServer();

// Register background jobs
builder.Services.AddTransient<TenantManagement.Worker.Jobs.CustomerTenantSyncJob>();
builder.Services.AddTransient<TenantManagement.Worker.Jobs.TenantUserSyncJob>();
builder.Services.AddTransient<TenantManagement.Worker.Jobs.TenantGroupSyncJob>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
