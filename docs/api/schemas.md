# Schemas

Component schemas exactly as they appear in the committed snapshots
([openapi/ml.json](../openapi/ml.json), [openapi/service.json](../openapi/service.json)), plus the
payloads that are embedded **inside JSON strings**.

## `ml` document (6 schemas)

### `MlModel`

`src/Ballware.Ml.Data/Public/MlModel.cs:6-21` — the editable model definition.

| Field | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | string (`format: uuid`) | no | maps to `Uuid` of the persisted entity, not the surrogate `long` key (`StorageMappingProfile.cs:9-14`) |
| `Identifier` | string | yes | unique per tenant (`MlModelBaseCustomizer.cs:41`); cache key for predictions |
| `Type` | `MlModelTypes` | no | engine selector |
| `TrainSql` | string | yes | plain query executed by the Generic Service |
| `TrainState` | `MlModelTrainingStates` | no | read-only in practice — written by the training flow |
| `TrainResult` | string | yes | embedded JSON, see below |
| `Options` | string | yes | embedded JSON, see below |

`additionalProperties: false`.

### `MlModelSelectListEntry`

`src/Ballware.Ml.Data/SelectLists/MlModelSelectListEntry.cs:3-6` — responses of `selectlist` and
`selectbyid`.

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `Id` | string (`format: uuid`) | yes | model uuid |
| `Identifier` | string | yes (schema) | declared `required` because the C# property is non-nullable, yet the mapping source is nullable — treat it as optional at runtime |

### `MlModelTypes`

`src/Ballware.Ml.Data/Common/MlModelTypes.cs:3-6`, serialized as `int32`
(`src/Ballware.Ml.Service/Startup.cs:168-176` — no string-enum converter):

| Value | Name | Supported |
| --- | --- | --- |
| `0` | `Undefined` | no — training/prediction produce no columns and fail |
| `1` | `Regression` | yes (only implemented type, `AutoMlModelBuilder.cs:19-54`) |

### `MlModelTrainingStates`

`src/Ballware.Ml.Data/Common/MlModelTrainingStates.cs:3-10`, `int32`:

| Value | Name | Meaning |
| --- | --- | --- |
| `0` | `Unknown` | never trained |
| `1` | `Outdated` | definition changed (metadata convention; not written by the service) |
| `5` | `Queued` | job triggered, not started |
| `6` | `InProgress` | training running |
| `10` | `UpToDate` | artifact available |
| `99` | `Error` | last run failed, see `TrainResult` |

### `ExportUrlResult`

Produced by `exporturl` (`TenantableEndpointHandlerFactory.cs:281-286`):

| Field | Type | Notes |
| --- | --- | --- |
| `TenantId` | string (`format: uuid`) | needed for the anonymous download path |
| `Id` | string (`format: uuid`) | export id → `GET /ml/mlmodel/download/{tenantId}/{id}` |

### `StringStringValuesKeyValuePair`

Request-body item schema of `exporturl` (`TenantantableEditingEndpoint.cs:116`,
`Accepts<IFormCollection>`):

| Field | Type | Notes |
| --- | --- | --- |
| `Key` | string | form field name |
| `Value` | string[] | form field values; multi-value fields stay arrays |

Each pair becomes a query parameter of the export definition
(`TenantableEndpointHandlerFactory.cs:254-261`).

## `service` document (3 schemas)

### `ReportDatasourceDefinition`

