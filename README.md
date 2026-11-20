# Ballware ML Service

[![Build & Publish (main)](https://github.com/ballware/ballware-ml-service/actions/workflows/publish-packages.yml/badge.svg?branch=main)](https://github.com/ballware/ballware-ml-service/actions/workflows/publish-packages.yml)
[![CI Tests (main)](https://github.com/ballware/ballware-ml-service/actions/workflows/sonarqube-latest.yml/badge.svg?branch=main)](https://github.com/ballware/ballware-ml-service/actions/workflows/sonarqube-latest.yml)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=ballware_ballware-ml-service&metric=coverage)](https://sonarcloud.io/summary/overall?id=ballware_ballware-ml-service)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=ballware_ballware-ml-service&metric=bugs)](https://sonarcloud.io/summary/overall?id=ballware_ballware-ml-service)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=ballware_ballware-ml-service&metric=vulnerabilities)](https://sonarcloud.io/summary/overall?id=ballware_ballware-ml-service)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=ballware_ballware-ml-service&metric=security_rating)](https://sonarcloud.io/summary/overall?id=ballware_ballware-ml-service)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=ballware_ballware-ml-service&metric=reliability_rating)](https://sonarcloud.io/summary/overall?id=ballware_ballware-ml-service)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=ballware_ballware-ml-service&metric=sqale_rating)](https://sonarcloud.io/summary/overall?id=ballware_ballware-ml-service)

This repository contains the Ballware ML Service – the component that defines, trains, and serves machine learning models for tenants of the Ballware platform. Model definitions (features, prediction field, training query) are stored locally, training data is pulled from the tenant's operational data, trained model artifacts are kept as attachment files, and predictions are exposed through HTTP APIs.

For a complete, multi-service installation (Keycloak, Meta, Storage, Generic, etc.), please use the umbrella repository: https://github.com/ballware/ballware-docker-compose. This README focuses solely on the specifics of the ML Service.


- Tech stack: .NET 10, ASP.NET Core (minimal API), EF Core (PostgreSQL or SQL Server), ML.NET AutoML, Serilog, Quartz, Mapster, Jint (rights expressions)
- AuthN/Z: OAuth2/OIDC with JWT Bearer, scope-based authorization (mlApi, serviceApi)
- Model engine: Microsoft.ML.AutoML regression experiment (featurizer + auto-selected regression trainer, R² metric)
- Caching: in-memory, tenant-aware model cache
- Clients: calls the Meta Service (metadata/rights/jobs), the Generic Service (training data queries), and the Storage Service (model + log files) via OAuth2 client credentials
- API docs: Swagger/OpenAPI, plus a generated C# client (`Ballware.Ml.Service.Client`, NSwag)


## Repository structure

- `src/Ballware.Ml.Service/` – Web service host (Startup, Program, appsettings, Dockerfile)
- `src/Ballware.Ml.Api/` – API endpoints and route mapping
- `src/Ballware.Ml.Service.Client/` – NSwag-generated client (`MlServiceClient`)
- `src/Ballware.Ml.Data/` – domain contracts, repositories, public model types
- `src/Ballware.Ml.Data.Ef/` – shared EF Core mapping and repository base
- `src/Ballware.Ml.Data.Ef.Postgres/`, `src/Ballware.Ml.Data.Ef.SqlServer/` – provider-specific storage, migrations, initialization worker
- `src/Ballware.Ml.Engine/` – `IModelExecutor` abstraction (train/predict)
- `src/Ballware.Ml.Engine.AutoMl/` – ML.NET AutoML implementation (executor, dynamic input type builder, prediction context)
- `src/Ballware.Ml.Jobs/` – background jobs (Quartz `train`/`ml`)
- `src/Ballware.Ml.Caching/` – tenant-aware model cache
- `src/Ballware.Ml.Metadata/` – metadata contracts (model options, regression fields, tenant data adapter)
- `src/Ballware.Ml.Service/Adapter/` – adapters to Meta / Storage / Generic services
- `test/*` – unit/integration tests (API, Postgres, SqlServer)

Package dependencies on `Ballware.Shared.*` come from the private GitHub NuGet feed (`https://nuget.pkg.github.com/ballware/index.json`) and require a `GITHUB_USERNAME` / `GITHUB_PAT` pair for restore and image builds.


## What the ML Service provides

The service hosts three logical API surfaces:

- ML API – tenant-facing model operations under the `mlApi` scope
- Meta-facing editing API – CRUD/edit endpoints for `MlModel` definitions, backed by Meta Service metadata and rights (`meta` policy)
- Service API – endpoints used by other services/clients under the `serviceApi` scope

Mounted routes:

- `/ml/mlmodel` – model select list, select by id, predict by id, trigger training (`mlApi`)
- `/ml/mlmodel` – editable `MlModel` entity API (query/create/update/delete with rights checks)
- `/ml/mlmodel/consumebyidbehalfofuser/{tenantId}/{userId}/{modelId}` – predict on behalf of a user (`serviceApi`)
- `/ml/mlmodel/consumebyidentifierbehalfofuser/{tenantId}/{userId}/{identifier}` – predict by model identifier (`serviceApi`)
- `/ml/tenant/reportmldatasourcesfortenant/{tenantId}` – report datasource definition (`mlmodelLookup`) for a tenant (`serviceApi`)

Use Swagger UI to explore the concrete operations and models. Two Swagger documents are published: `ml` (user API) and `service` (service API).


## Model definitions, training and prediction

An `MlModel` record holds:

| Field | Meaning |
| --- | --- |
| `Identifier` | tenant-unique logical name used for prediction-by-identifier and cache keys |
| `Type` | model type; currently `Regression` |
| `TrainSql` | plain query executed against the tenant's data via the Generic Service |
| `Options` | JSON options: `TrainingTime` (seconds), `TrainingFileName`, `TrainingLogFileName`, and for regression `FeatureFields` / `PredictionField` |
| `TrainState` | `Unknown`, `Outdated`, `Queued`, `InProgress`, `UpToDate`, `Error` |
| `TrainResult` | JSON summary of the last run (record count, loss, metric, duration, peak CPU/memory) or the error message |

Training flow (`POST /ml/mlmodel/train` with `id` list):

1. Model state is set to `Queued` and a Quartz job (`train`/`ml`) is triggered.
2. The job sets state to `InProgress`, loads metadata, compiles a dynamic input type from `Options`, and loads training rows from `TrainSql`.
3. ML.NET AutoML builds a featurizer + regression pipeline and runs an experiment (R² metric, default 60 s training time).
4. The resulting model is uploaded to the Storage Service as attachment `model.zip` (or `Options.TrainingFileName`); the AutoML log is uploaded as `Options.TrainingLogFileName`.
5. The tenant model cache entry is purged and the state becomes `UpToDate` (or `Error`).

Prediction flow: metadata is resolved for the tenant, the model artifact is fetched from the Storage Service and cached per tenant/identifier, and the JSON request body is mapped to the compiled input type; the prediction is returned as JSON.


## Quick start (local development)

Prerequisites:

- .NET SDK 8.0+
- PostgreSQL or SQL Server for model metadata storage
- An OIDC provider (e.g., Keycloak) reachable by the service
- Reachable Meta Service, Generic Service, and Storage Service (the service fails fast if any of the client sections is missing)

Steps:

1) Provide a local development certificate

- The Development profile expects `certificate/aspnet-dev-cert.pfx` (git-ignored). Either place a PFX there or switch the HTTPS endpoint in `appsettings.Development.json` to `Https` and run
  - `dotnet dev-certs https --trust`

2) Create `src/Ballware.Ml.Service/appsettings.local.json`

- Adjust Authority, connection strings, and client credentials to your setup (see Configuration section below). A working sample is present in the repository.

3) Start the service

- From the repo root:
  - `dotnet restore` (private feed access may require a GitHub PAT)
  - `dotnet build`
  - `dotnet run --project src/Ballware.Ml.Service`

By default in Development the service listens on:

- HTTP: `http://localhost:5003`
- HTTPS: `https://localhost:6003`

4) Open Swagger UI

