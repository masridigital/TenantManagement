using System;
using TenantManagement.Domain.Common;

namespace TenantManagement.Domain.Entities;

public class CustomerTenant : BaseEntity
{
    public Guid CustomerTenantId { get; private set; }
    public string DefaultDomainName { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    protected CustomerTenant() { } // For EF Core

    public CustomerTenant(string mspId, Guid customerTenantId, string defaultDomainName, string displayName) 
        : base(mspId)
    {
        CustomerTenantId = customerTenantId;
        DefaultDomainName = defaultDomainName;
        DisplayName = displayName;
    }

    public void UpdateDisplayName(string displayName, string? updatedById = null)
    {
        DisplayName = displayName;
        UpdateAudit(updatedById);
    }
}
