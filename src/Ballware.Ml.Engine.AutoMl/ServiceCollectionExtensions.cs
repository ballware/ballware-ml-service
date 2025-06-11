using Ballware.Ml.Engine.AutoMl.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Ml.Engine.AutoMl;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareAutoMlExecutor(this IServiceCollection services)
    {
        services.AddScoped<IModelExecutor, AutoMlExecutor>();

        return services;
    }
}