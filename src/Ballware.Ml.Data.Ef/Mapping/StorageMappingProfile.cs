using AutoMapper;

namespace Ballware.Ml.Data.Ef.Mapping;

class StorageMappingProfile : Profile
{
    public StorageMappingProfile()
    {
        CreateMap<Public.MlModel, Persistables.MlModel>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Uuid, opt => opt.MapFrom(src => src.Id));

        CreateMap<Persistables.MlModel, Public.MlModel>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Uuid));
    }
}