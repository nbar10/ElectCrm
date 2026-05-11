namespace ElectCrm.Application.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ContactService and future application services register here once the Application
        // project gains a direct reference to Infrastructure. Currently ContactService lives
        // in Infrastructure to avoid a circular project reference.
        return services;
    }
}
