using Ballware.Ml.Metadata;
using Mapster;

namespace Ballware.Ml.Service.Mappings;

public class MetaServiceMlMetadataProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Ballware.Meta.Service.Client.ServiceEntity, EntityMetadata>()
            .Map(dest => dest.RightsCheckScript, src => src.CustomScripts.ExtendedRightsCheck);

        config.NewConfig<Ballware.Meta.Service.Client.ServiceTenant, Tenant>();

        config.NewConfig<Ballware.Shared.Api.Public.JobStates, Ballware.Meta.Service.Client.JobStates>();
        config.NewConfig<Ballware.Meta.Service.Client.JobStates, Ballware.Shared.Api.Public.JobStates>();
    }
}
