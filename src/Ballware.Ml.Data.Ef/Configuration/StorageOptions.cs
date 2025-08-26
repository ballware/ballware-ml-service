using System.ComponentModel.DataAnnotations;

namespace Ballware.Ml.Data.Ef.Configuration;

public sealed class StorageOptions
{
    [Required]
    public required string Provider { get; set; }
    
    [Required]
    public required string ConnectionString { get; set; }
    
    public bool AutoMigrations { get; set; } = false;
}