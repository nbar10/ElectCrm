namespace ElectCrm.Presentation.DependencyInjection;

using ElectCrm.Presentation.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyNames.AnyStaff,
                p => p.RequireAuthenticatedUser());

            options.AddPolicy(PolicyNames.Consultant,
                p => p.AddRequirements(new HasRoleRequirement(nameof(PolicyNames.Consultant))));

            options.AddPolicy(PolicyNames.BranchManager,
                p => p.AddRequirements(new HasRoleRequirement(nameof(PolicyNames.BranchManager))));

            options.AddPolicy(PolicyNames.BrandAdmin,
                p => p.AddRequirements(new HasRoleRequirement(nameof(PolicyNames.BrandAdmin))));

            options.AddPolicy(PolicyNames.ComplianceOfficer,
                p => p.AddRequirements(new HasRoleRequirement(nameof(PolicyNames.ComplianceOfficer))));

            options.AddPolicy(PolicyNames.FinanceOfficer,
                p => p.AddRequirements(new HasRoleRequirement(nameof(PolicyNames.FinanceOfficer))));

            options.AddPolicy(PolicyNames.GroupAdmin,
                p => p.AddRequirements(new HasRoleRequirement(nameof(PolicyNames.GroupAdmin))));
        });

        services.AddScoped<IAuthorizationHandler, HasRoleHandler>();

        return services;
    }
}