- Navigate to `https://localhost:6003/swagger` (see Swagger section for usage with OIDC).


## Configuration

Configuration is loaded in this order: `appsettings.json` → `appsettings.{Environment}.json` → `appsettings.local.json` → environment variables.

Key sections used by the service:

- Authorization
- Storage (EF Core provider + connection string name)
- Cache
- Swagger
- Cors
- MetaClient (OAuth2 client credentials for the Meta Service)
- GenericClient (OAuth2 client credentials for the Generic Service)
- StorageClient (OAuth2 client credentials for the Storage Service)
- Quartz
- ConnectionStrings (ML storage + QuartzConnection)
- Kestrel (incl. `MaxRequestBodySize`, currently 40 MB for training payloads)

Example (trim to your needs):

```
{
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://localhost:5003" },
      "Https": { "Url": "https://localhost:6003" }
    },
    "Limits": { "MaxRequestBodySize": 40000000 }
  },
  "Authorization": {
    "Authority": "https://localhost:3001/realms/ballware",
    "Audience": "metaApi",
    "RequireHttpsMetadata": true,
    "TenantClaim": "tenant",
    "UserIdClaim": "sub",
    "RightClaim": "right",
    "RequiredMetaScope": "mlApi",
    "RequiredServiceScope": "serviceApi"
  },
  "Storage": {
    "Provider": "postgres",               // "postgres" or "mssql"
    "AutoMigrations": true,
    "ConnectionString": "MlPostgresStorageConnection"
  },
  "Cache": {
    "CacheExpirationHours": 1
  },
  "Cors": {
    "AllowedOrigins": "*",
    "AllowedMethods": "*",
    "AllowedHeaders": "*"
  },
  "Swagger": {
    "EnableClient": true,
    "ClientId": "ballwareweb",
    "ClientSecret": "<secret>",
    "RequiredScopes": "openid mlApi"
  },
  "MetaClient": {
    "ServiceUrl": "https://localhost:6001",
    "TokenEndpoint": "https://localhost:3001/realms/ballware/protocol/openid-connect/token",
    "ClientId": "meta",
    "ClientSecret": "<secret>",
    "Scopes": "openid serviceApi offline_access"
  },
  "GenericClient": {
    "ServiceUrl": "https://localhost:6002",
    "TokenEndpoint": "https://localhost:3001/realms/ballware/protocol/openid-connect/token",
    "ClientId": "generic",
    "ClientSecret": "<secret>",
    "Scopes": "openid serviceApi offline_access"
  },
  "StorageClient": {
    "ServiceUrl": "https://localhost:6005",
    "TokenEndpoint": "https://localhost:3001/realms/ballware/protocol/openid-connect/token",
    "ClientId": "storage",
    "ClientSecret": "<secret>",
    "Scopes": "openid serviceApi offline_access"
  },
  "Quartz": {
    // Example for PostgreSQL-backed clustered job store
    "quartz.scheduler.instanceName": "ballware-local-ml",
    "quartz.jobStore.dataSource": "ballware",
    "quartz.dataSource.ballware.provider": "Npgsql",
    "quartz.dataSource.ballware.connectionStringName": "QuartzConnection"

    // Use this for an in-memory job store in demos/tests:
    // "quartz.jobStore.type": "Quartz.Simpl.RAMJobStore, Quartz"
  },
  "ConnectionStrings": {
    "MlPostgresStorageConnection": "Host=localhost;Port=5432;Database=ml;Username=ml;Password=***",
    "MlSqlStorageConnection": "Server=localhost,1433;Initial Catalog=ml;User ID=ml;Password=***;Encrypt=True;TrustServerCertificate=true",
    "QuartzConnection": "Host=localhost;Port=5432;Database=quartz;Username=quartz;Password=***"
  }
}
```

