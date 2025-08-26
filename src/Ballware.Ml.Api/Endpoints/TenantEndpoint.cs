using System.Diagnostics.CodeAnalysis;
using AutoMapper;
using Ballware.Ml.Data;
using Ballware.Ml.Data.Public;
using Ballware.Ml.Data.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ballware.Ml.Api.Endpoints;

public static class TenantEndpoint
{
    private const string ApiTag = "Tenant";
    private const string ApiOperationPrefix = "Tenant";
    
    private const string MlLookupsDatasourceIdentifier = "MlLookups";
    
    public static IEndpointRouteBuilder MapMlTenantServiceApi(this IEndpointRouteBuilder app,
        string basePath,
        string apiTag = ApiTag,
        string apiOperationPrefix = ApiOperationPrefix,
        string authorizationScope = "serviceApi",
        string apiGroup = "service")
    {
        app.MapGet(basePath + "/reportmldatasourcesfortenant/{tenantId}", HandleReportMlDatasourcesForTenant)
            .RequireAuthorization(authorizationScope)
            .Produces<IEnumerable<ReportDatasourceDefinition>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "ReportMlDatasources")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query report ml datasources for tenant");
        
        return app;
    }
    
    private static async Task<IResult> HandleReportMlDatasourcesForTenant(IMapper mapper, IMlDbConnectionFactory mlDbConnectionFactory,
        IMlModelMetaRepository mlModelMetaRepository,
        Guid tenantId)
    {
        var mlConnectionProvider = mlDbConnectionFactory.Provider;
        var mlConnectionString = mlDbConnectionFactory.ConnectionString;
        
        var schemaDefinitions = new List<ReportDatasourceDefinition>();
        
        var mlLookupsSchemaDefinition = new ReportDatasourceDefinition
        {
            Name = MlLookupsDatasourceIdentifier,
            Provider = mlConnectionProvider,
            ConnectionString = mlConnectionString,
            Tables = new []
                {
                    new ReportDatasourceTable { Name = "mlmodelLookup", Query = await mlModelMetaRepository.GenerateListQueryAsync(tenantId) },
                }
        };
        
        schemaDefinitions.Add(mlLookupsSchemaDefinition);
        
        return Results.Ok(schemaDefinitions);
    }
}