using AutoMapper;
using Ballware.Ml.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;

namespace Ballware.Ml.Data.Ef.SqlServer.Repository;

public class MlModelRepository : MlModelBaseRepository
{
    public MlModelRepository(IMapper mapper, IMlDbContext dbContext,
        ITenantableRepositoryHook<Public.MlModel, Persistables.MlModel>? hook = null)
        : base(mapper, dbContext, hook)
    {
    }

    public override Task<string> GenerateListQueryAsync(Guid tenantId)
    {
        return Task.FromResult($"select uuid as Id, identifier as Identifier from ml_model where tenant_id='{tenantId}'");
    }
}