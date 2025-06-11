using AutoMapper;

namespace Ballware.Ml.Service.Mappings;

public class MetaServiceMlMetadataProfile : Profile
{
    public MetaServiceMlMetadataProfile()
    {
        CreateMap<Ballware.Meta.Client.MlModel, Ballware.Ml.Metadata.ModelMetadata>();
        CreateMap<Ballware.Ml.Metadata.UpdateMlModelTrainingStatePayload, Ballware.Meta.Client.MlModelTrainingState>();
    }
}