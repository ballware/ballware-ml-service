using Ballware.Shared.Data.Repository;
using Ballware.Ml.Data.Ef.Configuration;
using Ballware.ML.Data.Ef.Model;
using Ballware.Ml.Data.Ef.SqlServer.Internal;
using Ballware.Ml.Data.Ef.SqlServer.Repository;
using Ballware.Ml.Data.Public;
using Ballware.Ml.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Ml.Data.Ef.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareMlStorageForSqlServer(this IServiceCollection services, StorageOptions options, string connectionString)
    {
        services.AddSingleton(options);
        services.AddDbContext<MlDbContext>(o =>
        {
            o.UseSqlServer(connectionString, o =>
            {
                o.MigrationsAssembly(typeof(MlDbContext).Assembly.FullName);
            });

            o.UseSnakeCaseNamingConvention();
            o.ReplaceService<IModelCustomizer, MlModelBaseCustomizer>();
        });

        services.AddScoped<IMlDbContext, MlDbContext>();
        
        services.AddScoped<ITenantableRepository<MlModel>, MlModelRepository>();
        services.AddScoped<IMlModelMetaRepository, MlModelRepository>();

        services.AddSingleton<IMlDbConnectionFactory>(new MlDbConnectionFactory(connectionString));
        services.AddHostedService<InitializationWorker>();

        return services;
    }
}