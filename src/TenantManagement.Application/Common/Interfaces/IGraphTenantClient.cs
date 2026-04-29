using Microsoft.Graph;
using System.Threading.Tasks;

namespace TenantManagement.Application.Common.Interfaces;

public interface IGraphTenantClient
{
    /// <summary>
    /// Gets a configured GraphServiceClient scoped to the specified customer tenant.
    /// This uses the underlying authentication flows (e.g., Delegated, Application) 
    /// required to access the target tenant's resources.
    /// </summary>
    /// <param name="customerTenantId">The ID of the customer tenant (target tenant).</param>
    /// <returns>A configured GraphServiceClient.</returns>
    Task<GraphServiceClient> GetClientForTenantAsync(string customerTenantId);
}
