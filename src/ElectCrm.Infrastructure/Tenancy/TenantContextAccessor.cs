namespace ElectCrm.Infrastructure.Tenancy;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;
using Microsoft.AspNetCore.Http;

public sealed class TenantContextAccessor : ITenantContext
{
    public const string AgencyBrandIdClaimType = "agency_brand_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public TenantId CurrentTenantId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User
                .FindFirst(AgencyBrandIdClaimType);

            if (claim is null || !Guid.TryParse(claim.Value, out var id))
                return TenantId.Empty;

            return new TenantId(id);
        }
    }
}
