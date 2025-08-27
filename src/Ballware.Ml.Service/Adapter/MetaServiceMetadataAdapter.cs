using AutoMapper;
using Ballware.Meta.Service.Client;
using Ballware.Ml.Metadata;
using Ballware.Shared.Api;
using Ballware.Shared.Authorization;

namespace Ballware.Ml.Service.Adapter;

public class MetaServiceMetadataAdapter : IAuthorizationMetadataProvider, IExportMetadataProvider, IJobMetadataProvider
{
    private IMapper Mapper { get; }
    private MetaServiceClient MetaClient { get; }
    
    public MetaServiceMetadataAdapter(IMapper mapper, MetaServiceClient metaClient)
    {
        Mapper = mapper;
        MetaClient = metaClient;
    }
    
    public async Task<ITenantAuthorizationMetadata?> MetadataForTenantByIdAsync(Guid tenantId)
    {
        var tenant = await MetaClient.TenantServiceMetadataAsync(tenantId);
        
        return Mapper.Map<Tenant>(tenant);   
    }

    public async Task<IEntityAuthorizationMetadata?> MetadataForEntityByTenantAndIdentifierAsync(Guid tenantId, string entity)
    {
        var entityMetadata = await MetaClient.EntityServiceMetadataForTenantByIdentifierAsync(tenantId, entity);
        
        return Mapper.Map<EntityMetadata>(entityMetadata);  
    }
    
    public async Task<Guid> CreateExportAsync(Guid tenantId, Guid userId, Shared.Api.Public.Export payload)
    {
        return await MetaClient.ExportCreateForTenantBehalfOfUserAsync(tenantId, userId, new ExportCreatePayload()
        {
            Application = payload.Application,
            Entity = payload.Entity,
            Query = payload.Query,
            ExpirationStamp = payload.ExpirationStamp,
            MediaType = payload.MediaType
        });
    }

    public async Task<Shared.Api.Public.Export?> GetExportByIdAsync(Guid tenantId, Guid exportId)
    {
        var export = await MetaClient.ExportFetchForTenantByIdAsync(tenantId, exportId);

        return new Shared.Api.Public.Export()
        {
            Id = export.Id,
            Application = export.Application,
            Entity = export.Entity,
            Query = export.Query,
            ExpirationStamp = export.ExpirationStamp?.DateTime,
            MediaType = export.MediaType,
        };
    }
    
    public async Task<Guid> CreateJobAsync(Guid tenantId, Guid userId, string scheduler, string identifier, string? options)
    {
        var job = await MetaClient.JobCreateForTenantBehalfOfUserAsync(tenantId, userId, new JobCreatePayload()
        {
            Scheduler = scheduler,
            Identifier = identifier,
            Options = options,
        });

        if (job == null)
        {
            throw new InvalidOperationException($"Failed to create job for tenant {tenantId}.");
        }
        
        return job.Id;
    }

    public async Task UpdateJobAsync(Guid tenantId, Guid userId, Guid id, Ballware.Shared.Api.Public.JobStates state, string? result)
    {
        await MetaClient.JobUpdateForTenantBehalfOfUserAsync(tenantId, userId, new JobUpdatePayload()
        {
            Id = id,
            State = Mapper.Map<Ballware.Meta.Service.Client.JobStates>(state),
            Result = result
        });
    }
}