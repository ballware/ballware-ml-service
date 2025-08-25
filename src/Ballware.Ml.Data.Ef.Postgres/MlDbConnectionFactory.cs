namespace Ballware.Ml.Data.Ef.Postgres;

public class MlDbConnectionFactory : IMlDbConnectionFactory
{
    public string Provider { get; }
    public string ConnectionString { get; }

    public MlDbConnectionFactory(string connectionString)
    {
        Provider = "postgres";
        ConnectionString = connectionString;
    }
}
