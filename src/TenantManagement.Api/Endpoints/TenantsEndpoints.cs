using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TenantManagement.Application.Tenants.Queries.GetCustomerTenants;

namespace TenantManagement.Api.Endpoints;

public static class TenantsEndpoints
{
    public static void MapTenantsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants")
                       .RequireAuthorization(); // Ensures token is valid, so MspContextAccessor works

        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCustomerTenantsQuery());
            return Results.Ok(result);
        })
        .WithName("GetCustomerTenants")
        .WithSummary("Retrieves all customer tenants for the authenticated MSP");
    }
}
