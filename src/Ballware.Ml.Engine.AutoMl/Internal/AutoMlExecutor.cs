using System.Collections;
using System.Text;
using Ballware.Ml.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.ML;
using Microsoft.ML.AutoML;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballware.Ml.Engine.AutoMl.Internal;

public class AutoMlExecutor : IModelExecutor
{
    private ILogger<AutoMlExecutor> Logger { get; }
    private IMetadataAdapter MetadataAdapter { get; }
    private ITenantDataAdapter TenantDataAdapter { get; }
    private IAutoMlFileStorageAdapter StorageAdapter { get; }
    
    public AutoMlExecutor(ILogger<AutoMlExecutor> logger, IMetadataAdapter metadataAdapter, ITenantDataAdapter tenantDataAdapter, IAutoMlFileStorageAdapter storageAdapter)
    {
        Logger = logger;
        MetadataAdapter = metadataAdapter;
        TenantDataAdapter = tenantDataAdapter;
        StorageAdapter = storageAdapter;
    }
    
    public async Task TrainAsync(Guid tenantId, Guid modelId, Guid userId)
    {
        var logBuilder = new StringBuilder();

        await MetadataAdapter.MlModelUpdateTrainingStateBehalfOfUserAsync(tenantId, userId,
            new UpdateMlModelTrainingStatePayload()
            {
                Id = modelId,
                State = MlModelTrainingStates.InProgress,
                Result = string.Empty,
            });
        
        MLContext mlContext = new MLContext();
        
        mlContext.Log += (_, e) => {
            if (e.Source.Equals("AutoMLExperiment"))
            {
                logBuilder.AppendLine(e.RawMessage);
            }
        };

        var modelMetadata = await MetadataAdapter.MlModelMetadataByTenantAndIdAsync(tenantId, modelId);
        
        var modelOptions = JsonConvert.DeserializeObject<ModelOptions>(modelMetadata.Options ?? "{}");
        
        var inputType = new AutoMlModelBuilder().CompileInputType(tenantId, modelMetadata);
        var dataviewSchema = new AutoMlModelBuilder().BuildDataViewSchema(modelMetadata);
        var columnInformation = new AutoMlModelBuilder().BuildColumnInformation(modelMetadata);
        
        var runtimeTrainingDataListType = typeof(List<>).MakeGenericType(inputType);
        IList runtimeTrainingDataList = (IList)Activator.CreateInstance(runtimeTrainingDataListType);
        
        var objectConverter = typeof(JObject)
            .GetMethods().Where(m => m.Name == "ToObject" && m.GetParameters().Length == 0).First()
            .MakeGenericMethod(inputType);
        
        var trainingData = (await TenantDataAdapter.MlModelTrainingdataByTenantAndIdAsync(tenantId, modelId)).ToList();

        foreach (var td in trainingData)
        {
            runtimeTrainingDataList.Add(objectConverter.Invoke(td, null));
        }
        
        var dataView = typeof(DataOperationsCatalog)
            .GetMethods().Where(m => m.Name == "LoadFromEnumerable" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(DataViewSchema)).First()
            .MakeGenericMethod(inputType)
            .Invoke(mlContext.Data, new object[] { runtimeTrainingDataList, dataviewSchema }) as IDataView;
        
        var pipeline = mlContext.Auto().Featurizer(dataView, columnInformation)
            .Append(mlContext.Auto().Regression(labelColumnName: columnInformation.LabelColumnName));
        
        var experiment = mlContext.Auto().CreateExperiment();

        experiment.SetPipeline(pipeline).SetRegressionMetric(RegressionMetric.RSquared,
                labelColumn: columnInformation.LabelColumnName)
            .SetTrainingTimeInSeconds(modelOptions.TrainingTime > 0 ? modelOptions.TrainingTime : 60)
            .SetDataset(dataView);

        try
        {
            var result = await experiment.RunAsync();

            using (var stream = new MemoryStream())
            {
                mlContext.Model.Save(result.Model, dataView.Schema, stream);

                stream.Position = 0;

                await StorageAdapter.UploadFileForOwnerAsync(modelId.ToString(),
                    !string.IsNullOrEmpty(modelOptions.TrainingFileName)
                        ? modelOptions.TrainingFileName
                        : "model.zip", "application/binary", stream);
            }
            
            await MetadataAdapter.MlModelUpdateTrainingStateBehalfOfUserAsync(tenantId, userId, new UpdateMlModelTrainingStatePayload()
            {
                Id = modelId,
                State = MlModelTrainingStates.UpToDate,
                Result = JsonConvert.SerializeObject(new
                {
                    TrainingRecordCount = trainingData.Count,
                    result.Loss,
                    result.Metric,
                    result.DurationInMilliseconds,
                    result.PeakCpu,
                    result.PeakMemoryInMegaByte
                })
            });
        }
        catch (Exception ex)
        {
            await MetadataAdapter.MlModelUpdateTrainingStateBehalfOfUserAsync(tenantId, userId, new UpdateMlModelTrainingStatePayload()
            {
                Id = modelId,
                State = MlModelTrainingStates.Error,
                Result = JsonConvert.SerializeObject(new
                {
                    TrainingRecordCount = trainingData.Count,
                    Exception = ex.Message
                })
            });
        }
        
        if (!string.IsNullOrEmpty(modelOptions.TrainingLogFileName))
        {
            using var logStream = new MemoryStream();
            await logStream.WriteAsync(Encoding.UTF8.GetBytes(logBuilder.ToString()));
            logStream.Position = 0;

            await StorageAdapter.UploadFileForOwnerAsync(modelId.ToString(), modelOptions.TrainingLogFileName,
                "application/binary", logStream);
        }
    }
    
