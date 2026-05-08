namespace ElectCrm.Infrastructure.DependencyInjection;

using ElectCrm.Domain.Common;
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

        services.AddDbContext<ElectCrmDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
            })
            .AddEntityFrameworkStores<ElectCrmDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ElectUserClaimsPrincipalFactory>();

        services.AddHttpContextAccessor();

        services.AddScoped<ITenantContext, TenantContextAccessor>();
        services.AddScoped<IDomainEventDispatcher, NoOpDomainEventDispatcher>();

        return services;
    }
}
