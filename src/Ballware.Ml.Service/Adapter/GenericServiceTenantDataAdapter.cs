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
    
    public async Task<IEnumerable<object>> MlModelTrainingdataByTenantAndPlainQueryAsync(Guid tenantId, string query)
    {
        return await Client.MlModelTrainingDataByTenantAndPlainQueryAsync(tenantId, query);
    }
}