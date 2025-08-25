using System.Collections.Immutable;
using System.Text;
using Ballware.Ml.Data.Common;
using Ballware.Ml.Data.Public;
using Ballware.Ml.Data.Repository;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace Ballware.Ml.Data.Ef.Postgres.Tests.Repository;

public class MlModelMetaRepositoryTest : RepositoryBaseTest
{
    [Test]
    public async Task Save_and_remove_value_succeeds()
    {
        using var scope = Application.Services.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMlModelMetaRepository>();

        var expectedValue = await repository.NewQueryAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);

        expectedValue.Identifier = $"fake_identifier_1";
        expectedValue.Type = MlModelTypes.Regression;
        expectedValue.Options = "{}";
        expectedValue.TrainResult = "fake train result";
        expectedValue.TrainSql = "fake train sql";
        expectedValue.TrainState = MlModelTrainingStates.UpToDate;
        
        await repository.SaveAsync(TenantId, null, "primary", ImmutableDictionary<string, object>.Empty, expectedValue);

        var actualValue = await repository.ByIdAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);
        var actualById = await repository.MetadataByTenantAndIdAsync(TenantId, expectedValue.Id);
        var actualByIdentifier = await repository.MetadataByTenantAndIdentifierAsync(TenantId, expectedValue.Identifier);

        Assert.Multiple(() =>
        {
            Assert.That(actualValue, Is.Not.Null);
            Assert.That(actualValue?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualValue?.Identifier, Is.EqualTo(expectedValue.Identifier));
            Assert.That(actualValue?.Type, Is.EqualTo(expectedValue.Type));
            Assert.That(actualValue?.Options, Is.EqualTo(expectedValue.Options));
            Assert.That(actualValue?.TrainResult, Is.EqualTo(expectedValue.TrainResult));
            Assert.That(actualValue?.TrainSql, Is.EqualTo(expectedValue.TrainSql));
            Assert.That(actualValue?.TrainState, Is.EqualTo(expectedValue.TrainState));
            
            Assert.That(actualById, Is.Not.Null);
            Assert.That(actualById?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualById?.Identifier, Is.EqualTo(expectedValue.Identifier));
            Assert.That(actualById?.Type, Is.EqualTo(expectedValue.Type));
            Assert.That(actualById?.Options, Is.EqualTo(expectedValue.Options));
            Assert.That(actualById?.TrainResult, Is.EqualTo(expectedValue.TrainResult));
            Assert.That(actualById?.TrainSql, Is.EqualTo(expectedValue.TrainSql));
            Assert.That(actualById?.TrainState, Is.EqualTo(expectedValue.TrainState));
            
            Assert.That(actualByIdentifier, Is.Not.Null);
            Assert.That(actualByIdentifier?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualByIdentifier?.Identifier, Is.EqualTo(expectedValue.Identifier));
            Assert.That(actualByIdentifier?.Type, Is.EqualTo(expectedValue.Type));
            Assert.That(actualByIdentifier?.Options, Is.EqualTo(expectedValue.Options));
            Assert.That(actualByIdentifier?.TrainResult, Is.EqualTo(expectedValue.TrainResult));
            Assert.That(actualByIdentifier?.TrainSql, Is.EqualTo(expectedValue.TrainSql));
            Assert.That(actualByIdentifier?.TrainState, Is.EqualTo(expectedValue.TrainState));
        });

        Assert.ThrowsAsync<Exception>(async () =>
        {
            await repository.SaveTrainingStateAsync(TenantId, Guid.NewGuid(), new MlModelTrainingState()
            {
                Id = Guid.NewGuid(),
                Result = "updated training result",
                State = MlModelTrainingStates.Error
            });
        });
        
        await repository.SaveTrainingStateAsync(TenantId, Guid.NewGuid(), new MlModelTrainingState()
        {
            Id = actualValue.Id,
            Result = "updated training result",
            State = MlModelTrainingStates.Error
        });
        
        actualValue = await repository.ByIdAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.Multiple(() =>
        {
            Assert.That(actualValue, Is.Not.Null);
            Assert.That(actualValue?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualValue?.TrainResult, Is.EqualTo("updated training result"));
            Assert.That(actualValue?.TrainState, Is.EqualTo(MlModelTrainingStates.Error));
        });
        
        var removeParams = new Dictionary<string, object>([new KeyValuePair<string, object>("Id", expectedValue.Id)]);

        var removeResult = await repository.RemoveAsync(TenantId, null, ImmutableDictionary<string, object>.Empty, removeParams);

        Assert.Multiple(() =>
        {
            Assert.That(removeResult.Result, Is.True);
        });

        actualValue = await repository.ByIdAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.That(actualValue, Is.Null);
    }

    [Test]
    public async Task Query_tenant_items_succeeds()
    {
        using var scope = Application.Services.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMlModelMetaRepository>();

        var fakeTenantIds = new[] { Guid.NewGuid(), Guid.NewGuid(), TenantId, Guid.NewGuid() };

        List<Guid> fakeValueIds = new List<Guid>();
        
        foreach (var fakeTenant in fakeTenantIds)
        {
            for (var i = 0; i < 10; i++)
            {
                var fakeValue = await repository.NewAsync(fakeTenant, "primary", ImmutableDictionary<string, object>.Empty);

                fakeValue.Identifier = $"fake_identifier_{i}";
                fakeValue.Type = MlModelTypes.Regression;
                fakeValue.Options = "{}";
                fakeValue.TrainResult = "fake train result";
                fakeValue.TrainSql = "fake train sql";
                fakeValue.TrainState = MlModelTrainingStates.UpToDate;
                
                await repository.SaveAsync(fakeTenant, null, "primary", ImmutableDictionary<string, object>.Empty, fakeValue);

                if (fakeTenant == TenantId)
                {
                    fakeValueIds.Add(fakeValue.Id);    
                }
            }
        }

        var actualTenantItemsCount = await repository.CountAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        var actualTenantAllItems = await repository.AllAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty);
        var actualTenantQueryItems = await repository.QueryAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        var actualSelectListItems = await repository.SelectListForTenantAsync(TenantId);
        var actualSelectByIdItem = await repository.SelectByIdForTenantAsync(TenantId, fakeValueIds[0]);
        
        Assert.Multiple(() =>
        {
            Assert.That(actualTenantItemsCount, Is.EqualTo(10));
            Assert.That(actualTenantAllItems.Count(), Is.EqualTo(10));
            Assert.That(actualTenantQueryItems.Count(), Is.EqualTo(10));
            Assert.That(actualSelectListItems.Count(), Is.EqualTo(10));
            Assert.That(actualSelectByIdItem, Is.Not.Null);
            Assert.That(actualSelectByIdItem?.Id, Is.EqualTo(fakeValueIds[0]));
        });
    }

    [Test]
    public async Task Import_values_succeeds()
    {
        using var scope = Application.Services.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMlModelMetaRepository>();

        var importList = new List<MlModel>();

        for (var i = 0; i < 10; i++)
        {
            var fakeValue = await repository.NewAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty);

            fakeValue.Identifier = $"fake_identifier_{i}";
            fakeValue.Type = MlModelTypes.Regression;
            fakeValue.Options = "{}";
            fakeValue.TrainResult = "fake train result";
            fakeValue.TrainSql = "fake train sql";
            fakeValue.TrainState = MlModelTrainingStates.UpToDate;
            
            importList.Add(fakeValue);
        }

        var importBinary = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(importList));

        using var importStream = new MemoryStream(importBinary);

        await repository.ImportAsync(TenantId, null, "primary", ImmutableDictionary<string, object>.Empty, importStream, (doc) => Task.FromResult(true));

        var actualTenantItemsCount = await repository.CountAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        var actualTenantAllItems = await repository.AllAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty);
        var actualTenantQueryItems = await repository.QueryAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        Assert.Multiple(() =>
        {
            Assert.That(actualTenantItemsCount, Is.EqualTo(10));
            Assert.That(actualTenantAllItems.Count(), Is.EqualTo(10));
            Assert.That(actualTenantQueryItems.Count(), Is.EqualTo(10));
        });
    }

    [Test]
    public async Task Export_values_succeeds()
    {
        using var scope = Application.Services.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMlModelMetaRepository>();

        var exportIdList = new List<Guid>();
        var exportItemList = new List<MlModel>();

        for (var i = 0; i < 10; i++)
        {
            var fakeValue = await repository.NewAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty);

            fakeValue.Identifier = $"fake_identifier_{i}";
            fakeValue.Type = MlModelTypes.Regression;
            fakeValue.Options = "{}";
            fakeValue.TrainResult = "fake train result";
            fakeValue.TrainSql = "fake train sql";
            fakeValue.TrainState = MlModelTrainingStates.UpToDate;
            
            await repository.SaveAsync(TenantId, null, "primary", ImmutableDictionary<string, object>.Empty, fakeValue);

            if (i % 2 == 0)
            {
                exportIdList.Add(fakeValue.Id);
                exportItemList.Add(fakeValue);
            }
        }

        var idStringValues = new StringValues(exportIdList.Select(id => id.ToString()).ToArray());
        
        var exportResult = await repository.ExportAsync(TenantId, "primary", ImmutableDictionary<string, object>.Empty, new Dictionary<string, object>(new[] { new KeyValuePair<string, object>("id", idStringValues) }));

        Assert.Multiple(() =>
        {
            Assert.That(exportResult.FileName, Is.EqualTo("primary.json"));
            Assert.That(exportResult.MediaType, Is.EqualTo("application/json"));
            Assert.That(exportResult.Data, Is.Not.Null);

            using var inputStream = new MemoryStream(exportResult.Data);
            using var streamReader = new StreamReader(inputStream);

            var actualItems = JsonConvert.DeserializeObject<IEnumerable<MlModel>>(streamReader.ReadToEnd())?.ToList();

            Assert.That(actualItems, Is.Not.Null);
            Assert.That(actualItems?.Count, Is.EqualTo(5));
            Assert.That(actualItems?.Select(item => item.Id), Is.EquivalentTo(exportItemList.Select(item => item.Id)));
        });
    }
    
    [Test]
    public async Task Execute_generated_list_query_succeeds()
    {
        using var scope = Application.Services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<MlDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IMlModelMetaRepository>();

        var listQuery = await repository.GenerateListQueryAsync(TenantId);

        var connection = dbContext.Database.GetDbConnection();
        
        var result = await connection.QueryAsync(listQuery);
        
        Assert.Multiple(() =>
        {
            Assert.That(result.Count(), Is.EqualTo(0));
        });
    }
}