# API surface and conventions

Two OpenAPI documents are served per deployment, each with its own Swagger UI page and raw JSON:

| Document | Title | Paths | Schemas | Swagger JSON |
| --- | --- | --- | --- | --- |
| `ml` | ballware Ml user API | 15 | 6 | `/swagger/ml/swagger.json` |
| `service` | ballware Ml service API | 3 | 3 | `/swagger/service/swagger.json` |

Titles and version `v1` are defined in `src/Ballware.Ml.Service/Startup.cs:291-301`; the UI wiring is
`Startup.cs:377-389`. Committed snapshots of both documents live in [docs/openapi/](../openapi/).

The `ml` document contains **both** the 4 tenant-facing model operations
(`src/Ballware.Ml.Api/Endpoints/MlModelEndpoint.cs:19-65`) and the 11 generic editing operations for
`MlModel` (`Startup.cs:364`, `ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs:22-132`).

## Grouping, tags, operation ids

- Every endpoint carries a **group** (`WithGroupName`) and a resource **tag** (`WithTags`):
  `ml` for user + editing operations, `service` for the service API
  (`MlModelEndpoint.cs:31, 41, 50, 60, 81, 92`, `TenantEndpoint.cs:31`, `TenantantableEditingEndpoint.cs:28-29`).
- Tags are `MlModel` (`MlModelEndpoint.cs:16`, `TenantantableEditingEndpoint.cs` mount at `Startup.cs:364`)
  and `Tenant` (`TenantEndpoint.cs:14`).
- Operation ids follow `<OperationPrefix><Name>` with prefix `MlModel` / `Tenant`:
  `MlModelSelectList`, `MlModelConsumeByIdBehalfOfUser`, `MlModelSaveBatch`, `TenantReportMlDatasources`
  (`.WithName(apiOperationPrefix + "…")` calls, e.g. `MlModelEndpoint.cs:30, 80-81`, `TenantEndpoint.cs:30`).
- Schema names come straight from the public contract types (`MlModel`, `MlModelSelectListEntry`,
  `ReportDatasourceDefinition`, …) — no `CustomSchemaIds` rewrite is configured.

## Payload conventions

- Request and response JSON is **PascalCase**: both MVC and HTTP JSON options disable the naming policy
  (`Startup.cs:168-176`).
- Enums serialize as **integers** — `Type` `0`/`1` (`MlModelTypes.cs:3-6`), `TrainState`
  `0/1/5/6/10/99` (`MlModelTrainingStates.cs:3-10`). There is no string-enum converter.
- `Options` and `TrainResult` are **strings containing JSON**, not objects
  (`src/Ballware.Ml.Data/Public/MlModel.cs:18-20`) — see [schemas.md](schemas.md).
- GUIDs are plain guid strings (`format: uuid` in the snapshots).
- Send `Content-Type: application/json` for JSON bodies; `export` responses carry the export media type
  (`application/json` in practice, `TenantableBaseRepository.cs:256-263`).

## The `identifier` query parameter

The 11 editing operations resolve `identifier` from the **query string** — it is required on
`all`, `query`, `new`, `byid`, `save`, `savebatch`, `import`, `export`, `exporturl`
(`TenantantableEditingEndpoint.cs:22-123`) and absent on `remove/{id}` and `download/...`, which use
`primary` internally (`TenantableEndpointHandlerFactory.cs:17, 158`). `identifier` selects the
metadata definition and the **right candidate**; the data access itself is EF against `ml_model`.
Conventions for `meta.mlmodel`: `primary` (default), `exportjson` (export list), `trainingstate`
(dedicated save statement). Full table in [generic-crud.md](generic-crud.md).

Multi-value query parameters on `query` are merged into a single delimited value: repeated
`?identifier=primary&Identifier=a&Identifier=b` reaches the handler as `Identifier = "|a|b|"`
(`TenantableEndpointHandlerFactory.cs:312-329`).

## Spec vs. runtime caveats

Document these when generating clients from the snapshots:

| Snapshot says | Runtime does | Where |
| --- | --- | --- |
| `train` responses `201 Created` | `202 Accepted` always | `MlModelEndpoint.cs:57` vs `:138, 167` (verified by `MlModelConsumeAndTrainBehaviorTest`) |
| `train` has no parameters | feature/model ids come from the query string via `QueryValueBag` | `MlModelEndpoint.cs:129`, `QueryValueBag.cs:11-15` |
| `consumebyid/{modelId}` has only the path parameter | feature values are free-form query parameters, bound as `string[]` | `MlModelEndpoint.cs:45-52`, `QueryValueBag.cs:13` (see [model-lifecycle.md](../concepts/model-lifecycle.md#known-limitation-get-prediction-input-binding)) |
| Both documents require scopes `openid mlApi` | `service` operations require the `serviceApi` scope | `SwaggerOptions.cs:8`, `MlModelEndpoint.cs:71`, `TenantEndpoint.cs:23` |
| `export` produces `string` with `application/json` | response content type is the export definition's media type | `TenantantableEditingEndpoint.cs:103-111`, `TenantableEndpointHandlerFactory.cs:241-244` |

## Obtaining tokens

- Swagger UI (`/swagger`) uses the OIDC button with `Swagger.ClientId` and `Swagger.RequiredScopes`
  (authorization code + PKCE, `Startup.cs:384-387`).
- Service-to-service callers obtain a client-credentials token carrying the `serviceApi` scope from the
  IdP token endpoint — the same mechanism the Ml Service itself uses for outbound calls
  (`Startup.cs:188-215`):

  ```bash
  curl -s -X POST <tokenEndpoint> \
    -d 'grant_type=client_credentials&client_id=<clientId>&client_secret=<clientSecret>&scope=openid serviceApi'
  ```

  User API and editing calls need a user token carrying the `mlApi` scope **and** the `tenant` claim.

## Error responses

500-class failures return `application/problem+json` (`Startup.cs:330-355`). Expected per-operation
statuses: [concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md#expected-status-codes).

## Pages

| Page | Covers |
| --- | --- |
| [ml-api.md](ml-api.md) | `selectlist`, `selectbyid/{id}`, `consumebyid/{modelId}`, `train` |
| [service-api.md](service-api.md) | `consumebyidbehalfofuser`, `consumebyidentifierbehalfofuser`, `reportmldatasourcesfortenant` |
| [generic-crud.md](generic-crud.md) | the 11 `MlModel` editing operations |
| [schemas.md](schemas.md) | component schemas + embedded JSON payloads |
| [examples.md](examples.md) | end-to-end request sequences |