`src/Ballware.Ml.Data/Public/ReportDatasource.cs:19-25` — response of
`reportmldatasourcesfortenant/{tenantId}`.

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `Name` | string | yes | `MlLookups` (`TenantEndpoint.cs:17`) |
| `Provider` | string | yes | `postgres` or `mssql`, from `Storage.Provider` (`TenantEndpoint.cs:42`) |
| `ConnectionString` | string | yes | **sensitive** — live ML storage connection string (`TenantEndpoint.cs:43`), see [service-api.md](service-api.md#security-note) |
| `Tables` | `ReportDatasourceTable[]` | no | one entry: `mlmodelLookup` |

### `ReportDatasourceTable`

`ReportDatasource.cs:3-9`

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `Name` | string | yes | `mlmodelLookup` (`TenantEndpoint.cs:54`) |
| `Query` | string | yes | provider-specific generated list query (`Postgres/Repository/MlModelRepository.cs:17`, `SqlServer/Repository/MlModelRepository.cs:17`) |
| `Relations` | `ReportDatasourceRelation[]` | no | empty for the ML lookups datasource |

### `ReportDatasourceRelation`

`ReportDatasource.cs:11-17` — declared, never populated by the Ml Service.

| Field | Type | Required |
| --- | --- | --- |
| `Name` | string | yes |
| `ChildTable` | string | yes |
| `MasterColumn` | string | yes |
| `ChildColumn` | string | yes |

## Embedded payload 1 — `MlModel.Options` (JSON string)

Parsed with Newtonsoft.Json (`AutoMlExecutor.cs:56, 197`; `AutoMlModelBuilder.cs:23, 159, 185, 214`)
into `Ballware.Ml.Metadata.ModelOptions` / `RegressionOptions` / `RegressionField`. Key casing is
**camelCase**, matching the C# property names of those classes (Newtonsoft default).

| Key | Type | Default when absent | Used by |
| --- | --- | --- | --- |
| `trainingTime` | number (seconds) | `60` (`AutoMlExecutor.cs:90`) | AutoML experiment budget |
| `trainingFileName` | string | `model.zip` (`AutoMlExecutor.cs:104-106, 200`) | attachment name of the model artifact |
| `trainingLogFileName` | string | none → **no log uploaded** (`AutoMlExecutor.cs:140`) | attachment name of the AutoML log |
| `featureFields` | `RegressionField[]` | – | feature columns (`RegressionOptions.cs:5`) |
| `predictionField` | `RegressionField` | – | label column (`RegressionOptions.cs:6`, `AutoMlModelBuilder.cs:52`) |

`RegressionField` (`src/Ballware.Ml.Metadata/RegressionField.cs:3-6`):

| Key | Type | Notes |
| --- | --- | --- |
| `name` | string | must match the column name produced by `TrainSql` and the prediction input key |
| `type` | integer | `FieldTypes` (`src/Ballware.Ml.Metadata/FieldTypes.cs:3-12`): `0` Bool, `1` Int, `2` Decimal, `3` Double, `4` String, `5` Date, `6` Datetime, `7` Time |

Classification and CLR/DataView types per `type`: [model-lifecycle.md](../concepts/model-lifecycle.md#column-classification-and-typing).

## Embedded payload 2 — `MlModel.TrainResult` (JSON string)

Success shape (`src/Ballware.Ml.Engine.AutoMl/Internal/AutoMlExecutor.cs:111-124`,
PascalCase from the anonymous object serializer):

| Field | Type | Meaning |
| --- | --- | --- |
| `TrainingRecordCount` | number | rows fetched from the Generic Service |
| `Loss` | number | AutoML loss |
| `Metric` | number | the R² metric (`AutoMlExecutor.cs:88-89`) |
| `DurationInMilliseconds` | number | experiment run time |
| `PeakCpu` | number | peak CPU reported by AutoML |
| `PeakMemoryInMegaByte` | number | peak memory in MB |

Error shape (`AutoMlExecutor.cs:128-137`):

| Field | Type | Meaning |
| --- | --- | --- |
| `TrainingRecordCount` | number | rows fetched before the failure |
| `Exception` | string | exception message |

`TrainState` decides which shape to expect: `10` → success, `99` → error. `Queued`/`InProgress` keep an
empty string (`MlModelEndpoint.cs:154-159`, `AutoMlExecutor.cs:37-43`).

## Prediction input / output (no component schema)

The prediction routes declare `Produces<object>()` / `Accepts<Dictionary<string, object>>`
(`MlModelEndpoint.cs:47, 78-79, 87-90`), so the shape is defined by the dynamically compiled types
(`AutoMlModelBuilder.cs:177-239`):

- **Input**: one key per `featureFields[].name`; missing keys fall back to type defaults
  (`AutoMlModelBuilder.cs:161-171`). The label field is *not* part of the input.
- **Output**: every feature field echoed with its CLR type + `Score` (`float`,
  `AutoMlModelBuilder.cs:230-233`). The internal `Features` (`float[]`) vector is `JsonIgnore`d and never
  serialized (`AutoMlModelBuilder.cs:224-228`).

## Storage-side entity (not an API schema)

`src/Ballware.Ml.Data/Persistables/MlModel.cs:6-33` adds `Id` (`long` surrogate), `Uuid`, `TenantId`,
`CreatorId`, `CreateStamp`, `LastChangerId`, `LastChangeStamp` — table `ml_model`, none of them exposed
through the API schemas.
