namespace ElectCrm.Domain.Common;

using ElectCrm.Shared;

/// <summary>
/// Marker interface applied to all tenant-scoped entities.
/// AppDbContext uses this to apply global query filters automatically.
/// </summary>
public interface IHasTenantId
{
    TenantId TenantId { get; }
}