Notes:

- `Storage.ConnectionString` is the *name* of a key in `ConnectionStrings`, not the DSN itself. `Storage.Provider` selects the matching `AddBallwareMlStorageForPostgres`/`...SqlServer` registration.
- Cache: the model cache is in-memory and tenant-aware; it is purged whenever a model is retrained.
- Swagger: when `EnableClient` is true, the UI exposes the "ml" and "service" documents with OIDC (PKCE) security.
- Clients: outgoing HTTP calls to Meta, Generic, and Storage services use OAuth2 client credentials; in Debug builds, server certificate validation is disabled for those outgoing calls only.
- `Authorization.Audience` must match the audience your IdP stamps on tokens for this API (the editing API is `meta`-facing, hence `metaApi` is a common value).


## Authentication and authorization

- The service uses JWT Bearer authentication.
- It validates tokens against `Authorization.Authority` and `Authorization.Audience`.
- Authorization is scope-based (space-separated `scope` claim):
  - Policy `mlApi` requires `Authorization.RequiredMetaScope` (default: `mlApi`).
  - Policy `serviceApi` requires `Authorization.RequiredServiceScope` (default: `serviceApi`).
- Entity-level rights for the editable `MlModel` API are resolved through the Meta Service metadata adapter and a Jint-based rights checker; tenant and user identity are taken from `TenantClaim`/`UserIdClaim`.
- Swagger UI is configured with an OpenID Connect security definition pointing to `Authority + /.well-known/openid-configuration`.

