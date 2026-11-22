# Model lifecycle

A model is a row in `ml_model` plus a model artifact in the Storage Service. Everything the engine
needs to train or predict is contained in that row.

## Model definition

Public contract `src/Ballware.Ml.Data/Public/MlModel.cs:6-21` (field tables in
[../api/schemas.md](../api/schemas.md)):

| Field | Meaning |
| --- | --- |
| `Id` | model uuid (maps to `Uuid`, not to the surrogate `long` id — `StorageMappingProfile.cs:9-14`) |
| `Identifier` | tenant-unique model name; also the prediction cache key |
| `Type` | engine selector, `MlModelTypes` (`src/Ballware.Ml.Data/Common/MlModelTypes.cs:3-6`); only `1` = `Regression` is implemented |
| `TrainSql` | plain query handed to the Generic Service to fetch training rows |
| `TrainState` | `MlModelTrainingStates` (`src/Ballware.Ml.Data/Common/MlModelTrainingStates.cs:3-10`) |
| `TrainResult` | embedded JSON string written by the training run |
| `Options` | embedded JSON string describing features, prediction field and artifact names |

`Options` is parsed with Newtonsoft.Json (`AutoMlExecutor.cs:56, 197`,
`AutoMlModelBuilder.cs:23, 159, 185, 214`) into `ModelOptions` / `RegressionOptions`
(`src/Ballware.Ml.Metadata/ModelOptions.cs:3-7`, `RegressionOptions.cs:3-6`, `RegressionField.cs:3-6`).
See [../best-practices/model-definition.md](../best-practices/model-definition.md) for templates.

## TrainState machine

