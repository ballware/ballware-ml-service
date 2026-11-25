using Mapster;

namespace Ballware.Ml.Data.Ef.Mapping;

class StorageMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Public.MlModel, Persistables.MlModel>()
            .Ignore(dest => dest.Id)
            .Map(dest => dest.Uuid, src => src.Id);

        config.NewConfig<Persistables.MlModel, Public.MlModel>()
            .Map(dest => dest.Id, src => src.Uuid);
    }
}
