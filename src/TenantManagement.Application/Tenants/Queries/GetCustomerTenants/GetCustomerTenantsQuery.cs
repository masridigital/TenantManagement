using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TenantManagement.Application.Common.Interfaces;

namespace TenantManagement.Application.Tenants.Queries.GetCustomerTenants;

public record GetCustomerTenantsQuery : IRequest<List<CustomerTenantDto>>;

public class GetCustomerTenantsQueryHandler : IRequestHandler<GetCustomerTenantsQuery, List<CustomerTenantDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly IMspContextAccessor _mspContextAccessor;

    public GetCustomerTenantsQueryHandler(
        IApplicationDbContext context,
        IMemoryCache memoryCache,
        IDistributedCache distributedCache,
        IMspContextAccessor mspContextAccessor)
    {
        _context = context;
        _memoryCache = memoryCache;
        _distributedCache = distributedCache;
        _mspContextAccessor = mspContextAccessor;
    }

    public async Task<List<CustomerTenantDto>> Handle(GetCustomerTenantsQuery request, CancellationToken cancellationToken)
    {
        var mspId = _mspContextAccessor.MspId;
        if (string.IsNullOrEmpty(mspId))
        {
            return new List<CustomerTenantDto>();
        }

        string cacheKey = $"tenant:{mspId}:customerTenants:list";

        // 1. Check L1 Cache (In-Memory)
        if (_memoryCache.TryGetValue(cacheKey, out List<CustomerTenantDto>? cachedTenants) && cachedTenants != null)
        {
            return cachedTenants;
        }

        // 2. Check L2 Cache (Redis)
        var distributedCacheBytes = await _distributedCache.GetAsync(cacheKey, cancellationToken);
        if (distributedCacheBytes != null)
        {
            var l2CachedTenants = JsonSerializer.Deserialize<List<CustomerTenantDto>>(distributedCacheBytes);
            if (l2CachedTenants != null)
            {
                // Populate L1 cache
                _memoryCache.Set(cacheKey, l2CachedTenants, System.TimeSpan.FromMinutes(1));
                return l2CachedTenants;
            }
        }

        // 3. Fallback to L3 (Postgres Projection)
        // EF Core Global Query Filters will automatically ensure we only get this MSP's tenants.
        var tenants = await _context.CustomerTenants
            .Select(t => new CustomerTenantDto(t.CustomerTenantId, t.DefaultDomainName, t.DisplayName))
            .ToListAsync(cancellationToken);

        // Populate L2 cache
        var serializedTenants = JsonSerializer.SerializeToUtf8Bytes(tenants);
        await _distributedCache.SetAsync(cacheKey, serializedTenants, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = System.TimeSpan.FromHours(1)
        }, cancellationToken);

        // Populate L1 cache
        _memoryCache.Set(cacheKey, tenants, System.TimeSpan.FromMinutes(1));

        return tenants;
    }
}
