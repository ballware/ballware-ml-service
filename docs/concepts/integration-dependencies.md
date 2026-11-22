# Integration dependencies

The Ml Service is not self-sufficient: model definitions and artifacts live in its own storage, but
authorization metadata, export/job records and training data come from the other ballware services.

## Outbound clients

Three OAuth2 client-credentials clients are registered by name (`src/Ballware.Ml.Service/Startup.cs:188-215`)
and attached to typed `HttpClient`s for the NSwag-generated service clients (`Startup.cs:217-251`):

| Client name | Config section | Client used for |
| --- | --- | --- |
| `meta` | `MetaClient` | `MetaServiceClient` — tenant/entity metadata, exports, jobs |
| `storage` | `StorageClient` | `StorageServiceClient` — model artifacts, import/export temp files |
| `generic` | `GenericClient` | `GenericServiceClient` — training data |

Each section requires `ServiceUrl`, `TokenEndpoint`, `ClientId`, `ClientSecret`, `Scopes`
(`src/Ballware.Ml.Service/Configuration/ServiceClientOptions.cs:5-21`) and is validated on startup with
`ValidateOnStart` (`Startup.cs:81-91`). A missing section raises `ConfigurationException`
(`Startup.cs:105-118`) and the process exits with code `-1` (`Program.cs:29-37`) — the service never
starts half-configured.

In `DEBUG` builds the three clients accept any server certificate
(`Startup.cs:221-226, 233-238, 245-250`); release builds validate normally.

## Meta Service

`src/Ballware.Ml.Service/Adapter/MetaServiceMetadataAdapter.cs` implements three shared contracts and
is registered as `IAuthorizationMetadataProvider`, `IJobMetadataProvider` and
`IExportMetadataProvider` (`Startup.cs:264-266`).

| Adapter method | Meta operation | Used by |
| --- | --- | --- |
| `MetadataForTenantByIdAsync` (`:20-25`) | `TenantServiceMetadataAsync` | tenant-level rights check of every editing operation |
| `MetadataForEntityByTenantAndIdentifierAsync` (`:27-32`) | `EntityServiceMetadataForTenantByIdentifierAsync` (`mlmodel`) | entity-level rights check (`CustomScripts.extendedRightsCheck`) |
| `CreateExportAsync` / `GetExportByIdAsync` (`:34-59`) | `ExportCreateForTenantBehalfOfUserAsync` / `ExportFetchForTenantByIdAsync` | `exporturl` and the anonymous `download/{tenantId}/{id}` |
| `CreateJobAsync` / `UpdateJobAsync` (`:61-86`) | `JobCreateForTenantBehalfOfUserAsync` / `JobUpdateForTenantBehalfOfUserAsync` | the shared `import` job lifecycle |

