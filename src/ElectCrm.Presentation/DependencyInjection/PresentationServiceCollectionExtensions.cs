namespace ElectCrm.Presentation.DependencyInjection;

using ElectCrm.Presentation.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

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

            options.AddPolicy(PolicyNames.UserAdmin,
                p => p.AddRequirements(new UserAdminRequirement()));

            options.AddPolicy(PolicyNames.Worker,
                p => p.AddRequirements(new WorkerRequirement()));
        });

        services.AddScoped<IAuthorizationHandler, HasRoleHandler>();
        services.AddScoped<IAuthorizationHandler, UserAdminRequirementHandler>();
        services.AddScoped<IAuthorizationHandler, WorkerRequirementHandler>();

        // 5-minute window ensures role changes propagate to active sessions — see Plan 08 §1.3
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromMinutes(5);
        });

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("WorkerRegistration", limiterOptions =>
            {
                limiterOptions.PermitLimit = 10;
                limiterOptions.Window = TimeSpan.FromMinutes(10);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "text/html";
                await context.HttpContext.Response.WriteAsync(
                    "<p>Too many registration attempts. Please try again later.</p>", ct);
            };
        });

        return services;
    }
}
