namespace Ballware.Ml.Engine.AutoMl;

public interface IAutoMlFileStorageAdapter
{
    Task<Stream> FileByNameForOwnerAsync(string owner, string fileName);
    Task UploadFileForOwnerAsync(string owner, string fileName, string contentType, Stream data);
}