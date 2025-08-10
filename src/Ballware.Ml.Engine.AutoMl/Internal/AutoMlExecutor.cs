using System.Collections;
using System.Text;
using Ballware.Ml.Caching;
using Ballware.Ml.Metadata;
using Microsoft.Extensions.Logging;
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
    private ITenantAwareModelCache? Cache { get; }
    
    public AutoMlExecutor(ILogger<AutoMlExecutor> logger, IMetadataAdapter metadataAdapter, ITenantDataAdapter tenantDataAdapter, IAutoMlFileStorageAdapter storageAdapter, ITenantAwareModelCache? cache)
    {
        Logger = logger;
        MetadataAdapter = metadataAdapter;
        TenantDataAdapter = tenantDataAdapter;
        StorageAdapter = storageAdapter;
        Cache = cache;
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
            .GetMethods()
            .First(m => m.Name == "ToObject" && m.GetParameters().Length == 0)
            .MakeGenericMethod(inputType);
        
        var trainingData = (await TenantDataAdapter.MlModelTrainingdataByTenantAndIdAsync(tenantId, modelId)).ToList();

        foreach (var td in trainingData)
        {
            runtimeTrainingDataList.Add(objectConverter.Invoke(td, null));
        }
        
        var dataView = typeof(DataOperationsCatalog)
            .GetMethods()
            .First(m => m.Name == "LoadFromEnumerable" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(DataViewSchema))
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

                await StorageAdapter.UploadAttachmentFileForOwnerAsync(tenantId, userId, "mlmodel", modelId,
                    !string.IsNullOrEmpty(modelOptions.TrainingFileName)
                        ? modelOptions.TrainingFileName
                        : "model.zip", "application/binary", stream);
            }
            
            Cache?.PurgeItem(tenantId, modelMetadata.Identifier);
            
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

            await StorageAdapter.UploadAttachmentFileForOwnerAsync(tenantId, userId, "mlmodel", modelId,
                modelOptions.TrainingLogFileName, "application/binary", logStream);
        }
    }
    
    public async Task<object> PredictAsync(Guid tenantId, Guid modelId, IDictionary<string, object> query)
    {   
        var modelMetadata = await MetadataAdapter.MlModelMetadataByTenantAndIdAsync(tenantId, modelId);

        if (modelMetadata == null)
        {
            throw new ArgumentException($"Model with ID {modelId} not found for tenant {tenantId}");
        }
        
        AutoMlPredictionContext? prediction = null;
        
        Cache?.TryGetItem(tenantId, modelMetadata.Identifier, out prediction);

        if (prediction == null)
        {
            prediction = await CreatePredictionEngine(tenantId, modelMetadata);
            Cache?.SetItem(tenantId, modelMetadata.Identifier, prediction);
        }
        
        return Predict(prediction, query);
    }
    
    public async Task<object> PredictAsync(Guid tenantId, string identifier, IDictionary<string, object> query)
    {
        var modelMetadata = await MetadataAdapter.MlModelMetadataByTenantAndIdentifierAsync(tenantId, identifier);

        if (modelMetadata == null)
        {
            throw new ArgumentException($"Model with identifier {identifier} not found for tenant {tenantId}");
        }
        
        AutoMlPredictionContext? prediction = null;
        
        Cache?.TryGetItem(tenantId, modelMetadata.Identifier, out prediction);

        if (prediction == null)
        {
            prediction = await CreatePredictionEngine(tenantId, modelMetadata);
            Cache?.SetItem(tenantId, modelMetadata.Identifier, prediction);
        }
        
        return Predict(prediction, query);
    }
    
    private async Task<AutoMlPredictionContext> CreatePredictionEngine(Guid tenantId, ModelMetadata modelMetadata)
    {
        var modelData =
            await StorageAdapter.AttachmentFileByNameForOwnerAsync(tenantId, "mlmodel", modelMetadata.Id, "model.zip");
        
        var inputType = new AutoMlModelBuilder().CompileInputType(tenantId, modelMetadata);
        var outputType = new AutoMlModelBuilder().CompileOutputType(tenantId, modelMetadata);
        
        MLContext mlContext = new MLContext();

        var modelTransformer = mlContext.Model.Load(modelData, out DataViewSchema _);

        var predictionEngine = typeof(ModelOperationsCatalog)
            .GetMethods()
            .First(m =>
                m.Name == "CreatePredictionEngine" && m.IsGenericMethod && m.GetParameters().Length == 4)
            .MakeGenericMethod(inputType, outputType)
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
    
    private static object Predict(AutoMlPredictionContext prediction, IDictionary<string, object> query)
    {
        lock (prediction)
        {
            var inputInstance = new AutoMlModelBuilder().CreateInput(prediction.InputType, prediction.Metadata, query);
            
            var outputInstance = typeof(PredictionEngine<,>)
                .MakeGenericType(prediction.InputType, prediction.OutputType)
                .GetMethods()
                .First(m => m.Name == "Predict" && m.GetParameters().Length == 1)
                .Invoke(prediction.PredictionEngine, new[] { inputInstance });

            return outputInstance;    
        }
    }
}