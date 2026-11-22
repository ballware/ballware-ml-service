# Ml user API (`ml` document, policy `mlApi`)

Mounted by `MapMlModelUserApi("/ml/mlmodel")` (`Startup.cs:362`,
`src/Ballware.Ml.Api/Endpoints/MlModelEndpoint.cs:19-65`). All four operations derive the tenant from
the caller's `tenant` claim (`MlModelEndpoint.cs:101, 107, 122, 131`) and are gated by the endpoint
policy only — no `meta.mlmodel.*` right is evaluated here
(see [concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md#endpoint-policy-only-vs-rights-checks)).

## Overview

| # | Method | Path | Operation id | Success | Notes |
| --- | --- | --- | --- | --- | --- |
| 1 | GET | `/ml/mlmodel/selectlist` | `MlModelSelectList` | `200`, array of `MlModelSelectListEntry` | `MlModelEndpoint.cs:26-33` |
| 2 | GET | `/ml/mlmodel/selectbyid/{id}` | `MlModelSelectById` | `200`, `MlModelSelectListEntry`; `404` | `MlModelEndpoint.cs:35-43` |
| 3 | GET | `/ml/mlmodel/consumebyid/{modelId}` | `MlModelConsumeById` | `200`, prediction object | `MlModelEndpoint.cs:45-52` |
| 4 | POST | `/ml/mlmodel/train` | `MlModelTrain` | **`202`** (declared `201`) | `MlModelEndpoint.cs:54-62` |

None of them reads or writes `Options`, `TrainSql` or `TrainResult` — model definitions are edited
through [generic-crud.md](generic-crud.md).

## 1. `GET /ml/mlmodel/selectlist`

Returns the tenant's models as lightweight entries for pickers, ordered by `Identifier`
(`MlModelEndpoint.cs:99-104` → `MlModelBaseRepository.cs:56-62`).

```json
[
  { "Id": "6f2c1e2a-0000-4000-8000-000000000001", "Identifier": "daytime-personal-count" },
  { "Id": "9a4b7c1d-0000-4000-8000-000000000002", "Identifier": "vehicle-response-time" }
]
```

## 2. `GET /ml/mlmodel/selectbyid/{id}`

`{id}` is the model **uuid** (path, required). `404` when the tenant has no model with that uuid
(`MlModelEndpoint.cs:106-118`, `MlModelBaseRepository.cs:64-70`).

```bash
curl -s "https://localhost:6003/ml/mlmodel/selectbyid/6f2c1e2a-0000-4000-8000-000000000001" \
  -H "Authorization: Bearer $TOKEN"
```

```json
{ "Id": "6f2c1e2a-0000-4000-8000-000000000001", "Identifier": "daytime-personal-count" }
```

## 3. `GET /ml/mlmodel/consumebyid/{modelId}`

Prediction by model uuid. Feature values are **query parameters named exactly like
`Options.featureFields[].name`** (`MlModelEndpoint.cs:120-127` → `AutoMlExecutor.cs:151-171` →
`AutoMlModelBuilder.cs:152-175`):

```bash
curl -s "https://localhost:6003/ml/mlmodel/consumebyid/6f2c1e2a-0000-4000-8000-000000000001?Day=3&Hour=7" \
  -H "Authorization: Bearer $TOKEN"
```

Response echoes the feature fields and adds `Score` (`float`); the internal `Features` vector is
`JsonIgnore`d (`AutoMlModelBuilder.cs:206-239`):

```json
{ "Day": 3, "Hour": 7, "Score": 12.4 }
```

Known limitation: `QueryValueBag` binds each key as `string[]` (`QueryValueBag.cs:13`) and the engine
stringifies values with `value.ToString()` (`AutoMlModelBuilder.cs:169`), so typed features fail to
parse through this route — `Int`/`Double`/`Bool` features produce `500` (parse failure) and `String`
features receive `"System.String[]"` as value. Verified in
`test/Ballware.Ml.Api.Tests/MlModel/MlModelConsumeAndTrainBehaviorTest.cs`.
**For real predictions use the POST service-API variants** ([service-api.md](service-api.md)), which
bind typed JSON values (`BodyValueBag.cs:11-54`).

`500` (`ArgumentException`) when the model uuid does not exist for the tenant
(`AutoMlExecutor.cs:155-158`) — this route does not return `404`.

## 4. `POST /ml/mlmodel/train`

Queues training for one or more models. Model ids come from the **query string**, not the body:

```bash
curl -s -i -X POST \
  "https://localhost:6003/ml/mlmodel/train?id=6f2c1e2a-0000-4000-8000-000000000001" \
  -H "Authorization: Bearer $TOKEN"
```

Behavior (`MlModelEndpoint.cs:129-168`):

- `?id=<guid>` is required for any work to happen; without it the handler still returns `202` and does
  nothing (`MlModelEndpoint.cs:134-138, 167`).
- Repeated `id` parameters queue one Quartz job each (`MlModelEndpoint.cs:141-164`), each job setting
  `TrainState = Queued` (`5`) and clearing `TrainResult` first (`MlModelEndpoint.cs:154-161`).
- Every query parameter is copied into the job data map alongside `tenantId`, `userId`, `modelId`
  (`MlModelEndpoint.cs:145-152`) — extra parameters are forwarded but unused by `ModelTrainJob`
  (`Ballware.Ml.Jobs/Internal/ModelTrainJob.cs:21-23`).
- **`202 Accepted` with an empty body**, even though the snapshot declares `201`
  (`MlModelEndpoint.cs:57`). Poll `TrainState` via `GET /ml/mlmodel/byid?identifier=primary&id=<guid>`
  or `GET /ml/mlmodel/selectbyid/{id}` (select list does not include the state — use `byid`).
- A non-guid `id` throws at `MlModelEndpoint.cs:152` and surfaces as `500`.