Consequence for the API: the editing operations need a `meta` tenant and a `meta.mlmodel` entity for
the tenant, otherwise the rights check short-circuits with `404`
(see [tenancy-and-authorization.md](tenancy-and-authorization.md#script-based-enforcement-the-real-control-point)).
Mapping of Meta payloads onto the local contracts is done by
`Mappings/MetaServiceMlMetadataProfile.cs:10-16` (`CustomScripts.ExtendedRightsCheck` →
`EntityMetadata.RightsCheckScript`, plus the job-state enums).

## Generic Service

Training rows are fetched through `ITenantDataAdapter` → `GenericServiceTenantDataAdapter`
(`src/Ballware.Ml.Service/Adapter/GenericServiceTenantDataAdapter.cs:15-18`, registered at
`Startup.cs:267`):

```text
AutoMlExecutor.cs:70  MlModelTrainingdataByTenantAndPlainQueryAsync(tenantId, TrainSql)
  → GenericServiceClient.MlModelTrainingDataByTenantAndPlainQueryAsync
  → POST {GenericServiceUrl}/tenent/mlmodel/trainingdatabytenantandplainquery/{tenantId}
     Content-Type: application/json
     body: a JSON *string* containing the plain query (TrainSql)
  → 200 JSON array of row objects
```

The Generic Service resolves the tenant, runs the query against the tenant's business data source and
returns rows as untyped JSON objects (`ballware-generic-service/src/Ballware.Generic.Api/Endpoints/MlModelDataEndpoint.cs:20-46`).
Rows are converted into the dynamically compiled training type via `JObject.ToToObject<T>()`
(`AutoMlExecutor.cs:65-75`), so **column names in the query result must match
`Options.featureFields[].name` and `predictionField.name` exactly**.

## Storage Service

`StorageServiceFileStorageAdapter` implements both `IAutoMlFileStorageAdapter` (model artifacts) and
`IFileStorageProvider` (import/export temp files), registered at `Startup.cs:268-269`
(`src/Ballware.Ml.Service/Adapter/StorageServiceFileStorageAdapter.cs`):

| Adapter method | Storage operation | Used by |
| --- | --- | --- |
| `UploadAttachmentFileForOwnerAsync` (`:24-27`) | `AttachmentUploadForTenantEntityAndOwnerBehalfOfUserAsync` | training: model + AutoML log (`AutoMlExecutor.cs:103-106, 146-148`) |
| `AttachmentFileByNameForOwnerAsync` (`:16-22`) | `AttachmentDownloadForTenantEntityAndOwnerByFilenameAsync` | prediction: load model (`AutoMlExecutor.cs:199-200`) |
| `UploadTemporaryFileBehalfOfUserAsync` (`:34-40`) | `TemporaryUploadForTenantAndIdBehalfOfUserAsync` | `import`, `exporturl` |
| `TemporaryFileByIdAsync` (`:29-32`) | `TemporaryDownloadForTenantByIdAsync` | `download/{tenantId}/{id}`, import job |
| `RemoveTemporaryFileByIdBehalfOfUserAsync` (`:42-45`) | `TemporaryDropForTenantAndIdBehalfOfUserAsync` | import job cleanup |

Attachments are keyed by `(entity = "mlmodel", ownerId = model uuid, fileName)` — see
[model-lifecycle.md](model-lifecycle.md#artifacts).

## Local storage

`Storage.Provider` selects the EF provider (`Startup.cs:271-279`), `Storage.ConnectionString` names a
connection string entry (`Startup.cs:98`). Provider packages register `MlDbContext`,
`IMlDbContext`, `ITenantableRepository<MlModel>`, `IMlModelMetaRepository`, `IMlDbConnectionFactory`
and the migration worker (`Postgres/ServiceCollectionExtensions.cs:16-39`,
`SqlServer/ServiceCollectionExtensions.cs`):

- Postgres uses the snake-case naming convention (`Postgres/ServiceCollectionExtensions.cs:26`).
- Both providers store model rows in `ml_model` (`MlModelBaseCustomizer.cs:37`).

`IMlDbConnectionFactory` exposes the configured provider and connection string to
`/ml/tenant/reportmldatasourcesfortenant/{tenantId}` (`TenantEndpoint.cs:42-43`).

## Security handling of outbound payloads

- Client secrets are ordinary configuration values (`ServiceClientOptions.cs:15-17`); keep them out of
  committed files — the repository pattern is `appsettings.local.json`, which is git-ignored
  (`.gitignore:487`).
- `GET /ml/tenant/reportmldatasourcesfortenant/{tenantId}` returns the ML storage **connection string**
  verbatim plus a generated lookup query (`TenantEndpoint.cs:47-56`,
  `Postgres/Repository/MlModelRepository.cs:15-18`,
  `SqlServer/Repository/MlModelRepository.cs:15-18`). It is guarded only by the `serviceApi` scope —
  treat that scope as privileged and never log or cache the response body. Details in
  [../api/service-api.md](../api/service-api.md#security-note).
