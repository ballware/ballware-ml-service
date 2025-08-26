namespace Ballware.Ml.Data;

public interface IMlDbConnectionFactory
{
    string Provider { get; }
    string ConnectionString { get; }
}