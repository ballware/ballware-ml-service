namespace Ballware.Ml.Metadata;

public interface ITenantDataAdapter
{
    Task<IEnumerable<object>> MlModelTrainingdataByTenantAndIdAsync(Guid tenantId, Guid modelId);
}