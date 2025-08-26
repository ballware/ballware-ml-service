using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Ballware.Ml.Api.Endpoints;
using Ballware.Ml.Api.Tests.Utils;
using Ballware.Shared.Authorization;
using Ballware.Ml.Data.Repository;
using Ballware.Ml.Data.SelectLists;
using Ballware.Ml.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Ballware.Ml.Api.Tests.MlModel;

public class MlModelMetaApiTest : ApiMappingBaseTest
{   
    [Test]
    public async Task HandleSelectList_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedList = new List<MlModelSelectListEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Identifier = "model1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Identifier = "model2"
            }
        };

        var principalUtilsMock = new Mock<IPrincipalUtils>();
        var tenantRightsCheckerMock = new Mock<ITenantRightsChecker>();
        var authorizationMetadataProviderMock = new Mock<IAuthorizationMetadataProvider>();
        var repositoryMock = new Mock<IMlModelMetaRepository>();
        var executorMock = new Mock<IModelExecutor>();

        principalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        repositoryMock
            .Setup(r => r.SelectListForTenantAsync(expectedTenantId))
            .ReturnsAsync(expectedList);

        var client = await CreateApplicationClientAsync("mlApi", services =>
        {
            services.AddSingleton<IPrincipalUtils>(principalUtilsMock.Object);
            services.AddSingleton<ITenantRightsChecker>(tenantRightsCheckerMock.Object);
            services.AddSingleton<IAuthorizationMetadataProvider>(authorizationMetadataProviderMock.Object);
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
        var response = await client.GetAsync($"mlmodel/selectlist");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<IEnumerable<MlModelSelectListEntry>>(await response.Content.ReadAsStringAsync())?.ToList();
        
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(DeepComparer.AreListsEqual(expectedList, result, TestContext.WriteLine));
        });
    }
    
    [Test]
    public async Task HandleSelectById_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedEntry = new MlModelSelectListEntry()
        {
            Id = Guid.NewGuid(),
            Identifier = "model1"
        };
        
        var principalUtilsMock = new Mock<IPrincipalUtils>();
        var tenantRightsCheckerMock = new Mock<ITenantRightsChecker>();
        var authorizationMetadataProviderMock = new Mock<IAuthorizationMetadataProvider>();
        var repositoryMock = new Mock<IMlModelMetaRepository>();
        var executorMock = new Mock<IModelExecutor>();

        principalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        repositoryMock
            .Setup(r => r.SelectByIdForTenantAsync(expectedTenantId, expectedEntry.Id))
            .ReturnsAsync(expectedEntry);

        var client = await CreateApplicationClientAsync("mlApi", services =>
        {
            services.AddSingleton<IPrincipalUtils>(principalUtilsMock.Object);
            services.AddSingleton<ITenantRightsChecker>(tenantRightsCheckerMock.Object);
            services.AddSingleton<IAuthorizationMetadataProvider>(authorizationMetadataProviderMock.Object);
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
        var response = await client.GetAsync($"mlmodel/selectbyid/{expectedEntry.Id}");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<MlModelSelectListEntry>(await response.Content.ReadAsStringAsync());
        
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(DeepComparer.AreEqual(expectedEntry, result, TestContext.WriteLine));
        });

        // Arrange
        principalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());
        
        // Act
        var notFoundResponse = await client.GetAsync($"mlmodel/selectbyid/{expectedEntry.Id}");
        
        // Assert
        Assert.That(notFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
}