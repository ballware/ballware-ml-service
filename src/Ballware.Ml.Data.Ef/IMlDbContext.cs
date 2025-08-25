using Ballware.Ml.Data.Persistables;
using Ballware.Shared.Data.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ballware.Ml.Data.Ef;

public interface IMlDbContext : IDbContext
{
    DbSet<MlModel> MlModels { get; }
}