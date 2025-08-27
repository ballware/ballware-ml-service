namespace Ballware.Ml.Metadata;

public interface ITenantDataAdapter
{
    Task<IEnumerable<object>> MlModelTrainingdataByTenantAndPlainQueryAsync(Guid tenantId, string query);
}