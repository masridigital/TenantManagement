using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using TenantManagement.Domain.Entities;

namespace TenantManagement.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<CustomerTenant> CustomerTenants { get; }
    DbSet<TenantUser> TenantUsers { get; }
    DbSet<TenantGroup> TenantGroups { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
