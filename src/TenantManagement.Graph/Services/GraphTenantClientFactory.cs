using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using System.Threading;
using System.Threading.Tasks;
using TenantManagement.Application.Common.Interfaces;

namespace TenantManagement.Graph.Services;

public class GraphTenantClientFactory : IGraphTenantClient
{
    private readonly ITokenAcquisition _tokenAcquisition;

    public GraphTenantClientFactory(ITokenAcquisition tokenAcquisition)
    {
        _tokenAcquisition = tokenAcquisition;
    }

    public Task<GraphServiceClient> GetClientForTenantAsync(string customerTenantId)
    {
        // We create an authentication provider that uses ITokenAcquisition to get a token
        // specifically for the target customerTenantId. This assumes we have Application permissions
        // or Delegated permissions correctly consented in that tenant (e.g. via CSP GDAP).
        var authenticationProvider = new TokenAcquisitionAuthenticationProvider(_tokenAcquisition, customerTenantId);
        
        var client = new GraphServiceClient(authenticationProvider);
        return Task.FromResult(client);
    }
}

public class TokenAcquisitionAuthenticationProvider : IAuthenticationProvider
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly string _tenantId;

    public TokenAcquisitionAuthenticationProvider(ITokenAcquisition tokenAcquisition, string tenantId)
    {
        _tokenAcquisition = tokenAcquisition;
        _tenantId = tenantId;
    }

    public async Task AuthenticateRequestAsync(
        Microsoft.Kiota.Abstractions.RequestInformation request, 
        Dictionary<string, object>? additionalAuthenticationContext = null, 
        CancellationToken cancellationToken = default)
    {
        // Scopes for Graph API
        var scopes = new[] { "https://graph.microsoft.com/.default" };

        // Attempt to acquire an app token for the specific tenant
        // If user context is required (Delegated), GetAccessTokenForUserAsync would be used based on context.
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(
            scopes[0], 
            tenant: _tenantId);

        request.Headers.Add("Authorization", $"Bearer {token}");
    }
}
