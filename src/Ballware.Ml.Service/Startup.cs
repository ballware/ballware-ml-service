using Ballware.Generic.Service.Client;
using Ballware.Ml.Api.Endpoints;
using Ballware.Ml.Authorization;
using Ballware.Ml.Jobs;
using Ballware.Ml.Metadata;
using Ballware.Meta.Service.Client;
using Ballware.Ml.Caching;
using Ballware.Ml.Caching.Configuration;
using Ballware.Ml.Data.Ef;
using Ballware.Ml.Data.Ef.Configuration;
using Ballware.Ml.Data.Ef.Postgres;
using Ballware.Ml.Data.Ef.SqlServer;
using Ballware.Ml.Engine.AutoMl;
using Ballware.Ml.Service.Adapter;
using Ballware.Ml.Service.Configuration;
using Ballware.ML.Service.Configuration;
using Ballware.Ml.Service.Mappings;
using Ballware.Storage.Service.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Microsoft.OpenApi.Models;
using Quartz;
using CorsOptions = Ballware.Ml.Service.Configuration.CorsOptions;
using SwaggerOptions = Ballware.Ml.Service.Configuration.SwaggerOptions;
using Serilog;

namespace Ballware.Ml.Service;


public class Startup(IWebHostEnvironment environment, ConfigurationManager configuration, IServiceCollection services)
{
    private string ClaimTypeScope { get; }= "scope";
    
    private IWebHostEnvironment Environment { get; } = environment;
    private ConfigurationManager Configuration { get; } = configuration;
    private IServiceCollection Services { get; } = services;

    public void InitializeServices()
    {
        CorsOptions? corsOptions = Configuration.GetSection("Cors").Get<CorsOptions>();
        AuthorizationOptions? authorizationOptions =
            Configuration.GetSection("Authorization").Get<AuthorizationOptions>();
        StorageOptions? storageOptions = Configuration.GetSection("Storage").Get<StorageOptions>();
        SwaggerOptions? swaggerOptions = Configuration.GetSection("Swagger").Get<SwaggerOptions>();
        ServiceClientOptions? metaClientOptions = Configuration.GetSection("MetaClient").Get<ServiceClientOptions>();
        ServiceClientOptions? storageClientOptions = Configuration.GetSection("StorageClient").Get<ServiceClientOptions>();
        ServiceClientOptions? genericClientOptions = Configuration.GetSection("GenericClient").Get<ServiceClientOptions>();
        
        Services.AddOptionsWithValidateOnStart<AuthorizationOptions>()
            .Bind(Configuration.GetSection("Authorization"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<StorageOptions>()
            .Bind(Configuration.GetSection("Storage"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<CacheOptions>()
            .Bind(Configuration.GetSection("Cache"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<SwaggerOptions>()
            .Bind(Configuration.GetSection("Swagger"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<ServiceClientOptions>()
            .Bind(Configuration.GetSection("MetaClient"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<ServiceClientOptions>()
            .Bind(Configuration.GetSection("StorageClient"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<ServiceClientOptions>()
            .Bind(Configuration.GetSection("GenericClient"))
            .ValidateDataAnnotations();
        
        if (authorizationOptions == null || storageOptions == null)
        {
            throw new ConfigurationException("Required configuration for authorization and storage is missing");
        }
        
        var mlConnectionString = Configuration.GetConnectionString(storageOptions.ConnectionString);

        if (string.IsNullOrEmpty(mlConnectionString))
        {
            throw new ConfigurationException("Connection string for storage is missing");
        }

        if (metaClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for metaClient is missing");
        }

        if (storageClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for storageClient is missing");
        }
        
        if (genericClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for genericClient is missing");
        }

        Services.AddMemoryCache();
        Services.AddDistributedMemoryCache();
        
        Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.Authority = authorizationOptions.Authority;
            options.Audience = authorizationOptions.Audience;
            options.RequireHttpsMetadata = authorizationOptions.RequireHttpsMetadata;
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
            {
                ValidIssuer = authorizationOptions.Authority
            };
        });

        Services.AddAuthorizationBuilder()
            .AddPolicy("mlApi", policy => policy.RequireAssertion(context =>
                context.User
                    .Claims
                    .Where(c => ClaimTypeScope == c.Type)
                    .SelectMany(c => c.Value.Split(' '))
                    .Any(s => s.Equals(authorizationOptions.RequiredMetaScope, StringComparison.Ordinal)))
            )
            .AddPolicy("serviceApi", policy => policy.RequireAssertion(context =>
                context.User
                    .Claims
                    .Where(c => ClaimTypeScope == c.Type)
                    .SelectMany(c => c.Value.Split(' '))
                    .Any(s => s.Equals(authorizationOptions.RequiredServiceScope, StringComparison.Ordinal)))
            );

        if (corsOptions != null)
        {
            Services.AddCors(options =>
            {
                options.AddDefaultPolicy(c =>
                {
                    c.WithOrigins(corsOptions.AllowedOrigins)
                        .WithMethods(corsOptions.AllowedMethods)
                        .WithHeaders(corsOptions.AllowedHeaders);
                });
            });
        }
        
        Services.Configure<JsonOptions>(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = null;
        });
        
        Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
        });
        
        Services.AddHttpContextAccessor();

        Services.AddMvcCore()
            .AddApiExplorer();

        Services.AddControllers();
        
        Services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));
        Services.AddBallwareMlBackgroundJobs();

        Services.AddClientCredentialsTokenManagement()
            .AddClient("meta", client =>
            {
                client.TokenEndpoint = metaClientOptions.TokenEndpoint;

                client.ClientId = metaClientOptions.ClientId;
                client.ClientSecret = metaClientOptions.ClientSecret;

                client.Scope = metaClientOptions.Scopes;
            })
            .AddClient("storage", client =>
            {
                client.TokenEndpoint = storageClientOptions.TokenEndpoint;

                client.ClientId = storageClientOptions.ClientId;
                client.ClientSecret = storageClientOptions.ClientSecret;

                client.Scope = storageClientOptions.Scopes;
            })
            .AddClient("generic", client =>
            {
                client.TokenEndpoint = genericClientOptions.TokenEndpoint;

                client.ClientId = genericClientOptions.ClientId;
                client.ClientSecret = genericClientOptions.ClientSecret;

                client.Scope = genericClientOptions.Scopes;
            });
        
        Services.AddHttpClient<MetaServiceClient>(client =>
            {
                client.BaseAddress = new Uri(metaClientOptions.ServiceUrl);
            })
#if DEBUG            
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
#endif                  
            .AddClientCredentialsTokenHandler("meta");

        Services.AddHttpClient<StorageServiceClient>(client =>
            {
                client.BaseAddress = new Uri(storageClientOptions.ServiceUrl);
            })
#if DEBUG            
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
#endif            
            .AddClientCredentialsTokenHandler("storage");
        
        Services.AddHttpClient<GenericServiceClient>(client =>
            {
                client.BaseAddress = new Uri(genericClientOptions.ServiceUrl);
            })
#if DEBUG            
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
#endif                        
            .AddClientCredentialsTokenHandler("generic");
        
        Services.AddAutoMapper(config =>
        {
            config.AddBallwareMlStorageMappings();
            config.AddProfile<MetaServiceMlMetadataProfile>();
        });
        
        Services.AddScoped<IMetadataAdapter, MetaServiceMetadataAdapter>();
        Services.AddScoped<ITenantDataAdapter, GenericServiceTenantDataAdapter>();
        Services.AddScoped<IAutoMlFileStorageAdapter, StorageServiceFileStorageAdapter>();
        
        if ("mssql".Equals(storageOptions.Provider, StringComparison.InvariantCultureIgnoreCase) && !string.IsNullOrEmpty(mlConnectionString))
        {
            Services.AddBallwareMlStorageForSqlServer(storageOptions, mlConnectionString);
        } 
        else if ("postgres".Equals(storageOptions.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                   !string.IsNullOrEmpty(mlConnectionString))
        {
            Services.AddBallwareMlStorageForPostgres(storageOptions, mlConnectionString);
        }
        
        Services.AddBallwareMlMemoryCaching();
        Services.AddBallwareMlAuthorizationUtils(authorizationOptions.TenantClaim, authorizationOptions.UserIdClaim, authorizationOptions.RightClaim);
        Services.AddBallwareAutoMlExecutor();
        
        Services.AddEndpointsApiExplorer();
        
        if (swaggerOptions != null)
        {
            Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("ml", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "ballware Ml user API",
                    Version = "v1"
                });
                
                c.SwaggerDoc("service", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "ballware Ml service API",
                    Version = "v1"
                });
                
                c.EnableAnnotations();

                c.AddSecurityDefinition("oidc", new OpenApiSecurityScheme
                {
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.OpenIdConnect,
                    OpenIdConnectUrl = new Uri(authorizationOptions.Authority + "/.well-known/openid-configuration")
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oidc" }
                        },
                        swaggerOptions.RequiredScopes.Split(" ")
                    }
                });
            });
        }
    }

