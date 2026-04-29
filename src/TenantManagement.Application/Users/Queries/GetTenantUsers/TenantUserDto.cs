using System;

namespace TenantManagement.Application.Users.Queries.GetTenantUsers;

public record TenantUserDto(
    Guid GraphUserId,
    string UserPrincipalName,
    string DisplayName,
    bool AccountEnabled,
    string? JobTitle,
    string? Department
);
