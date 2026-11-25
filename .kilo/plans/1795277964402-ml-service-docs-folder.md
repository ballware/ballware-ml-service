# Plan: docs/ folder for ballware-ml-service (API + service mechanics, OpenAPI-based)

## Goal

Add a `docs/` folder to `ballware-ml-service` that details the HTTP API surface and the service's
inner workings, with the emphasis on **API calls and data payloads**, anchored on committed OpenAPI
snapshots. Structure must mirror the already-established per-service docs convention
(`ballware-meta-service/docs`, `ballware-generic-service/docs`) so `ballware-docs` can consume it later.

## Confirmed decisions (with user)

1. **One-line fix + full docs**: mount the generic editing API into the declared `ml` Swagger group
   so all operations appear in the OpenAPI snapshot; then document them fully.
2. **English** documentation language.
3. **Full structure** incl. `docs/best-practices/` with anonymized MlModel definition templates
   derived from the fwpro reference data. Raw content of `ballware-fwpro` files must **not** be copied
   into the docs (patterns/conventions only, with placeholders).

## Key facts verified against code (evidence for the writer)

- Swagger documents `ml` ("ballware Ml user API") and `service` ("ballware Ml service API"),
  `Startup.cs:291-301`; UI wiring `Startup.cs:377-389`; raw JSON at `/swagger/{ml|service}/swagger.json`.
- Mounted routes (`Startup.cs:362-366`):
  - `MapMlModelUserApi("/ml/mlmodel")` → group `ml`, policy `mlApi` (MlModelEndpoint.cs:19-65):
    `GET selectlist`, `GET selectbyid/{id}`, `GET consumebyid/{modelId}`, `POST train`.
  - `MapMlModelServiceApi("/ml/mlmodel")` → group `service`, policy `serviceApi`
    (MlModelEndpoint.cs:67-97): `POST consumebyidbehalfofuser/{tenantId}/{userId}/{modelId}`,
    `POST consumebyidentifierbehalfofuser/{tenantId}/{userId}/{identifier}`.
  - `MapTenantableEditingApi<Data.Public.MlModel>("/ml/mlmodel", "meta", "mlmodel", "MlModel",
    "MlModel", "mlApi", "mlmodel")` (`Startup.cs:364`) → the 11 shared editing operations
    (`all|query|new|byid|save|savebatch|remove/{id}|import|export|exporturl|download/{tenantId}/{id}`,
    see `ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs`).
  - `MapMlTenantServiceApi("/ml/tenant")` → `GET reportmldatasourcesfortenant/{tenantId}`
    (TenantEndpoint.cs:26-33).
- **Gap (to fix)**: editing API uses apiGroup `"mlmodel"`, which is not a declared SwaggerDoc → the 11
  operations appear in neither document today. Confirmed via live snapshots
  `ballware-docs/openapi/ml/ml.json` (4 paths) and `service.json` (3 paths).
  **Fix**: `Startup.cs:364` last parameter `"mlmodel"` → `"ml"`. No operationId collisions
  (`MlModelAll/Query/New/ById/Save/SaveBatch/Remove/Import/Export/ExportUrl/Download` vs
  `MlModelSelectList/SelectById/ConsumeById/Train`). NSwag client is generated from the `service`
  document only (`publish-packages.yml` + `nswag.json`) → unaffected.
