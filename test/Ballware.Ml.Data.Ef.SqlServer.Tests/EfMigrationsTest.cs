using Ballware.Ml.Data.Ef.Configuration;
using Ballware.Ml.Data.Ef.SqlServer;
using Ballware.Ml.Data.Ef.SqlServer.Tests.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Ml.Data.Ef.SqlServer.Tests;

[TestFixture]
public class EfMigrationsTest : DatabaseBackedBaseTest
{
    [Test]
    public async Task Initialization_with_migrations_up_succeeds()
    {
        var storageOptions = PreparedBuilder.Configuration.GetSection("Storage").Get<StorageOptions>();
        var connectionString = MasterConnectionString;

        Assert.Multiple(() =>
        {
            Assert.That(storageOptions, Is.Not.Null);
            Assert.That(connectionString, Is.Not.Null);
        });

        PreparedBuilder.Services.AddBallwareMlStorageForSqlServer(storageOptions, connectionString);
        PreparedBuilder.Services.AddAutoMapper(config =>
        {
            config.AddBallwareMlStorageMappings();
        });

        var app = PreparedBuilder.Build();

        await app.StartAsync();
        await app.StopAsync();
    }
}