| Value | State | Set by |
| --- | --- | --- |
| `0` | `Unknown` | nothing — initial/default value |
| `1` | `Outdated` | metadata convention only (a tenant's `meta.mlmodel` `SaveStatement` commonly sets it on save); the service itself never writes it |
| `5` | `Queued` | `POST /ml/mlmodel/train` (`MlModelEndpoint.cs:154-161`) |
| `6` | `InProgress` | job start (`AutoMlExecutor.cs:37-43`) |
| `10` | `UpToDate` | successful experiment (`AutoMlExecutor.cs:111-124`) |
| `99` | `Error` | failed experiment (`AutoMlExecutor.cs:128-137`) |

State transitions go through `MlModelBaseRepository.SaveTrainingStateAsync`
(`src/Ballware.Ml.Data.Ef/Repository/MlModelBaseRepository.cs:37-54`), which also stamps
`LastChangerId`/`LastChangeStamp`.

## Training flow

```text
POST /ml/mlmodel/train?id=<modelId>
  MlModelEndpoint.cs:129-168
  ├─ TrainState = Queued, TrainResult = ""          SaveTrainingStateAsync
  └─ TriggerJob(JobKey("train","ml"), jobData)      MlModelEndpoint.cs:163
     jobData: every query parameter + tenantId, userId, modelId   MlModelEndpoint.cs:143-152
  → 202 Accepted
ModelTrainJob.Execute                               Ballware.Ml.Jobs/Internal/ModelTrainJob.cs:17-32
  → AutoMlExecutor.TrainAsync                       AutoMlExecutor.cs:33-149
     ├─ TrainState = InProgress                     L37-43
     ├─ read metadata + Options                     L54-56
     ├─ compile input type / schema / column info   L58-60 (AutoMlModelBuilder.cs)
     ├─ training rows via Generic Service query     L70 (TrainSql)
     ├─ AutoML featurizer + regression experiment   L83-91
     │    metric: R², trainingTime default 60 s
     ├─ success: save artifact → Storage            L97-107
     │           purge prediction cache             L109
     │           TrainState = UpToDate + TrainResult L111-124
     ├─ failure: TrainState = Error + TrainResult   L128-137
     └─ AutoML log → Storage (if requested)         L140-148
```

Details worth knowing:

- The endpoint annotation declares `201 Created` (`MlModelEndpoint.cs:57`), but the handler always
  returns `Results.Accepted()` → `202` (`MlModelEndpoint.cs:138, 167`). Verified by
  `test/Ballware.Ml.Api.Tests/MlModel/MlModelConsumeAndTrainBehaviorTest.cs`. **`202 Accepted` is the
  actual behavior**; the snapshot keeps the declared `201`.
- `train` is repeatable and takes its model ids from the **query string** (`?id=<guid>`; multiple
  `id` parameters queue one job each). A value that is not a GUID throws inside the handler and
  surfaces as `500` (`MlModelEndpoint.cs:152`). The snapshot shows no parameters for `train` because
  the values are bound through `QueryValueBag` (`MlModelEndpoint.cs:129`), which is not visible to the
  API explorer.
- The `Swagger` environment uses a RAM job store (`appsettings.Swagger.json:21-23`); production uses
  the clustered ADO store (`appsettings.json:32-38`), so a job triggered by one instance can run on
  another.
- Job failures are wrapped in `JobExecutionException(refireImmediately: false)`
  (`ModelTrainJob.cs:27-31`); the model row still carries the `Error` state and message.

## Column classification and typing

`AutoMlModelBuilder.BuildColumnInformation` (`AutoMlModelBuilder.cs:15-58`) classifies each feature by
`FieldTypes` (`src/Ballware.Ml.Metadata/FieldTypes.cs:3-12`):

| `FieldTypes` | Role in AutoML | CLR property type | DataView type |
| --- | --- | --- | --- |
| `0 Bool` | categorical | `bool` | `Boolean` |
| `1 Int` | numeric | `float` | `Single` |
| `2 Decimal` | numeric | `decimal` | `Single` |
| `3 Double` | numeric | `double` | `Double` |
| `4 String` | categorical | `string` | `Text` |
| `5 Date`, `6 Datetime`, `7 Time` | **ignored** | `DateTime` | `DateTime` |

`Int` and `Decimal` become single-precision `Single` columns in the data view
(`AutoMlModelBuilder.cs:89-94`); date-like fields are dropped from the pipeline
(`AutoMlModelBuilder.cs:44-48`).

## Artifacts

Model binary and log land as **attachments of entity `mlmodel`, owned by the model uuid**
(`AutoMlExecutor.cs:103-106, 146-148`) in the Storage Service:

| Artifact | Filename | Media type |
| --- | --- | --- |
| model | `Options.trainingFileName`, default `model.zip` | `application/binary` |
| AutoML log | `Options.trainingLogFileName`, only when set (no default) | `application/binary` |

Prediction loads the same attachment by name (`AutoMlExecutor.cs:199-200`). Changing
`trainingFileName` on an existing model therefore hides the previously trained artifact until the
model is retrained.

## Prediction flow

```text
PredictAsync(tenantId, modelId | identifier, values)     AutoMlExecutor.cs:151-193
  ├─ metadata lookup (by uuid or identifier)             MlModelBaseRepository.cs:22-35
  ├─ cache lookup key "<tenantId>_<identifier>"          InMemoryTenantAwareModelCache.cs:14-16
  ├─ miss → load artifact + compile input/output types   AutoMlExecutor.cs:195-226
  ├─ store prediction engine (Cache.CacheExpirationHours, default 1 h)
  │                                                       CacheOptions.cs:5, InMemoryTenantAwareModelCache.cs:47-55
  └─ Predict: build input instance, engine.Predict        AutoMlExecutor.cs:228-242
```

- The cache holds the compiled **prediction engine**, not results, and is keyed per
  tenant + model identifier (`InMemoryTenantAwareModelCache.cs:14-16`). Successful training purges the
  entry (`AutoMlExecutor.cs:109`); a failed run does not.
- Input values are matched to `featureFields[].name` and converted per declared `FieldTypes`
  (`AutoMlModelBuilder.cs:152-175`, `ParseValue` at `129-150`). Missing feature keys are simply
  skipped, so the compiled instance keeps its default values (`AutoMlModelBuilder.cs:161-171`).
- Output echoes every feature field plus `Score` (float); the ML.NET `Features` vector is marked
  `JsonIgnore` and never appears in responses (`AutoMlModelBuilder.cs:206-239`).

### Known limitation: GET prediction input binding

`GET /ml/mlmodel/consumebyid/{modelId}` binds its feature values through `QueryValueBag`, which stores
each query key as a **`string[]`** (`src/Ballware.Ml.Api/Endpoints/QueryValueBag.cs:13`), while
`CreateInput` converts values with `value.ToString()` (`AutoMlModelBuilder.cs:169`). A `string[]`
stringifies to `"System.String[]"`, so typed fields fail to parse (`ParseValue`,
`AutoMlModelBuilder.cs:129-148`) and string fields receive the type name instead of the value.
Verified by `test/Ballware.Ml.Api.Tests/MlModel/MlModelConsumeAndTrainBehaviorTest.cs`.

**Use the POST service-API variants for prediction** — `BodyValueBag`
(`src/Ballware.Ml.Api/Endpoints/BodyValueBag.cs:11-54`) binds JSON scalars with real types
(`long`, `double`, `bool`, `string`), which `CreateInput` converts correctly. Fixing the query binding
is out of scope here.
