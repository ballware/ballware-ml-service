namespace Ballware.Ml.Metadata;

public interface IMetadataAdapter
{
    Task<ModelMetadata?> MlModelMetadataByTenantAndIdAsync(Guid tenantId, Guid modelId);
    Task<ModelMetadata?> MlModelMetadataByTenantAndIdentifierAsync(Guid tenantId, string identifier);
    Task MlModelUpdateTrainingStateBehalfOfUserAsync(Guid tenantId, Guid userId, UpdateMlModelTrainingStatePayload payload);
}