namespace ElectCrm.Domain.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

public static class DomainServiceCollectionExtensions
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        return services;
    }
}
