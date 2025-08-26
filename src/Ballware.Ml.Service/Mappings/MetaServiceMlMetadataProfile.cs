using AutoMapper;
using Ballware.Ml.Metadata;

namespace Ballware.Ml.Service.Mappings;

public class MetaServiceMlMetadataProfile : Profile
{
    public MetaServiceMlMetadataProfile()
    {
        CreateMap<Ballware.Meta.Service.Client.ServiceEntity, EntityMetadata>()
            .ForMember(dst => dst.RightsCheckScript, 
                opt => opt.MapFrom(source => source.CustomScripts.ExtendedRightsCheck));
        
        CreateMap<Ballware.Meta.Service.Client.ServiceTenant, Tenant>();
     
        CreateMap<Ballware.Shared.Api.Public.JobStates, Ballware.Meta.Service.Client.JobStates>();
        CreateMap<Ballware.Meta.Service.Client.JobStates, Ballware.Shared.Api.Public.JobStates>();
    }
}