    public async Task<object> PredictAsync(Guid tenantId, Guid modelId, IDictionary<string, object> query)
    {   
        var modelMetadata = await MetadataAdapter.MlModelMetadataByTenantAndIdAsync(tenantId, modelId);
        
        var prediction = await CreatePredictionEngine(tenantId, modelMetadata);
        
        return Predict(prediction, query);
    }
    
    public async Task<object> PredictAsync(Guid tenantId, string identifier, IDictionary<string, object> query)
    {
        var modelMetadata = await MetadataAdapter.MlModelMetadataByTenantAndIdentifierAsync(tenantId, identifier);

        var prediction = await CreatePredictionEngine(tenantId, modelMetadata);
        
        return Predict(prediction, query);
    }
    
    private async Task<AutoMlPredictionContext> CreatePredictionEngine(Guid tenantId, ModelMetadata modelMetadata)
    {
        var modelOptions = JsonConvert.DeserializeObject<ModelOptions>(modelMetadata.Options ?? "{}");
        
        var modelData = await StorageAdapter.FileByNameForOwnerAsync(modelMetadata.Id.ToString(), !string.IsNullOrEmpty(modelOptions.TrainingFileName) ? modelOptions.TrainingFileName : "model.zip");

        var inputType = new AutoMlModelBuilder().CompileInputType(tenantId, modelMetadata);
        var outputType = new AutoMlModelBuilder().CompileOutputType(tenantId, modelMetadata);
        
        MLContext mlContext = new MLContext();

        var modelTransformer = mlContext.Model.Load(modelData, out DataViewSchema modelSchema);

        var predictionEngine = typeof(ModelOperationsCatalog)
            .GetMethods().Where(m =>
                m.Name == "CreatePredictionEngine" && m.IsGenericMethod && m.GetParameters().Length == 4)
            .First()
            .MakeGenericMethod(new[] { inputType, outputType })
            .Invoke(mlContext.Model, new object[] { modelTransformer, true, null, null });

        var prediction = new AutoMlPredictionContext
        {
            Metadata = modelMetadata,
            Context = mlContext,
            PredictionEngine = predictionEngine,
            InputType = inputType,
            OutputType = outputType
        };
        
        return prediction;
    }
    
    public object Predict(AutoMlPredictionContext prediction, IDictionary<string, object> query)
    {
        var inputInstance = new AutoMlModelBuilder().CreateInput(prediction.InputType, prediction.Metadata, query);
            
        var outputInstance = typeof(PredictionEngine<,>)
            .MakeGenericType(new[] { prediction.InputType, prediction.OutputType })
            .GetMethods().Where(m => m.Name == "Predict" && m.GetParameters().Length == 1)
            .First()
            .Invoke(prediction.PredictionEngine, new[] { inputInstance });

        return outputInstance;
    }
}