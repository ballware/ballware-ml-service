using Ballware.Ml.Caching.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Ml.Caching;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareMlMemoryCaching(this IServiceCollection services)
    {
        services.AddSingleton<ITenantAwareModelCache, InMemoryTenantAwareModelCache>();

        return services;
    }

}