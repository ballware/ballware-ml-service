using Ballware.Ml.Authorization.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Ml.Authorization;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareMlAuthorizationUtils(this IServiceCollection services, string tenantClaim, string userIdClaim, string rightClaim)
    {
        services.AddSingleton<IPrincipalUtils>(new DefaultPrincipalUtils(tenantClaim, userIdClaim, rightClaim));

        return services;
    }
}