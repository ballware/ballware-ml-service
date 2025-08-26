using System.Security.Claims;
using Ballware.Ml.Data.Common;
using Ballware.Ml.Data.Repository;
using Ballware.Ml.Data.SelectLists;
using Ballware.Shared.Authorization;
using Ballware.Ml.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Quartz;

namespace Ballware.Ml.Api.Endpoints;

public static class MlModelEndpoint
{
    private const string ApiTag = "MlModel";
    private const string ApiOperationPrefix = "MlModel";

    public static IEndpointRouteBuilder MapMlModelUserApi(this IEndpointRouteBuilder app,
        string basePath,
        string apiTag = ApiTag,
        string apiOperationPrefix = ApiOperationPrefix,
        string authorizationScope = "mlApi",
        string apiGroup = "ml")
    {
        app.MapGet(basePath + "/selectlist", HandleSelectListAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<IEnumerable<MlModelSelectListEntry>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "SelectList")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query list of all documents");
        
        app.MapGet(basePath + "/selectbyid/{id}", HandleSelectByIdAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<MlModelSelectListEntry>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "SelectById")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query select item by id");
        
        app.MapGet(basePath + "/consumebyid/{modelId}", HandleConsumeByIdAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<object>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "ConsumeById")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Consume model by id");
        
        app.MapPost(basePath + "/train", HandleTrainAsync)
            .RequireAuthorization(authorizationScope)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "Train")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Train models");
        
        return app;
    }
    
    public static IEndpointRouteBuilder MapMlModelServiceApi(this IEndpointRouteBuilder app,
        string basePath,
        string apiTag = ApiTag,
        string apiOperationPrefix = ApiOperationPrefix,
        string authorizationScope = "serviceApi",
        string apiGroup = "service")
    {
        app.MapPost(basePath + "/consumebyidbehalfofuser/{tenantId}/{userId}/{modelId}", HandleConsumeByIdBehalfOfUserAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<object>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new HttpMethodMetadata(["POST"]))
            .Accepts<Dictionary<string, object>>("application/json")
            .WithName(apiOperationPrefix + "ConsumeByIdBehalfOfUser")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Consume model by id behalf of user");
        
        app.MapPost(basePath + "/consumebyidentifierbehalfofuser/{tenantId}/{userId}/{identifier}", HandleConsumeByIdentifierBehalfOfUserAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<object>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new HttpMethodMetadata(["POST"]))
            .Accepts<Dictionary<string, object>>("application/json")
            .WithName(apiOperationPrefix + "ConsumeByIdentifierBehalfOfUser")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Consume model by identifier behalf of user");
        
        return app;
    }
    
    private static async Task<IResult> HandleSelectListAsync(IPrincipalUtils principalUtils, IMlModelMetaRepository repository, ClaimsPrincipal user)
    {
        var tenantId = principalUtils.GetUserTenandId(user);

        return Results.Ok(await repository.SelectListForTenantAsync(tenantId));
    }
    
    private static async Task<IResult> HandleSelectByIdAsync(IPrincipalUtils principalUtils, IMlModelMetaRepository repository, ClaimsPrincipal user, Guid id)
    {
        var tenantId = principalUtils.GetUserTenandId(user);
        
        var entry = await repository.SelectByIdForTenantAsync(tenantId, id);

        if (entry == null)
        {
            return Results.NotFound();
        }
        
        return Results.Ok(entry);
    }
    
    private static async Task<IResult> HandleConsumeByIdAsync(IPrincipalUtils principalUtils, IModelExecutor executor, ClaimsPrincipal user, Guid modelId, QueryValueBag query)
    {
        var tenantId = principalUtils.GetUserTenandId(user);
        
        var prediction = await executor.PredictAsync(tenantId, modelId, query.Query);
        
        return Results.Ok(prediction);
    }
    
    private static async Task<IResult> HandleTrainAsync(ISchedulerFactory schedulerFactory, IPrincipalUtils principalUtils, IMlModelMetaRepository mlModelMetaRepository, ClaimsPrincipal user, QueryValueBag query)
    {
        var tenantId = principalUtils.GetUserTenandId(user);
        var userId = principalUtils.GetUserId(user);

        if (query.Query.TryGetValue("id", out object ids))
        {
            if (ids is not string[] value)
            {
                return Results.Accepted();
            }
            
            foreach (var id in value)
            {
                var jobData = new JobDataMap();

                foreach (var q in query.Query)
                {
                    jobData.Add(q.Key, q.Value);
                }

                jobData["tenantId"] = tenantId;
                jobData["userId"] = userId;
                jobData["modelId"] = Guid.Parse(id);
                
                var updateTrainingStatePayload = new MlModelTrainingState()
                {
                    Id = Guid.Parse(id),
                    State = MlModelTrainingStates.Queued,
                    Result = string.Empty,
                };
                
                await mlModelMetaRepository.SaveTrainingStateAsync(tenantId, userId, updateTrainingStatePayload);
                
                await (await schedulerFactory.GetScheduler()).TriggerJob(JobKey.Create("train", "ml"), jobData);
            }
        }
        
        return Results.Accepted();
    }
    
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "S1172:Unused method parameters should be removed", Justification = "Parameter required by API contract for future extensibility")]
    private static async Task<IResult> HandleConsumeByIdBehalfOfUserAsync(IModelExecutor executor, Guid tenantId, Guid userId, Guid modelId, BodyValueBag query)
    {
        var prediction = await executor.PredictAsync(tenantId, modelId, query.Value);
        
        return Results.Ok(prediction);
    }
    
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "S1172:Unused method parameters should be removed", Justification = "Parameter required by API contract for future extensibility")]
    private static async Task<IResult> HandleConsumeByIdentifierBehalfOfUserAsync(IModelExecutor executor, Guid tenantId, Guid userId, string identifier, BodyValueBag query)
    {
        var prediction = await executor.PredictAsync(tenantId, identifier, query.Value);
        
        return Results.Ok(prediction);
    }
}