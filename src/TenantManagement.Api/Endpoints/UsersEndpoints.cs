using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using TenantManagement.Application.Users.Queries.GetTenantUsers;

namespace TenantManagement.Api.Endpoints;

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants/{customerTenantId:guid}/users")
                       .RequireAuthorization();

        group.MapGet("/", async (Guid customerTenantId, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetTenantUsersQuery(customerTenantId));
            return Results.Ok(result);
        })
        .WithName("GetTenantUsers")
        .WithSummary("Retrieves all cached users for a specific customer tenant");
    }
}