- Payload conventions to document:
  - JSON bodies PascalCase (`Startup.cs:168-176`).
  - Enums serialize as **integers**: `Type` 1 = Regression (MlModelTypes.cs), `TrainState`
    0/1/5/6/10/99 = Unknown/Outdated/Queued/InProgress/UpToDate/Error (MlModelTrainingStates.cs);
    confirmed by fwpro export files (`Type: 1`, `TrainState: 10/99`).
  - `MlModel.Options` is an embedded JSON **string**, camelCase keys (Newtonsoft, case-insensitive):
    `trainingTime`, `trainingFileName`, `trainingLogFileName`, `featureFields: [{type, name}]`,
    `predictionField: {type, name}` (ModelOptions.cs, RegressionOptions.cs, RegressionField.cs,
    FieldTypes.cs 0-7: Bool/Int/Decimal/Double/String/Date/Datetime/Time).
  - `TrainResult` success shape: `{TrainingRecordCount, Loss, Metric, DurationInMilliseconds, PeakCpu,
    PeakMemoryInMegaByte}`; error shape `{TrainingRecordCount, Exception}` (AutoMlExecutor.cs:111-137).
  - Prediction **input**: JSON object / query params keyed by `featureFields[].name`; values parsed per
    FieldTypes (AutoMlModelBuilder.cs:129-175). GET variant binds query string via `QueryValueBag`
    (QueryValueBag.cs), POST variants bind body via `BodyValueBag` (BodyValueBag.cs).
  - Prediction **output**: echo of feature fields + `Score` (float); `Features` array is JsonIgnore'd
    (AutoMlModelBuilder.cs:206-239).
  - Editing semantics of `meta.mlmodel` (research via `ballware-fwpro/meta/metadata-entities.json`):
    rights `meta.mlmodel.view/edit/add/delete`; query identifier conventions `primary` (default),
    `exportjson` (ListQuery), `trainingstate` (SaveStatement used by `SaveTrainingStateAsync`).
    Document the conventions, not the tenant's actual query texts.
  - `reportmldatasourcesfortenant` returns `ReportDatasourceDefinition` incl. the configured provider +
    **connection string** of the ML storage and a generated `mlmodelLookup` table query
    (TenantEndpoint.cs:38-61, ReportDatasource.cs) → add a security note (serviceApi-only, treats
    connection strings as sensitive payloads).
- Training/prediction mechanics for concepts pages:
  - `POST train?id=<guid>` (repeatable) → sets state `Queued`, triggers Quartz `JobKey("train","ml")`,
    returns **202 Accepted** (MlModelEndpoint.cs:129-168) — note the spec mismatch: endpoint declares
    `Produces(201)` (line 57) but returns 202; docs describe actual behavior.
  - Job → `InProgress` → metadata → dynamic input type from `Options` → training rows via Generic
    Service plain query (`TrainSql`, GenericServiceTenantDataAdapter.cs) → AutoML featurizer +
    regression experiment, R² metric, `trainingTime` default 60 s → artifact uploaded to Storage Service
    as attachment of entity `mlmodel`, owner = model id, filename `trainingFileName` default
    `model.zip`; AutoML log uploaded as `trainingLogFileName` (AutoMlExecutor.cs:33-149).
  - Prediction: metadata → tenant-aware in-memory cache keyed by tenant+identifier
    (`Cache.CacheExpirationHours`, purged on retrain) → model artifact loaded from Storage →
    prediction engine (AutoMlExecutor.cs:151-226).
  - Outbound integrations via OAuth2 client credentials: Meta (metadata/rights/jobs/exports), Generic
    (training data), Storage (attachments/temp files) — adapters in `src/Ballware.Ml.Service/Adapter/`.
- **Suspected defect to verify (do not fix in this task)**: GET `consumebyid` binds query values as
  `string[]` (QueryValueBag.cs:13) and `CreateInput` calls `value.ToString()`
  (AutoMlModelBuilder.cs:169) → typed parsing likely fails for real values. Verify with a small unit
  test in `test/Ballware.Ml.Api.Tests` (pattern: `MlModelMetaApiTest.cs` / `ApiMappingBaseTest`).
  If confirmed, document as a **known limitation** (use the POST service-API variants for prediction)
  and report to the user; leave the code fix out of scope.

## Deliverables (target layout, mirrors ballware-meta-service/docs)

