using Ballware.Ml.Jobs.Internal;
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
            q.AddJob<ModelTrainJob>(ModelTrainJob.Key, configurator => configurator.StoreDurably());
        });

        services.AddQuartzServer(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}