using AutoMapper;
using Ballware.Ml.Data.Common;
using Ballware.Ml.Data.Repository;
using Ballware.Ml.Data.SelectLists;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;
using Microsoft.EntityFrameworkCore;

namespace Ballware.Ml.Data.Ef.Repository;

public abstract class MlModelBaseRepository : TenantableBaseRepository<Public.MlModel, Persistables.MlModel>, IMlModelMetaRepository
{
    private IMlDbContext MetaContext { get; }

    public MlModelBaseRepository(IMapper mapper, IMlDbContext dbContext,
        ITenantableRepositoryHook<Public.MlModel, Persistables.MlModel>? hook = null)
        : base(mapper, dbContext, hook)
    {
        MetaContext = dbContext;
    }

    public virtual async Task<Public.MlModel?> MetadataByTenantAndIdAsync(Guid tenantId, Guid id)
    {
        var result = await MetaContext.MlModels.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Uuid == id);

        return result != null ? Mapper.Map<Public.MlModel>(result) : null;
    }

    public virtual async Task<Public.MlModel?> MetadataByTenantAndIdentifierAsync(Guid tenantId, string identifier)
    {

        var result = await MetaContext.MlModels.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Identifier == identifier);

        return result != null ? Mapper.Map<Public.MlModel>(result) : null;
    }

    public virtual async Task SaveTrainingStateAsync(Guid tenantId, Guid userId, MlModelTrainingState state)
    {
        var model = await MetaContext.MlModels.SingleOrDefaultAsync(j => j.TenantId == tenantId && j.Uuid == state.Id);

        if (model == null)
        {
            throw new Exception("Model not found");
        }

        model.TrainState = state.State;
        model.TrainResult = state.Result;
        model.LastChangerId = userId;
        model.LastChangeStamp = DateTime.Now;

        MetaContext.MlModels.Update(model);

        await Context.SaveChangesAsync();
    }
    
    public virtual async Task<IEnumerable<MlModelSelectListEntry>> SelectListForTenantAsync(Guid tenantId)
    {
        return await Task.FromResult(MetaContext.MlModels.Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Identifier)
            .Select(r => new MlModelSelectListEntry
                { Id = r.Uuid, Identifier = r.Identifier }));
    }
    
    public virtual async Task<MlModelSelectListEntry?> SelectByIdForTenantAsync(Guid tenantId, Guid id)
    {
        return await MetaContext.MlModels.Where(r => r.TenantId == tenantId && r.Uuid == id)
            .Select(r => new MlModelSelectListEntry
                { Id = r.Uuid, Identifier = r.Identifier })
            .FirstOrDefaultAsync();
    }

    public abstract Task<string> GenerateListQueryAsync(Guid tenantId);
}