    public void InitializeApp(WebApplication app)
    {
        if (Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            IdentityModelEventSource.ShowPII = true;
        }
        
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                var exception = exceptionFeature?.Error;

                if (exception != null)
                {
                    Log.Error(exception, "Unhandled exception occurred");

                    var problemDetails = new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/500",
                        Title = "An unexpected error occurred.",
                        Status = StatusCodes.Status500InternalServerError,
                        Detail = app.Environment.IsDevelopment() ? exception.ToString() : null,
                        Instance = context.Request.Path
                    };

                    context.Response.StatusCode = problemDetails.Status.Value;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(problemDetails);
                }
            });
        });

        app.UseCors();
        app.UseRouting();

        app.UseAuthorization();

        app.MapMlModelUserApi("/ml");
        app.MapMlModelServiceApi("/ml");
        
        var authorizationOptions = app.Services.GetService<IOptions<AuthorizationOptions>>()?.Value;
        var swaggerOptions = app.Services.GetService<IOptions<SwaggerOptions>>()?.Value;

        if (swaggerOptions != null && authorizationOptions != null)
        {
            app.MapSwagger();

            app.UseSwagger();

            if (swaggerOptions.EnableClient)
            {
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("ml/swagger.json", "ballware Machine learning user API");
                    c.SwaggerEndpoint("service/swagger.json", "ballware Machine learning service  API");

                    c.OAuthClientId(swaggerOptions.ClientId);
                    c.OAuthClientSecret(swaggerOptions.ClientSecret);
                    c.OAuthScopes(swaggerOptions.RequiredScopes?.Split(" "));
                    c.OAuthUsePkce();
                });
            }
        }
    }
}