To use Swagger with auth:

1) Set `Authorization.Authority`, `Audience`, and `Swagger.ClientId` appropriately.
2) Open `https://localhost:6003/swagger` and pick either the ML or Service API.
3) Authorize via the OIDC button; scopes are taken from `Swagger.RequiredScopes`.


## Databases and migrations

- ML metadata DB: connection via the key named in `Storage.ConnectionString`.
- Quartz DB (when not using RAMJobStore): connection via `ConnectionStrings:QuartzConnection` with matching Quartz keys.
- Provider-specific EF Core migrations live in `Ballware.Ml.Data.Ef.Postgres`/`.SqlServer`; `Ballware.Ml.Data.Ef.*.Migrations` are excluded from Sonar analysis.
- If `Storage.AutoMigrations` is true, migrations are applied by an initialization worker on startup.


## Caching

- `Cache.CacheExpirationHours` controls the TTL of cached model artifacts (default 1 hour).
- Cached entries are keyed by tenant id and model identifier and are purged after retraining.


## Certificates and HTTPS (local development)

- `certificate/aspnet-dev-cert.pfx` is referenced by `appsettings.Development.json` but is git-ignored (`*.pfx`); generate your own dev certificate.
- Run `dotnet dev-certs https --trust` once on your machine.
- Prefer the `Https` Kestrel endpoint (system dev certificate) over `HttpsInlineCertAndKeyFile` unless you specifically need a bundled PFX.
- In Docker/production, provide certificates via mounting or your ingress/reverse proxy; the image exposes ports 5000 and 5001.


## Troubleshooting

Service exits at startup with "Required configuration for … is missing"

- `Authorization`, `Storage`, `MetaClient`, `StorageClient`, and `GenericClient` are all mandatory, as is the connection string referenced by `Storage.ConnectionString`. Add or rename the sections in `appsettings.local.json`.

JWT validation error IDX10500 / IDX10503

- The service could not download the issuer's JWKS, often due to HTTPS certificate validation issues with a self-signed issuer.
- Prefer a trusted certificate for your OIDC provider (mkcert/localhost certs) or import the issuer CA into your OS trust store; verify `Authorization.Authority` points at your realm.
- Note: there is no configuration switch to bypass issuer certificate validation for JWT metadata download. The DEBUG-only relaxation applies to outgoing service-to-service calls only.

