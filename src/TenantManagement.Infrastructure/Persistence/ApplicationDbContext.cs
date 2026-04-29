using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;
using TenantManagement.Application.Common.Interfaces;
using TenantManagement.Domain.Common;
using TenantManagement.Domain.Entities;

namespace TenantManagement.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly IMspContextAccessor _mspContextAccessor;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IMspContextAccessor mspContextAccessor) : base(options)
    {
        _mspContextAccessor = mspContextAccessor;
    }

    public DbSet<CustomerTenant> CustomerTenants => Set<CustomerTenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<TenantGroup> TenantGroups => Set<TenantGroup>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(builder);

        // Apply Global Query Filters for all entities inheriting from BaseEntity
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                
                // e => e.MspId == _mspContextAccessor.MspId && !e.IsDeleted
                var mspIdProperty = Expression.Property(parameter, nameof(BaseEntity.MspId));
                var mspIdValue = Expression.Property(Expression.Constant(this), 
                    typeof(ApplicationDbContext).GetProperty(nameof(CurrentMspId), BindingFlags.NonPublic | BindingFlags.Instance)!);
                var mspIdCondition = Expression.Equal(mspIdProperty, mspIdValue);

                var isDeletedProperty = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                var isDeletedValue = Expression.Constant(false);
                var isDeletedCondition = Expression.Equal(isDeletedProperty, isDeletedValue);

                var combinedCondition = Expression.AndAlso(mspIdCondition, isDeletedCondition);
                
                var filter = Expression.Lambda(combinedCondition, parameter);
                
                builder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    private string? CurrentMspId => _mspContextAccessor.MspId;
}
