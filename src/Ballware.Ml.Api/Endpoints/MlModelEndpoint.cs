using System.Security.Claims;
using Ballware.Ml.Authorization;
using Ballware.Ml.Engine;
using Ballware.Ml.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
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
        app.MapGet(basePath + "/consumebyid/{modelId}", HandleConsumeByIdAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<object>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "ConsumeById")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Consume model by id");
        
        app.MapGet(basePath + "/train", HandleTrainAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<object>()
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
            .WithName(apiOperationPrefix + "ConsumeByIdBehalfOfUser")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Consume model by id behalf of user");
        
        app.MapPost(basePath + "/consumebyidentifierbehalfofuser/{tenantId}/{userId}/{identifier}", HandleConsumeByIdentifierBehalfOfUserAsync)
            .RequireAuthorization(authorizationScope)
            .Produces<object>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "ConsumeByIdentifierBehalfOfUser")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Consume model by identifier behalf of user");
        
        return app;
    }
    
    private static async Task<IResult> HandleConsumeByIdAsync(IPrincipalUtils principalUtils, IModelExecutor executor, ClaimsPrincipal user, Guid modelId, QueryValueBag query)
    {
        var tenantId = principalUtils.GetUserTenandId(user);
        
        var prediction = await executor.PredictAsync(tenantId, modelId, query.Query);
        
        return Results.Ok(prediction);
    }
    
    private static async Task<IResult> HandleTrainAsync(ISchedulerFactory schedulerFactory, IPrincipalUtils principalUtils, IMetadataAdapter metadataAdapter, ClaimsPrincipal user, QueryValueBag query)
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
                
                var updateTrainingStatePayload = new UpdateMlModelTrainingStatePayload()
                {
                    Id = Guid.Parse(id),
                    State = MlModelTrainingStates.Queued,
                    Result = string.Empty,
                };
                
                await metadataAdapter.MlModelUpdateTrainingStateBehalfOfUserAsync(tenantId, userId, updateTrainingStatePayload);
                
                await (await schedulerFactory.GetScheduler()).TriggerJob(JobKey.Create("train", "ml"), jobData);
            }
        }
        
        return Results.Accepted();
    }
    
    private static async Task<IResult> HandleConsumeByIdBehalfOfUserAsync(IModelExecutor executor, Guid tenantId, Guid userId, Guid modelId, BodyValueBag query)
    {
        var prediction = await executor.PredictAsync(tenantId, modelId, query.Value);
        
        return Results.Ok(prediction);
    }
    
    private static async Task<IResult> HandleConsumeByIdentifierBehalfOfUserAsync(IModelExecutor executor, Guid tenantId, Guid userId, string identifier, BodyValueBag query)
    {
        var prediction = await executor.PredictAsync(tenantId, identifier, query.Value);
        
        return Results.Ok(prediction);
    }
}