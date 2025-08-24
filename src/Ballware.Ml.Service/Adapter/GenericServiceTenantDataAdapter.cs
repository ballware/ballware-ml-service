using Ballware.Generic.Service.Client;
using Ballware.Ml.Metadata;

namespace Ballware.Ml.Service.Adapter;

public class GenericServiceTenantDataAdapter : ITenantDataAdapter
{
    private GenericServiceClient Client { get; }
    
    public GenericServiceTenantDataAdapter(GenericServiceClient genericClient)
    {
        Client = genericClient;
    }
    
    public async Task<IEnumerable<object>> MlModelTrainingdataByTenantAndIdAsync(Guid tenantId, Guid modelId)
    {
        return await Client.MlModelTrainingDataByTenantAndIdAsync(tenantId, modelId);
    }
}