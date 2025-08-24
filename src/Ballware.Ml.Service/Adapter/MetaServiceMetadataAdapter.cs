using AutoMapper;
using Ballware.Meta.Service.Client;
using Ballware.Ml.Metadata;

namespace Ballware.Ml.Service.Adapter;

public class MetaServiceMetadataAdapter : IMetadataAdapter
{
    private IMapper Mapper { get; }
    private MetaServiceClient Client { get; }
    
    public MetaServiceMetadataAdapter(IMapper mapper, MetaServiceClient metaClient)
    {
        Mapper = mapper;
        Client = metaClient;
    }
    
    public async Task<ModelMetadata?> MlModelMetadataByTenantAndIdAsync(Guid tenantId, Guid modelId)
    {
        return Mapper.Map<ModelMetadata?>(await Client.MlModelMetadataByTenantAndIdAsync(tenantId, modelId));
    }

    public async Task<ModelMetadata?> MlModelMetadataByTenantAndIdentifierAsync(Guid tenantId, string identifier)
    {
        return Mapper.Map<ModelMetadata?>(await Client.MlModelMetadataByTenantAndIdentifierAsync(tenantId, identifier));
    }

    public async Task MlModelUpdateTrainingStateBehalfOfUserAsync(Guid tenantId, Guid userId,
        UpdateMlModelTrainingStatePayload payload)
    {
        await Client.MlModelSaveTrainingStateBehalfOfUserAsync(tenantId, userId, Mapper.Map<MlModelTrainingState>(payload));
    }
}