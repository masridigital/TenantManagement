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

public class TenantGroupSyncJob
{
    private readonly IApplicationDbContext _context;
    private readonly IGraphTenantClient _graphClientFactory;
    private readonly ILogger<TenantGroupSyncJob> _logger;

    public TenantGroupSyncJob(
        IApplicationDbContext context,
        IGraphTenantClient graphClientFactory,
        ILogger<TenantGroupSyncJob> logger)
    {
        _context = context;
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes groups from Microsoft Graph for a specific customer tenant.
    /// </summary>
    /// <param name="mspId">The Multi-Tenant ID representing the MSP.</param>
    /// <param name="customerTenantId">The Target Tenant ID.</param>
    public async Task SyncGroupsForTenantAsync(string mspId, Guid customerTenantId)
    {
        _logger.LogInformation("Starting group sync for tenant {CustomerTenantId} under MSP {MspId}", customerTenantId, mspId);

        try
        {
            var client = await _graphClientFactory.GetClientForTenantAsync(customerTenantId.ToString());

            // Fetch groups
            var groupsResponse = await client.Groups.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "description", "mailEnabled", "securityEnabled", "groupTypes" };
            });

            if (groupsResponse?.Value == null) return;

            var graphGroups = groupsResponse.Value;

            foreach (var graphGroup in graphGroups)
            {
                if (graphGroup.Id == null) continue;

                var graphGroupId = Guid.Parse(graphGroup.Id);
                var displayName = graphGroup.DisplayName ?? string.Empty;
                var mailEnabled = graphGroup.MailEnabled ?? false;
                var securityEnabled = graphGroup.SecurityEnabled ?? false;
                var groupTypes = graphGroup.GroupTypes != null ? string.Join(",", graphGroup.GroupTypes) : null;

                var existingGroup = await _context.TenantGroups
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(g => g.MspId == mspId && g.CustomerTenantId == customerTenantId && g.GraphGroupId == graphGroupId);

                if (existingGroup == null)
                {
                    var newGroup = new TenantGroup(mspId, customerTenantId, graphGroupId, displayName, mailEnabled, securityEnabled);
                    newGroup.UpdateProfile(displayName, graphGroup.Description, mailEnabled, securityEnabled, groupTypes, "System-Hangfire-Job");
                    _context.TenantGroups.Add(newGroup);
                }
                else
                {
                    existingGroup.UpdateProfile(displayName, graphGroup.Description, mailEnabled, securityEnabled, groupTypes, "System-Hangfire-Job");
                }
            }

            await _context.SaveChangesAsync(default);
            _logger.LogInformation("Completed group sync for tenant {CustomerTenantId}", customerTenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync groups for tenant {CustomerTenantId}", customerTenantId);
            throw;
        }
    }
}
