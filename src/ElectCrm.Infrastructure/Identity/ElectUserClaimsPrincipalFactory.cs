namespace ElectCrm.Infrastructure.Identity;

using System.Security.Claims;
using ElectCrm.Domain.Users;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

public sealed class ElectUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly ElectCrmDbContext _dbContext;

    public ElectUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        ElectCrmDbContext dbContext)
        : base(userManager, roleManager, optionsAccessor)
    {
        _dbContext = dbContext;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Bypass global query filter — no tenant claim exists on the principal yet.
        var domainUser = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.DomainUserId);

        if (domainUser is not null)
        {
            identity.AddClaim(new Claim(
                TenantContextAccessor.AgencyBrandIdClaimType,
                domainUser.AgencyBrandId.ToString()));
        }

        return identity;
    }
}
