using Ballware.Ml.Jobs.Internal;
using Ballware.Shared.Api.Jobs;
using Ballware.Shared.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.AspNetCore;

namespace Ballware.Ml.Jobs;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareMlBackgroundJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            var importJobName = "import";
            
            q.AddJob<TenantableImportJob<Data.Public.MlModel, ITenantableRepository<Data.Public.MlModel>>>(new JobKey(importJobName, "mlmodel"), configurator => configurator.StoreDurably());
            q.AddJob<ModelTrainJob>(ModelTrainJob.Key, configurator => configurator.StoreDurably());
        });

        services.AddQuartzServer(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}