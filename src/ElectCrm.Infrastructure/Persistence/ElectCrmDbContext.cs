namespace ElectCrm.Infrastructure.Persistence;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Contacts;
using ElectCrm.Domain.Users;
using ElectCrm.Infrastructure.Identity;
using ElectCrm.Shared;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public sealed class ElectCrmDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext _tenantContext;

    public ElectCrmDbContext(DbContextOptions<ElectCrmDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<AgencyBrand> AgencyBrands => Set<AgencyBrand>();
    public DbSet<Branch> Branches => Set<Branch>();
    public new DbSet<User> Users => Set<User>();
    public DbSet<UserInvite> UserInvites => Set<UserInvite>();
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ElectCrmDbContext).Assembly);

        ApplyGlobalQueryFilters(modelBuilder);
    }

    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        // AgencyBrand is the tenant itself — filter on its own Id.
        modelBuilder.Entity<AgencyBrand>().HasQueryFilter(
            e => _tenantContext.CurrentTenantId == TenantId.Empty
                 || e.Id == _tenantContext.CurrentTenantId.Value);

        // All other tenant-scoped entities filter on AgencyBrandId.
        modelBuilder.Entity<Branch>().HasQueryFilter(
            e => _tenantContext.CurrentTenantId == TenantId.Empty
                 || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value);

        modelBuilder.Entity<User>().HasQueryFilter(
            e => _tenantContext.CurrentTenantId == TenantId.Empty
                 || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value);

        modelBuilder.Entity<UserInvite>().HasQueryFilter(
            e => _tenantContext.CurrentTenantId == TenantId.Empty
                 || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value);

        modelBuilder.Entity<Contact>().HasQueryFilter(
            e => (_tenantContext.CurrentTenantId == TenantId.Empty
                  || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value)
                 && !e.IsDeleted);
    }
}
