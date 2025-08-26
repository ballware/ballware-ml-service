using Ballware.Ml.Engine.AutoMl;
using Ballware.Shared.Api;
using Ballware.Storage.Service.Client;

namespace Ballware.Ml.Service.Adapter;

public class StorageServiceFileStorageAdapter : IAutoMlFileStorageAdapter, IFileStorageProvider
{
    private StorageServiceClient StorageClient { get; }
    
    public StorageServiceFileStorageAdapter(StorageServiceClient storageClient)
    {
        StorageClient = storageClient;
    }
    
    public async Task<Stream> AttachmentFileByNameForOwnerAsync(Guid tenantId, string entity, Guid ownerId, string fileName)
    {
        var result =
            await StorageClient.AttachmentDownloadForTenantEntityAndOwnerByFilenameAsync(tenantId, entity, ownerId, fileName);
        
        return result.Stream;
    }

    public async Task UploadAttachmentFileForOwnerAsync(Guid tenantId, Guid userId, string entity, Guid ownerId, string fileName, string contentType, Stream data)
    {
        await StorageClient.AttachmentUploadForTenantEntityAndOwnerBehalfOfUserAsync(tenantId, userId, entity, ownerId, [new FileParameter(data, fileName, contentType)]);
    }
    
    public async Task<Stream> TemporaryFileByIdAsync(Guid tenantId, Guid temporaryId)
    {
        return (await StorageClient.TemporaryDownloadForTenantByIdAsync(tenantId, temporaryId)).Stream;
    }

    public async Task UploadTemporaryFileBehalfOfUserAsync(Guid tenantId, Guid userId, Guid temporaryId, string fileName,
        string contentType, Stream data)
    {
        await StorageClient.TemporaryUploadForTenantAndIdBehalfOfUserAsync(tenantId, userId, temporaryId, [
            new FileParameter(data, fileName, contentType)
        ]);
    }

    public async Task RemoveTemporaryFileByIdBehalfOfUserAsync(Guid tenantId, Guid userId, Guid temporaryId)
    {
        await StorageClient.TemporaryDropForTenantAndIdBehalfOfUserAsync(tenantId, userId, temporaryId);
    }
}