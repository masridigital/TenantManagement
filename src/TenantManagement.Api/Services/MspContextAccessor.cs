using System.Security.Claims;
using TenantManagement.Application.Common.Interfaces;

namespace TenantManagement.Api.Services;

public class MspContextAccessor : IMspContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MspContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? MspId 
    {
        get 
        {
            // First, try to get the MSP ID from the claims (assuming standard naming or custom claim "msp_id")
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("msp_id") ??
                        _httpContextAccessor.HttpContext?.User?.FindFirst("extension_MspId");
            
            // For development / placeholder purposes, if the user is authenticated but no msp_id claim exists, we can default or throw.
            // In a production scenario with strict multi-tenancy, if MspId is null, we should throw an UnauthorizedAccessException.
            return claim?.Value;
        }
    }
}
