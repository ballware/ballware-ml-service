# Ml service API (`service` document, policy `serviceApi`)

Mounted by `MapMlModelServiceApi("/ml/mlmodel")` (`Startup.cs:363`,
`src/Ballware.Ml.Api/Endpoints/MlModelEndpoint.cs:67-97`) and `MapMlTenantServiceApi("/ml/tenant")`
(`Startup.cs:366`, `src/Ballware.Ml.Api/Endpoints/TenantEndpoint.cs:19-36`).

These operations are meant for other services running with a client-credentials token. The tenant and
acting user are **path parameters**, not claims (`MlModelEndpoint.cs:74-75, 85-86`) — see the security
note below.

## Overview

| # | Method | Path | Operation id | Success |
| --- | --- | --- | --- | --- |
| 1 | POST | `/ml/mlmodel/consumebyidbehalfofuser/{tenantId}/{userId}/{modelId}` | `MlModelConsumeByIdBehalfOfUser` | `200`, prediction object |
| 2 | POST | `/ml/mlmodel/consumebyidentifierbehalfofuser/{tenantId}/{userId}/{identifier}` | `MlModelConsumeByIdentifierBehalfOfUser` | `200`, prediction object |
| 3 | GET | `/ml/tenant/reportmldatasourcesfortenant/{tenantId}` | `TenantReportMlDatasources` | `200`, array of `ReportDatasourceDefinition` |

## 1. `POST …/consumebyidbehalfofuser/{tenantId}/{userId}/{modelId}`

Predict by model **uuid**. The body is a JSON object of feature values keyed exactly like
`Options.featureFields[].name` (`Accepts<Dictionary<string, object>>`, `MlModelEndpoint.cs:78-79`;
bound by `src/Ballware.Ml.Api/Endpoints/BodyValueBag.cs:11-54`).

```bash
curl -s -X POST \
  "https://localhost:6003/ml/mlmodel/consumebyidbehalfofuser/<tenantId>/<userId>/6f2c1e2a-0000-4000-8000-000000000001" \
  -H "Authorization: Bearer $SERVICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "Day": 3, "Hour": 7, "Weekend": true }'
```

JSON scalars arrive as typed CLR values (`long`, `double`, `bool`, `string`, `BodyValueBag.cs:38-54`)
and are converted to the compiled feature types by `AutoMlModelBuilder.CreateInput`
(`AutoMlModelBuilder.cs:152-175`). Verified by
`test/Ballware.Ml.Api.Tests/MlModel/MlModelConsumeAndTrainBehaviorTest.cs`.

`{userId}` is currently accepted for contract completeness and is not used by the handler
(`MlModelEndpoint.cs:170-176`).

Response (feature echo + `Score`, `AutoMlModelBuilder.cs:206-239`):

```json
{ "Day": 3, "Hour": 7, "Weekend": true, "Score": 12.4 }
```

| Situation | Status |
| --- | --- |
| Body is missing or not a JSON object (binder returns `null`) | `400` (verified by `MlModelConsumeAndTrainBehaviorTest`) |
| Model uuid unknown for the tenant | `500` (`ArgumentException`, `AutoMlExecutor.cs:155-158`) |
| Model trained but artifact missing in Storage | `500` (attachment lookup fails, `AutoMlExecutor.cs:199-200`) |

## 2. `POST …/consumebyidentifierbehalfofuser/{tenantId}/{userId}/{identifier}`

Identical to #1 except the model is addressed by its `Identifier` (`MlModelEndpoint.cs:178-184` →
`AutoMlExecutor.cs:173-193` → `MlModelBaseRepository.cs:29-35`). This is the variant the platform
conventions use for user-facing prediction, since model names are stable across environments while
uuids are not.

```bash
curl -s -X POST \
  "https://localhost:6003/ml/mlmodel/consumebyidentifierbehalfofuser/<tenantId>/<userId>/daytime-personal-count" \
  -H "Authorization: Bearer $SERVICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "Day": 3, "Hour": 7 }'
```

Both prediction variants share the tenant+identifier prediction engine cache
([concepts/model-lifecycle.md](../concepts/model-lifecycle.md#prediction-flow)).

## 3. `GET /ml/tenant/reportmldatasourcesfortenant/{tenantId}`

Reports the datasource definition a reporting tool needs to read this tenant's ML lookups
(`TenantEndpoint.cs:38-61`). One `ReportDatasourceDefinition` is returned, named `MlLookups`
(`TenantEndpoint.cs:17`), containing the configured provider and connection string of the **ML
storage** plus one generated table query (`MlModelBaseRepository.GenerateListQueryAsync`, implemented
per provider in `Postgres/Repository/MlModelRepository.cs:15-18` and
`SqlServer/Repository/MlModelRepository.cs:15-18`):

```json
[
  {
    "Name": "MlLookups",
    "Provider": "postgres",
    "ConnectionString": "Server=<host>;Database=<ml-db>;Port=5432;User Id=<user>;…",
    "Tables": [
      {
        "Name": "mlmodelLookup",
        "Query": "SELECT uuid AS \"Id\", identifier as \"Identifier\" FROM ml_model WHERE tenant_id='<tenantId>'",
        "Relations": []
      }
    ]
  }
]
```

Shapes: `ReportDatasourceDefinition`, `ReportDatasourceTable`, `ReportDatasourceRelation` in
`src/Ballware.Ml.Data/Public/ReportDatasource.cs:3-25` (tables in [schemas.md](schemas.md)).

### Security note

The response carries a **live connection string** for the ML storage, in plaintext, and the generated
query embeds the tenant id. Handling requirements:

- The operation is guarded only by the `serviceApi` scope — there is no claim-to-path tenant comparison
  (`TenantEndpoint.cs:26-30`). Anyone holding that scope can read any tenant's connection string.
  Treat `serviceApi` as privileged and issue it only to first-party services.
- Do not log, cache, persist or forward this response body. Reporting integrations should consume it
  in-process.
- The generated query interpolates `tenantId` directly into SQL text (`Postgres/Repository/MlModelRepository.cs:17`);
  it is only reachable with a `serviceApi` token, but never expose the returned query string to
  lower-privileged callers.
- Never commit a real connection string: the committed snapshots in [docs/openapi/](../openapi/) are
  generated in the `Swagger` environment, where `appsettings.Swagger.json` supplies fake values only.
