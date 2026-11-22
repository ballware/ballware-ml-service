# Architecture

## Projects

| Project | Role |
| --- | --- |
| `src/Ballware.Ml.Service` | Web service host: configuration binding, DI wiring, route mounting, Swagger (`Startup.cs`, `Program.cs`) |
| `src/Ballware.Ml.Api` | Endpoint mapping and handlers: `Endpoints/MlModelEndpoint.cs`, `Endpoints/TenantEndpoint.cs`, payload binders `QueryValueBag`/`BodyValueBag` |
| `src/Ballware.Ml.Data` | Provider-independent contracts: `Public/MlModel.cs` (API DTO), `Persistables/MlModel.cs` (EF entity), enums, repository interfaces |
| `src/Ballware.Ml.Data.Ef` | Shared EF Core base: `Repository/MlModelBaseRepository.cs`, mapping profile, model customizer |
| `src/Ballware.Ml.Data.Ef.Postgres` / `.SqlServer` | Provider storage: `MlDbContext`, `MlDbConnectionFactory`, migrations, `InitializationWorker` (auto-migrations) |
| `src/Ballware.Ml.Engine` | `IModelExecutor` abstraction (train/predict) |
| `src/Ballware.Ml.Engine.AutoMl` | ML.NET AutoML implementation: `Internal/AutoMlExecutor.cs`, dynamic input/output types `Internal/AutoMlModelBuilder.cs` |
| `src/Ballware.Ml.Jobs` | Quartz jobs: `Internal/ModelTrainJob.cs` plus the shared import job |
| `src/Ballware.Ml.Caching` | Tenant-aware in-memory prediction cache |
| `src/Ballware.Ml.Metadata` | Metadata contracts: `ModelOptions`, `RegressionOptions`, `RegressionField`, `FieldTypes`, `ITenantDataAdapter` |
| `src/Ballware.Ml.Service/Adapter` | Outbound adapters for Meta / Generic / Storage |
| `src/Ballware.Ml.Service.Client` | NSwag-generated client, built from the `service` document only (see [openapi/README.md](../openapi/README.md)) |

Shared plumbing comes from `ballware-shared-dotnet` packages: the generic editing endpoints
(`Ballware.Shared.Api.Endpoints`), the Jint rights checkers (`Ballware.Shared.Authorization.Jint`),
and the tenantable EF repository base (`Ballware.Shared.Data.Ef`).

## Composition order

`Program.cs:9-27` builds the host; `Startup.InitializeServices` (`Startup.cs:54-320`) registers
everything before the app is built.

1. **Configuration** — options with `ValidateOnStart` for `Authorization`, `Storage`, `Cache`,
   `Swagger`, `MetaClient`, `StorageClient`, `GenericClient` (`Startup.cs:65-91`), then explicit
   presence checks that throw `ConfigurationException` for authorization, storage, connection string
   and the three clients (`Startup.cs:93-118`). `Program.cs:29-37` prints the message and exits `-1`.
   Configuration file order: `appsettings.json` → `appsettings.{Environment}.json` →
   `appsettings.local.json` → environment variables (`Program.cs:19-23`).
2. **Authentication** — JWT bearer, inbound claim mapping disabled (`Startup.cs:123-137`).
3. **Authorization policies** — `mlApi` and `serviceApi` (`Startup.cs:139-153`), see
   [tenancy-and-authorization.md](tenancy-and-authorization.md).
4. **JSON conventions** — System.Text.Json naming policy disabled for MVC and HTTP JSON options
   (`Startup.cs:168-176`): payloads are PascalCase.
5. **Outbound clients** — OAuth2 client-credentials clients `meta`, `storage`, `generic` and typed
   `HttpClient`s (`Startup.cs:188-251`), see [integration-dependencies.md](integration-dependencies.md).
6. **Mapping** — Mapster: storage mappings (`StorageMappingProfile`) plus the Meta metadata profile
   (`Startup.cs:253-259`, `Mappings/MetaServiceMlMetadataProfile.cs`).
7. **Contracts → adapters** — Jint rights checkers, shared API dependencies, Meta adapter
   (authorization/export/job metadata), Generic adapter (tenant training data), Storage adapter
   (files) (`Startup.cs:261-269`).
8. **Storage provider** — chosen by `Storage.Provider`: `mssql` or `postgres`, both requiring a
   resolved connection string (`Startup.cs:271-279`); an unknown provider leaves the storage services
   unregistered.
9. **Cache + engine + Swagger explorer** (`Startup.cs:281-285`), Swagger documents
   (`Startup.cs:287-319`).