```
docs/
  README.md                       # contents table, quick facts, request-flow ascii diagram
  concepts/
    architecture.md               # projects, request pipeline, mounted routes, error contract (ProblemDetails, Startup.cs:330-355)
    tenancy-and-authorization.md  # JWT, scope policies mlApi/serviceApi, tenant/user claims, Jint rights via Meta adapter
    model-lifecycle.md            # model definition, training job flow, TrainState machine, caching, artifacts
    integration-dependencies.md   # Meta/Generic/Storage client contracts + startup validation
  api/
    README.md                     # documents table (ml: 15 paths after group fix, service: 3), conventions, tokens, errors
    ml-api.md                     # 4 user-API operations + payload details incl. train id query bag
    service-api.md                # 3 service-API operations + ReportDatasource* payload
    generic-crud.md               # the 11 editing operations for MlModel incl. identifier/right conventions
    schemas.md                    # field tables: MlModel, MlModelSelectListEntry, ReportDatasource*, embedded Options/TrainResult JSON
    examples.md                   # copy-pasteable curl sequences: new → save → train → poll TrainState → predict
  best-practices/
    model-definition.md           # anonymized template: Options JSON skeleton, feature/prediction field design, trainingTime ranges, TrainSql patterns (derived patterns only, no fwpro tenant content)
  openapi/
    README.md                     # generation mechanism, Swagger-env rationale, leak warning, drift cadence
    fetch-spec.sh                 # adapted generator (see below)
    ml.json / service.json        # committed snapshots (generated)
    manifest.json                 # provenance (tool, version, git describe, fetchedAt, path/schema counts)
```

## Ordered tasks

1. **Source fix**: `src/Ballware.Ml.Service/Startup.cs:364` — change last argument `"mlmodel"` → `"ml"`.
   `dotnet build src/Ballware.Ml.Service --configuration Release` (private NuGet feed may need
   `GITHUB_USERNAME`/`GITHUB_PAT`).
2. **Snapshot tooling**: create `docs/openapi/fetch-spec.sh` adapted from
   `/Users/frank/Documents/GitHub/ballware-meta-service/docs/openapi/fetch-spec.sh`:
   - documents loop `for doc in ml service`; project `src/Ballware.Ml.Service`;
     dll `bin/Release/net10.0/Ballware.Ml.Service.dll`; tool pin `Swashbuckle.AspNetCore.Cli 10.1.3`
     (matches `Ballware.Ml.Service.csproj:31-32` and CI pin).
   - Keep `export ASPNETCORE_ENVIRONMENT=Swagger` **and** the env override
     `Authorization__Authority=https://localhost` — required because `appsettings.local.json`
     (git-ignored but present in dev checkouts, `Program.cs:17-20`) would otherwise bake real IdP URLs
     into committed snapshots.
   - Run it; expect `ml.json` ≈ 15 paths (4 user + 11 editing), `service.json` 3 paths; write manifest.
3. **Author docs pages** (English) against the generated snapshots plus the code evidence above.
   Every cited `file:line` must be re-verified by the writer. Keep fwpro data out; use placeholders
   (`<tenantId>`, `daytimePersonalCount`-style generic examples).
4. **Verify behavioral caveats** before writing them down:
   - train returns 202 while spec says 201 → document actual behavior + note the annotation mismatch.
   - GET `consumebyid` query-value binding issue → add/adjust a test in `Ballware.Ml.Api.Tests`;
     document verified behavior or known limitation.
5. **Root README**: add a short "Documentation" section linking `docs/README.md` (setup content stays untouched).
6. **Validate**: `dotnet test` (skip DB-backed projects), re-run `docs/openapi/fetch-spec.sh`
   idempotently (only `fetchedAt` should change), and check every `api/*.md` path exists in the snapshots.

## Risks / notes

- The group fix changes the public Swagger surface of the `ml` document (4 → 15 paths). Downstream
  `ballware-docs` (live fetch via `scripts/fetch-openapi.mjs` + `api/ml-service/` pages) will see the
  new operations; updating `ballware-docs` pages is explicitly out of scope.
- Never commit `appsettings.local.json` or any real tenant metadata; snapshots come from the Swagger
  environment with pinned Authority.
- `reportmldatasourcesfortenant` payload includes a live connection string → docs must flag handling
  requirements, without including any real DSN.

## Out of scope

- Fixing the `QueryValueBag`/string[] parsing defect and the 201/202 annotation mismatch.
- CI automation of the snapshot export; ballware-docs page/sidebar updates.

## Validation checklist

- [ ] `dotnet build` succeeds with the group fix.
- [ ] `ml.json` contains all 15 ml-group paths; `service.json` unchanged (3 paths).
- [ ] `manifest.json` written; re-run produces no diff except `fetchedAt`.
- [ ] All API pages cross-check against snapshots; no fwpro tenant data anywhere in docs/.
- [ ] `dotnet test` passes for non-DB test projects.
