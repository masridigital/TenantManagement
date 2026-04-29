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

public class CustomerTenantSyncJob
{
    private readonly IApplicationDbContext _context;
    private readonly IGraphTenantClient _graphClientFactory;
    private readonly ILogger<CustomerTenantSyncJob> _logger;

    public CustomerTenantSyncJob(
        IApplicationDbContext context,
        IGraphTenantClient graphClientFactory,
        ILogger<CustomerTenantSyncJob> logger)
    {
        _context = context;
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes the local L3 projection cache of customer tenants from Microsoft Graph.
    /// This represents the Hangfire background delta-sync process described in the architecture.
    /// </summary>
    /// <param name="mspId">The Multi-Tenant ID representing the MSP.</param>
    public async Task SyncTenantsForMspAsync(string mspId)
    {
        _logger.LogInformation("Starting tenant delta-sync for MSP {MspId}", mspId);

        try
        {
            // Usually, an MSP manages many tenants. In CSP scenarios, we'd query the Partner Center API
            // or a cross-tenant Graph query to get the list. For this foundation test, we assume we 
            // are syncing the MSP's own primary tenant data as a proxy for the 'CustomerTenant' list.
            var client = await _graphClientFactory.GetClientForTenantAsync(mspId);

            // Fetch organization details (mimicking fetching a list of tenants)
            var orgs = await client.Organization.GetAsync();

            if (orgs?.Value == null) return;

            foreach (var org in orgs.Value)
            {
                var tenantId = Guid.Parse(org.Id!);
                var domain = org.VerifiedDomains?.FirstOrDefault(d => d.IsDefault == true)?.Name ?? "unknown.onmicrosoft.com";
                var displayName = org.DisplayName ?? "Unknown Tenant";

                // We must use IgnoreQueryFilters here if the DbContext strictly relies on IMspContextAccessor 
                // which is empty in a background job context, or we just write it directly.
                // In a true implementation, we'd have a Scoped IMspContextSetter.
                var existingTenant = await _context.CustomerTenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.MspId == mspId && t.CustomerTenantId == tenantId);

                if (existingTenant == null)
                {
                    _logger.LogInformation("Adding new tenant {TenantName} for MSP {MspId}", displayName, mspId);
                    
                    var newTenant = new CustomerTenant(mspId, tenantId, domain, displayName);
                    _context.CustomerTenants.Add(newTenant);
                }
                else
                {
                    _logger.LogInformation("Updating tenant {TenantName} for MSP {MspId}", displayName, mspId);
                    existingTenant.UpdateDisplayName(displayName, "System-Hangfire-Job");
                }
            }

            await _context.SaveChangesAsync(default);
            _logger.LogInformation("Completed tenant delta-sync for MSP {MspId}", mspId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync tenants for MSP {MspId}", mspId);
            throw;
        }
    }
}
