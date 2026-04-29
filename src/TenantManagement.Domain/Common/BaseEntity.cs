using System;

namespace TenantManagement.Domain.Common;

public abstract class BaseEntity
{
    public string MspId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string? CreatedById { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public string? UpdatedById { get; private set; }
    public uint RowVersion { get; private set; }
    public bool IsDeleted { get; private set; }

    // Protected parameterless constructor for EF Core
    protected BaseEntity() { }

    protected BaseEntity(string mspId)
    {
        if (string.IsNullOrWhiteSpace(mspId))
        {
            throw new ArgumentException("MspId is required.", nameof(mspId));
        }
        MspId = mspId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateAudit(string? updatedById)
    {
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedById = updatedById;
    }

    public void MarkAsDeleted(string? deletedById)
    {
        IsDeleted = true;
        UpdateAudit(deletedById);
    }
}
