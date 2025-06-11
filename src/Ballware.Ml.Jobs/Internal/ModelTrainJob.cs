using Ballware.Ml.Engine;
using Quartz;

namespace Ballware.Ml.Jobs.Internal;

public class ModelTrainJob : IJob
{
    public static readonly JobKey Key = new JobKey("train", "ml");

    private IModelExecutor ModelExecutor { get; }
    
    public ModelTrainJob(IModelExecutor modelExecutor)
    {
        ModelExecutor = modelExecutor;
    }
    
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {   
            var tenantId = context.MergedJobDataMap.GetGuidValue("tenantId");
            var userId = context.MergedJobDataMap.GetGuidValue("userId");
            var modelId = context.MergedJobDataMap.GetGuidValue("modelId");

            await ModelExecutor.TrainAsync(tenantId, modelId, userId);
        }
        catch (Exception ex)
        {
            // do you want the job to refire?
            throw new JobExecutionException(msg: "", refireImmediately: false, cause: ex);
        }
    }
}