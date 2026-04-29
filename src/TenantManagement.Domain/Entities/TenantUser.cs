using System;
using TenantManagement.Domain.Common;

namespace TenantManagement.Domain.Entities;

/// <summary>
/// Represents a cached Microsoft Graph User projected into the L3 Postgres database.
/// Used to display user lists instantly without hitting the Graph API synchronously.
/// </summary>
public class TenantUser : BaseEntity
{
    public Guid Id { get; private set; }
    public Guid CustomerTenantId { get; private set; }
    
    /// <summary>
    /// The ObjectId of the user in Entra ID.
    /// </summary>
    public Guid GraphUserId { get; private set; }
    
    public string UserPrincipalName { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool AccountEnabled { get; private set; }
    public string? JobTitle { get; private set; }
    public string? Department { get; private set; }

    protected TenantUser() { } // For EF Core

    public TenantUser(
        string mspId, 
        Guid customerTenantId, 
        Guid graphUserId, 
        string userPrincipalName, 
        string displayName, 
        bool accountEnabled) 
        : base(mspId)
    {
        Id = Guid.NewGuid();
        CustomerTenantId = customerTenantId;
        GraphUserId = graphUserId;
        UserPrincipalName = userPrincipalName;
        DisplayName = displayName;
        AccountEnabled = accountEnabled;
    }

    public void UpdateProfile(string displayName, string userPrincipalName, bool accountEnabled, string? jobTitle, string? department, string? updatedById = null)
    {
        DisplayName = displayName;
        UserPrincipalName = userPrincipalName;
        AccountEnabled = accountEnabled;
        JobTitle = jobTitle;
        Department = department;
        
        UpdateAudit(updatedById);
    }
}
