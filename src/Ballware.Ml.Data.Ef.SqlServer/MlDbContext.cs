using Ballware.Ml.Data.Persistables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ballware.Ml.Data.Ef.SqlServer;

public class MlDbContext : DbContext, IMlDbContext
{
    private ILoggerFactory LoggerFactory { get; }
    
    public MlDbContext(DbContextOptions<MlDbContext> options, ILoggerFactory loggerFactory) : base(options)
    {
        LoggerFactory = loggerFactory;
    }

    public DbSet<MlModel> MlModels { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseLoggerFactory(LoggerFactory);
        
        base.OnConfiguring(optionsBuilder);
    }
}