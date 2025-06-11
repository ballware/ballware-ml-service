using Ballware.Generic.Client;
using Ballware.Ml.Metadata;

namespace Ballware.Ml.Service.Adapter;

public class GenericServiceTenantDataAdapter : ITenantDataAdapter
{
    private BallwareGenericClient Client { get; }
    
    public GenericServiceTenantDataAdapter(BallwareGenericClient genericClient)
    {
        Client = genericClient;
    }
    
    public async Task<IEnumerable<object>> MlModelTrainingdataByTenantAndIdAsync(Guid tenantId, Guid modelId)
    {
        return await Client.MlModelTrainingDataByTenantAndIdAsync(tenantId, modelId);
    }
}