## Mounted routes

`Startup.cs:362-366`:

| Mount | Group | Policy | Operations |
| --- | --- | --- | --- |
| `MapMlModelUserApi("/ml/mlmodel")` (`Startup.cs:362`) | `ml` | `mlApi` | 4 — see [../api/ml-api.md](../api/ml-api.md) |
| `MapMlModelServiceApi("/ml/mlmodel")` (`Startup.cs:363`) | `service` | `serviceApi` | 2 — see [../api/service-api.md](../api/service-api.md) |
| `MapTenantableEditingApi<Data.Public.MlModel>("/ml/mlmodel", "meta", "mlmodel", "MlModel", "MlModel", "mlApi", "ml")` (`Startup.cs:364`) | `ml` | `mlApi` | 11 — see [../api/generic-crud.md](../api/generic-crud.md) |
| `MapMlTenantServiceApi("/ml/tenant")` (`Startup.cs:366`) | `service` | `serviceApi` | 1 — see [../api/service-api.md](../api/service-api.md) |

The editing mount's `application`/`entity` pair (`"meta"`, `"mlmodel"`) is the **rights catalog key**
(`meta.mlmodel.<right>`), not a hint that the data lives in the Meta Service. Model definitions are
stored in this service's own database.

Both documents are exposed through `MapSwagger` + `UseSwagger`, with the Swagger UI client wired from
`Swagger.*` (`Startup.cs:371-390`).

## Storage layout

`Persistables.MlModel` (`src/Ballware.Ml.Data/Persistables/MlModel.cs:6-33`) is mapped by
`MlModelBaseCustomizer` (`src/Ballware.Ml.Data.Ef/Model/MlModelBaseCustomizer.cs:37-41`):

- table `ml_model` (snake-case naming convention, e.g. `Postgres/ServiceCollectionExtensions.cs:26`)
- primary key `Id` (`long` surrogate) plus `Uuid` (`Guid`) — the tenant-facing identifier
- unique indexes `(TenantId, Uuid)` and `(TenantId, Identifier)` → a model `Identifier` is unique per
  tenant (`MlModelBaseCustomizer.cs:39-41`)
- all `DateTime` columns are normalized to UTC by a value converter (`MlModelBaseCustomizer.cs:21-33`)

API payloads expose `Id` as the `Uuid` only — `StorageMappingProfile` ignores the surrogate `Id` in one
direction and maps `Uuid` → `Id` in the other
(`src/Ballware.Ml.Data.Ef/Mapping/StorageMappingProfile.cs:9-14`).

Migrations run automatically only when `Storage.AutoMigrations` is `true`, via the provider
`InitializationWorker` (`Postgres/Internal/InitializationWorker.cs:17-29`).

## Error contract

A global exception handler converts unhandled exceptions into RFC 7807
(`src/Ballware.Ml.Service/Startup.cs:330-355`):

```http
HTTP/1.1 500 Internal Server Error
Content-Type: application/problem+json

{
  "type": "https://httpstatuses.com/500",
  "title": "An unexpected error occurred.",
  "status": 500,
  "detail": "<exception text — development environments only>",
  "instance": "/ml/mlmodel/selectlist"
}
```

`Detail` is populated only in Development (`Startup.cs:346`). Expected per-operation statuses are
tabulated in [tenancy-and-authorization.md](tenancy-and-authorization.md#expected-status-codes).

## Background jobs

`AddBallwareMlBackgroundJobs` (`src/Ballware.Ml.Jobs/ServiceCollectionExtensions.cs:12-28`) registers
two durably stored Quartz jobs and starts an in-process Quartz server:

| Job key | Class | Triggered by |
| --- | --- | --- |
| `train` / group `ml` | `Internal/ModelTrainJob.cs` | `POST /ml/mlmodel/train` (`MlModelEndpoint.cs:163`) |
| `import` / group `mlmodel` | shared `TenantableImportJob<Data.Public.MlModel, …>` | `POST /ml/mlmodel/import` (shared factory, `TenantableEndpointHandlerFactory.cs:212`) |

The production Quartz configuration uses a clustered ADO job store (`appsettings.json:32-38`); the
`Swagger` environment uses a RAM job store (`appsettings.Swagger.json:21-23`).

## Not part of the API documents

- EF migration assets and the initialization worker.
- The prediction cache (`Ballware.Ml.Caching`) — internal, no endpoints.
- The NSwag client `Ballware.Ml.Service.Client` — generated from the `service` document
  (`.github/workflows/publish-packages.yml:50-62`).
