using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ballware.Ml.Api.Endpoints;
using Ballware.Ml.Api.Tests.Utils;
using Ballware.Ml.Data.Common;
using Ballware.Ml.Data.Repository;
using Ballware.Ml.Engine;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Quartz;

namespace Ballware.Ml.Api.Tests.MlModel;

public class MlModelConsumeAndTrainBehaviorTest : ApiMappingBaseTest
{
    [Test]
    public async Task HandleTrain_respondsAccepted_although_the_endpoint_declares_created()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedModelId = Guid.NewGuid();

        var principalUtilsMock = new Mock<IPrincipalUtils>();
        var repositoryMock = new Mock<IMlModelMetaRepository>();
        var executorMock = new Mock<IModelExecutor>();
        var schedulerFactoryMock = new Mock<ISchedulerFactory>();
        var schedulerMock = new Mock<IScheduler>();

        principalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);

        principalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        schedulerFactoryMock
            .Setup(f => f.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedulerMock.Object);

        var client = await CreateApplicationClientAsync("mlApi", services =>
        {
            services.AddSingleton<IPrincipalUtils>(principalUtilsMock.Object);
            services.AddSingleton<IMlModelMetaRepository>(repositoryMock.Object);
            services.AddSingleton<IModelExecutor>(executorMock.Object);
            services.AddSingleton<ISchedulerFactory>(schedulerFactoryMock.Object);
        }, app =>
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMlModelUserApi("mlmodel");
            });
        });

        // Act
        var response = await client.PostAsync($"mlmodel/train?id={expectedModelId}", new StringContent(string.Empty));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        repositoryMock.Verify(r => r.SaveTrainingStateAsync(
            expectedTenantId,
            expectedUserId,
            It.Is<MlModelTrainingState>(state =>
                state.Id == expectedModelId &&
                state.State == MlModelTrainingStates.Queued &&
                state.Result == string.Empty)),
            Times.Once);
    }

    [Test]
    public async Task HandleConsumeById_binds_query_values_as_string_arrays()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedModelId = Guid.NewGuid();
        var boundQuery = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var principalUtilsMock = new Mock<IPrincipalUtils>();
        var repositoryMock = new Mock<IMlModelMetaRepository>();
        var executorMock = new Mock<IModelExecutor>();

        principalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);

        executorMock
            .Setup(e => e.PredictAsync(expectedTenantId, expectedModelId, It.IsAny<IDictionary<string, object>>()))
            .Callback<Guid, Guid, IDictionary<string, object>>((_, _, query) =>
            {
                foreach (var entry in query)
                {
                    boundQuery[entry.Key] = entry.Value;
                }
            })
            .ReturnsAsync(new { Score = 0f });

        var client = await CreateApplicationClientAsync("mlApi", services =>
        {
            services.AddSingleton<IPrincipalUtils>(principalUtilsMock.Object);
            services.AddSingleton<IMlModelMetaRepository>(repositoryMock.Object);
            services.AddSingleton<IModelExecutor>(executorMock.Object);
        }, app =>
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMlModelUserApi("mlmodel");
            });
        });

        // Act
        var response = await client.GetAsync($"mlmodel/consumebyid/{expectedModelId}?Day=3&Hour=7");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.Multiple(() =>
        {
            Assert.That(boundQuery.Keys, Is.EquivalentTo(new[] { "Day", "Hour" }));
            Assert.That(boundQuery["Day"], Is.InstanceOf<string[]>());
            Assert.That((string[])boundQuery["Day"], Is.EquivalentTo(new[] { "3" }));
            // The engine converts bound values with value.ToString(), which for a string[]
            // yields the type name instead of the scalar value (known limitation).
            Assert.That(boundQuery["Day"].ToString(), Is.EqualTo("System.String[]"));
        });
    }

    [Test]
    public async Task HandleConsumeByIdBehalfOfUser_binds_typed_body_values()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedModelId = Guid.NewGuid();
        var boundQuery = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var executorMock = new Mock<IModelExecutor>();

        executorMock
            .Setup(e => e.PredictAsync(expectedTenantId, expectedModelId, It.IsAny<IDictionary<string, object>>()))
            .Callback<Guid, Guid, IDictionary<string, object>>((_, _, query) =>
            {
                foreach (var entry in query)
                {
                    boundQuery[entry.Key] = entry.Value;
                }
            })
            .ReturnsAsync(new { Score = 0f });

        var client = await CreateApplicationClientAsync("serviceApi", services =>
        {
            services.AddSingleton<IModelExecutor>(executorMock.Object);
        }, app =>
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMlModelServiceApi("mlmodel");
            });
        });

        // Act
        var response = await client.PostAsync(
            $"mlmodel/consumebyidbehalfofuser/{expectedTenantId}/{expectedUserId}/{expectedModelId}",
            new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["Day"] = 3,
                ["Hour"] = 7,
                ["Weekend"] = true,
                ["Station"] = "station-a",
                ["Ratio"] = 1.5
            }), Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.Multiple(() =>
        {
            Assert.That(boundQuery["Day"], Is.EqualTo(3L));
            Assert.That(boundQuery["Hour"], Is.EqualTo(7L));
            Assert.That(boundQuery["Weekend"], Is.EqualTo(true));
            Assert.That(boundQuery["Station"], Is.EqualTo("station-a"));
            Assert.That(boundQuery["Ratio"], Is.EqualTo(1.5d));
        });
    }

    [Test]
    public async Task HandleConsumeByIdBehalfOfUser_rejects_unreadable_body()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedModelId = Guid.NewGuid();

        var executorMock = new Mock<IModelExecutor>();

        executorMock
            .Setup(e => e.PredictAsync(expectedTenantId, expectedModelId, It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(new { Score = 0f });

        var client = await CreateApplicationClientAsync("serviceApi", services =>
        {
            services.AddSingleton<IModelExecutor>(executorMock.Object);
        }, app =>
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMlModelServiceApi("mlmodel");
            });
        });

        // Act — BodyValueBag.BindAsync yields null for a body that is not a JSON object,
        // which the endpoint treats as a binding failure.
        var response = await client.PostAsync(
            $"mlmodel/consumebyidbehalfofuser/{expectedTenantId}/{expectedUserId}/{expectedModelId}",
            new StringContent("not-json", Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task HandleConsumeByIdentifierBehalfOfUser_resolves_model_by_identifier()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        const string expectedIdentifier = "daytime-personal-count";

        var executorMock = new Mock<IModelExecutor>();

        executorMock
            .Setup(e => e.PredictAsync(expectedTenantId, expectedIdentifier, It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(new { Score = 0f });

        var client = await CreateApplicationClientAsync("serviceApi", services =>
        {
            services.AddSingleton<IModelExecutor>(executorMock.Object);
        }, app =>
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMlModelServiceApi("mlmodel");
            });
        });

        // Act
        var response = await client.PostAsync(
            $"mlmodel/consumebyidentifierbehalfofuser/{expectedTenantId}/{expectedUserId}/{expectedIdentifier}",
            new StringContent("{\"Day\":3}", Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        executorMock.Verify(e => e.PredictAsync(expectedTenantId, expectedIdentifier,
            It.IsAny<IDictionary<string, object>>()), Times.Once);
    }
}
