namespace Ballware.Ml.Engine.AutoMl;

public interface IAutoMlFileStorageAdapter
{
    Task<Stream> AttachmentFileByNameForOwnerAsync(Guid tenantId, string entity, Guid ownerId, string fileName);
    Task UploadAttachmentFileForOwnerAsync(Guid tenantId, Guid userId, string entity, Guid ownerId, string fileName, string contentType, Stream data);
}