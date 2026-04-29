using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using TenantManagement.Api.Endpoints;
using TenantManagement.Application;
using TenantManagement.Graph;
using TenantManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add Application and Infrastructure layer services
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddGraphServices();

// Register context accessors
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantManagement.Application.Common.Interfaces.IMspContextAccessor, TenantManagement.Api.Services.MspContextAccessor>();

// Configure Authentication & Authorization using Microsoft.Identity.Web
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddMicrosoftGraph(builder.Configuration.GetSection("MicrosoftGraph"))
        .AddDistributedTokenCaches();

builder.Services.AddAuthorization(options =>
{
    // By default, all endpoints require authentication
    options.FallbackPolicy = options.DefaultPolicy;
});

// Add services to the container.
builder.Services.AddOpenApi();

// Build the app
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Minimal APIs grouping Example
var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
   .WithName("GetHealth")
   .AllowAnonymous();

// Map domain endpoints
app.MapTenantsEndpoints();
app.MapUsersEndpoints();

app.Run();
