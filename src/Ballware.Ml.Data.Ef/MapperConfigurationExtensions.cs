using Ballware.Ml.Data.Ef.Mapping;
using Mapster;

namespace Ballware.Ml.Data.Ef;

public static class MapperConfigurationExtensions
{
    public static TypeAdapterConfig AddBallwareMlStorageMappings(
        this TypeAdapterConfig configuration)
    {
        new StorageMappingProfile().Register(configuration);

        return configuration;
    }
}
