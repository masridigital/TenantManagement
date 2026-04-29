using System;

namespace TenantManagement.Application.Tenants.Queries.GetCustomerTenants;

public record CustomerTenantDto(
    Guid CustomerTenantId,
    string DefaultDomainName,
    string DisplayName
);
