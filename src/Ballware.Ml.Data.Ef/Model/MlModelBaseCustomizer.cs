using Ballware.Ml.Data.Persistables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ballware.ML.Data.Ef.Model;

public class MlModelBaseCustomizer : RelationalModelCustomizer
{
    public MlModelBaseCustomizer(ModelCustomizerDependencies dependencies) 
        : base(dependencies)
    {
    }
    
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                }

                if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(new ValueConverter<DateTime?, DateTime?>(
                        v => v.HasValue ? v.Value.ToUniversalTime() : v,
                        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v));
                }
            }
        }
        
        modelBuilder.Entity<MlModel>().ToTable("ml_model");
        modelBuilder.Entity<MlModel>().HasKey(d => d.Id);
        modelBuilder.Entity<MlModel>().HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
        modelBuilder.Entity<MlModel>().HasIndex(d => new { d.TenantId });
        modelBuilder.Entity<MlModel>().HasIndex(d => new { d.TenantId, d.Identifier }).IsUnique();
    }
}