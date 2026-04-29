using System;
using TenantManagement.Domain.Common;

namespace TenantManagement.Domain.Entities;

public class TenantGroup : BaseEntity
{
    public Guid Id { get; private set; }
    public Guid CustomerTenantId { get; private set; }
    public Guid GraphGroupId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool MailEnabled { get; private set; }
    public bool SecurityEnabled { get; private set; }
    public string? GroupTypes { get; private set; } // Comma-separated for simplicity in L3

    protected TenantGroup() { }

    public TenantGroup(
        string mspId,
        Guid customerTenantId,
        Guid graphGroupId,
        string displayName,
        bool mailEnabled,
        bool securityEnabled)
        : base(mspId)
    {
        Id = Guid.NewGuid();
        CustomerTenantId = customerTenantId;
        GraphGroupId = graphGroupId;
        DisplayName = displayName;
        MailEnabled = mailEnabled;
        SecurityEnabled = securityEnabled;
    }

    public void UpdateProfile(string displayName, string? description, bool mailEnabled, bool securityEnabled, string? groupTypes, string? updatedById = null)
    {
        DisplayName = displayName;
        Description = description;
        MailEnabled = mailEnabled;
        SecurityEnabled = securityEnabled;
        GroupTypes = groupTypes;
        
        UpdateAudit(updatedById);
    }
}
