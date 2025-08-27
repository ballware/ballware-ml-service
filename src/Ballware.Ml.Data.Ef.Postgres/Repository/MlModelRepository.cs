using AutoMapper;
using Ballware.Ml.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;

namespace Ballware.Ml.Data.Ef.Postgres.Repository;

public class MlModelRepository : MlModelBaseRepository
{
    public MlModelRepository(IMapper mapper, IMlDbContext dbContext,
        ITenantableRepositoryHook<Public.MlModel, Persistables.MlModel>? hook = null)
        : base(mapper, dbContext, hook)
    {
    }

    public override Task<string> GenerateListQueryAsync(Guid tenantId)
    {
        return Task.FromResult($"SELECT uuid AS \"Id\", identifier as \"Identifier\" FROM ml_model WHERE tenant_id='{tenantId}'");
    }
}
