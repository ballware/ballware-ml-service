using Microsoft.Extensions.Primitives;

namespace Ballware.Ml.Engine;

public interface IModelExecutor
{
    Task TrainAsync(Guid tenantId, Guid modelId, Guid userId);
    Task<object> PredictAsync(Guid tenantId, Guid modelId, IDictionary<string, object> query);
    Task<object> PredictAsync(Guid tenantId, string identifier, IDictionary<string, object> query);
}