using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using TenantManagement.Application.Common.Interfaces;
using TenantManagement.Domain.Entities;

namespace TenantManagement.Worker.Jobs;

public class TenantUserSyncJob
{
    private readonly IApplicationDbContext _context;
    private readonly IGraphTenantClient _graphClientFactory;
    private readonly ILogger<TenantUserSyncJob> _logger;

    public TenantUserSyncJob(
        IApplicationDbContext context,
        IGraphTenantClient graphClientFactory,
        ILogger<TenantUserSyncJob> logger)
    {
        _context = context;
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes users from Microsoft Graph for a specific customer tenant.
    /// In production, this would use deltaLinks stored in the database.
    /// </summary>
    /// <param name="mspId">The Multi-Tenant ID representing the MSP.</param>
    /// <param name="customerTenantId">The Target Tenant ID.</param>
    public async Task SyncUsersForTenantAsync(string mspId, Guid customerTenantId)
    {
        _logger.LogInformation("Starting user sync for tenant {CustomerTenantId} under MSP {MspId}", customerTenantId, mspId);

        try
        {
            var client = await _graphClientFactory.GetClientForTenantAsync(customerTenantId.ToString());

            // Fetch users (First page)
            var usersResponse = await client.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = new[] { "id", "userPrincipalName", "displayName", "accountEnabled", "jobTitle", "department" };
            });

            if (usersResponse?.Value == null) return;

            // Simplified Page Iterator approach using raw lists for foundation test
            var graphUsers = usersResponse.Value;

            foreach (var graphUser in graphUsers)
            {
                if (graphUser.Id == null) continue;

                var graphUserId = Guid.Parse(graphUser.Id);
                var upn = graphUser.UserPrincipalName ?? string.Empty;
                var displayName = graphUser.DisplayName ?? string.Empty;
                var accountEnabled = graphUser.AccountEnabled ?? false;

                // Again, IgnoreQueryFilters since the background worker lacks IMspContextAccessor's HTTP Context
                var existingUser = await _context.TenantUsers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.MspId == mspId && u.CustomerTenantId == customerTenantId && u.GraphUserId == graphUserId);

                if (existingUser == null)
                {
                    var newUser = new TenantUser(mspId, customerTenantId, graphUserId, upn, displayName, accountEnabled);
                    newUser.UpdateProfile(displayName, upn, accountEnabled, graphUser.JobTitle, graphUser.Department, "System-Hangfire-Job");
                    _context.TenantUsers.Add(newUser);
                }
                else
                {
                    existingUser.UpdateProfile(displayName, upn, accountEnabled, graphUser.JobTitle, graphUser.Department, "System-Hangfire-Job");
                }
            }

            await _context.SaveChangesAsync(default);
            _logger.LogInformation("Completed user sync for tenant {CustomerTenantId}", customerTenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync users for tenant {CustomerTenantId}", customerTenantId);
            throw;
        }
    }
}
