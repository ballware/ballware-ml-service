using Ballware.Ml.Engine.AutoMl;
using Ballware.Storage.Client;

namespace Ballware.Ml.Service.Adapter;

public class StorageServiceFileStorageAdapter : IAutoMlFileStorageAdapter
{
    private BallwareStorageClient Client { get; }
    
    public StorageServiceFileStorageAdapter(BallwareStorageClient storageClient)
    {
        Client = storageClient;
    }
    
    public async Task<Stream> FileByNameForOwnerAsync(string owner, string fileName)
    {
        var result = await Client.FileByNameForOwnerAsync(owner, fileName);
        
        return result.Stream;
    }

    public async Task UploadFileForOwnerAsync(string owner, string fileName, string contentType, Stream data)
    {
        await Client.UploadFileForOwnerAsync(owner, new []{ new FileParameter(data, fileName, contentType) });
    }
}