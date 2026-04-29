using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TenantManagement.Application.Common.Interfaces;

namespace TenantManagement.Application.Users.Queries.GetTenantUsers;

public record GetTenantUsersQuery(Guid CustomerTenantId) : IRequest<List<TenantUserDto>>;

public class GetTenantUsersQueryHandler : IRequestHandler<GetTenantUsersQuery, List<TenantUserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly IMspContextAccessor _mspContextAccessor;

    public GetTenantUsersQueryHandler(
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

    public async Task<List<TenantUserDto>> Handle(GetTenantUsersQuery request, CancellationToken cancellationToken)
    {
        var mspId = _mspContextAccessor.MspId;
        if (string.IsNullOrEmpty(mspId))
        {
            return new List<TenantUserDto>();
        }

        string cacheKey = $"tenant:{mspId}:customerTenant:{request.CustomerTenantId}:users";

        // 1. Check L1 Cache (In-Memory)
        if (_memoryCache.TryGetValue(cacheKey, out List<TenantUserDto>? cachedUsers) && cachedUsers != null)
        {
            return cachedUsers;
        }

        // 2. Check L2 Cache (Redis)
        var distributedCacheBytes = await _distributedCache.GetAsync(cacheKey, cancellationToken);
        if (distributedCacheBytes != null)
        {
            var l2CachedUsers = JsonSerializer.Deserialize<List<TenantUserDto>>(distributedCacheBytes);
            if (l2CachedUsers != null)
            {
                // Populate L1 cache
                _memoryCache.Set(cacheKey, l2CachedUsers, System.TimeSpan.FromMinutes(1));
                return l2CachedUsers;
            }
        }

        // 3. Fallback to L3 (Postgres Projection)
        // EF Core Global Query Filters automatically ensure we only get this MSP's data.
        var users = await _context.TenantUsers
            .Where(u => u.CustomerTenantId == request.CustomerTenantId)
            .Select(u => new TenantUserDto(
                u.GraphUserId, 
                u.UserPrincipalName, 
                u.DisplayName, 
                u.AccountEnabled, 
                u.JobTitle, 
                u.Department))
            .ToListAsync(cancellationToken);

        // Populate L2 cache
        var serializedUsers = JsonSerializer.SerializeToUtf8Bytes(users);
        await _distributedCache.SetAsync(cacheKey, serializedUsers, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = System.TimeSpan.FromHours(1)
        }, cancellationToken);

        // Populate L1 cache
        _memoryCache.Set(cacheKey, users, System.TimeSpan.FromMinutes(1));

        return users;
    }
}