Training stays `Queued` / never finishes

- Check that Quartz is configured (clustered ADO job store or `RAMJobStore`) and the `train`/`ml` job key exists; the endpoint only triggers an existing job.
- Inspect `TrainResult` on the model record – failures are recorded there as `Error` with the exception message.

Prediction fails with "Model with ID … not found" or missing artifact

- Verify the model exists for the calling tenant and that `TrainState` is `UpToDate`.
- Verify `StorageClient` can reach the Storage Service and that the model attachment (`Options.TrainingFileName`, default `model.zip`) is present.

Training returns no/invalid data

- `TrainSql` is executed as a plain query by the Generic Service for the current tenant; check the query, the feature field names/types in `Options`, and that `PredictionField` exists in the result set.

Swagger cannot authorize

- Make sure `Swagger.ClientId` exists in your IdP and `Swagger.RequiredScopes` match available scopes.
- Check CORS: allow the origin of your browser front-end if calling APIs directly.

NuGet restore fails for `Ballware.Shared.*`

- Add the GitHub feed with a PAT that has `read:packages` scope:
  - `dotnet nuget add source "https://nuget.pkg.github.com/ballware/index.json" --name github --username <user> --password <pat>`


## Docker

For a full dockerized environment (including Keycloak, Meta, Generic, Storage), please refer to https://github.com/ballware/ballware-docker-compose. That setup handles certificates and inter-service wiring.

This repository also includes a service `Dockerfile` under `src/Ballware.Ml.Service/` if you need a custom build:

```
docker build \
  --build-arg GITHUB_USERNAME=<user> \
  --build-arg GITHUB_PAT=<pat> \
  -t ballware-ml-service src/Ballware.Ml.Service
```

Published images are available at `ghcr.io/ballware/ballware-ml-service` (versioned tags from GitVersion), and library packages are pushed to the GitHub NuGet feed on `main`.


## Logging

- Serilog is configured via the `Serilog` section. Defaults log to console at Information level (Microsoft/System at Warning), enriched with log context and machine name.
- Request logging is enabled via Serilog request logging.
- AutoML training logs are captured separately and uploaded as the model's training log attachment when `Options.TrainingLogFileName` is set.


## FAQ

Q: Which model types are supported?

- A: Regression only (`MlModelTypes.Regression`) via ML.NET AutoML. Additional types require a new `IModelExecutor` implementation.

Q: Where are trained models stored?

- A: As attachment files in the Storage Service, owned by the model id (`mlmodel` entity). The ML Service keeps no model binaries locally; it caches them in memory per tenant.

Q: Can the ML Service run without Meta/Generic/Storage?

- A: No. Metadata/rights come from Meta, training data from Generic, model files from Storage; all three client sections are validated on startup.

Q: Is training synchronous?

- A: No. `POST /ml/mlmodel/train` enqueues a Quartz job and returns `202 Accepted`; poll the model's `TrainState`/`TrainResult` for progress.


## Development

- Run tests: `dotnet test`
- Database-backed test projects (`Ballware.Ml.Data.Ef.Postgres.Tests`, `Ballware.Ml.Data.Ef.SqlServer.Tests`) need reachable databases configured in their own `appsettings.local.json`; `Ballware.Ml.Api.Tests` covers mapping/endpoint behavior.
- Regenerate the client after API changes: run NSwag in `src/Ballware.Ml.Service.Client` (`nswag.json`, expects `swagger.json`).
- Typical inner loop: `dotnet run --project src/Ballware.Ml.Service`
- Versioning follows `GitVersion.yml` (`main` → patch, `develop`/`feature` → minor).


## License

MIT License. See `LICENSE` in this repository.

Copyright (c) 2025 ballware Software & Consulting, Frank Ballmeyer (https://www.ballware.de)
