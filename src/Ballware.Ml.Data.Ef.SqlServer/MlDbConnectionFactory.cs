namespace Ballware.Ml.Data.Ef.SqlServer;

public class MlDbConnectionFactory : IMlDbConnectionFactory
{
    public string Provider { get; }
    public string ConnectionString { get; }

    public MlDbConnectionFactory(string connectionString)
    {
        Provider = "mssql";
        ConnectionString = connectionString;
    }
}