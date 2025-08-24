using Ballware.Ml.Engine.AutoMl;
using Ballware.Storage.Service.Client;

namespace Ballware.Ml.Service.Adapter;

public class StorageServiceFileStorageAdapter : IAutoMlFileStorageAdapter
{
    private StorageServiceClient Client { get; }
    
    public StorageServiceFileStorageAdapter(StorageServiceClient storageClient)
    {
        Client = storageClient;
    }
    
    public async Task<Stream> AttachmentFileByNameForOwnerAsync(Guid tenantId, string entity, Guid ownerId, string fileName)
    {
        var result =
            await Client.AttachmentDownloadForTenantEntityAndOwnerByFilenameAsync(tenantId, entity, ownerId, fileName);
        
        return result.Stream;
    }

    public async Task UploadAttachmentFileForOwnerAsync(Guid tenantId, Guid userId, string entity, Guid ownerId, string fileName, string contentType, Stream data)
    {
        await Client.AttachmentUploadForTenantEntityAndOwnerBehalfOfUserAsync(tenantId, userId, entity, ownerId, [new FileParameter(data, fileName, contentType)]);
    }
}