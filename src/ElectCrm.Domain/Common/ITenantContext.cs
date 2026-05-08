namespace ElectCrm.Domain.Common;

using ElectCrm.Shared;

public interface ITenantContext
{
    TenantId CurrentTenantId { get; }
}
