namespace ElectCrm.Infrastructure.DependencyInjection;

using ElectCrm.Application.Features.Persons;
using ElectCrm.Domain.Common;
using ElectCrm.Infrastructure.Features.Admin;
using ElectCrm.Infrastructure.Features.Candidates;
using ElectCrm.Infrastructure.Features.Contacts;
using ElectCrm.Infrastructure.Features.Persons;
using ElectCrm.Infrastructure.Identity;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Infrastructure.Services;
using ElectCrm.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContextFactory<ElectCrmDbContext>(
            options => options.UseSqlServer(connectionString),
            ServiceLifetime.Scoped);

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
            })
            .AddEntityFrameworkStores<ElectCrmDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ElectUserClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.AccessDeniedPath = "/access-denied";
        });

        services.AddHttpContextAccessor();

        services.AddScoped<ITenantContext, TenantContextAccessor>();
        services.AddScoped<IDomainEventDispatcher, NoOpDomainEventDispatcher>();

        services.AddScoped<AgencyBrandAdminService>();
        services.AddScoped<BranchAdminService>();
        services.AddScoped<CandidateService>();
        services.AddScoped<ContactService>();
        services.AddScoped<PersonService>();
        services.AddScoped<IPersonHashingService, PersonHashingService>();

        return services;
    }
}
