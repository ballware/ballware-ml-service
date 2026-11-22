# Generic CRUD operations for `MlModel`

`MlModel` definitions are edited through the shared 11-operation editing API
(`ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs:22-132`,
handler implementation in `TenantableEndpointHandlerFactory.cs`), mounted at `/ml/mlmodel` with
`application="meta"`, `entity="mlmodel"`, policy `mlApi`, group `ml` (`src/Ballware.Ml.Service/Startup.cs:364`).
These 11 paths are part of the **`ml`** document in the committed snapshot.

All operations require the `mlApi` endpoint policy; most handlers then compute a **right candidate**
and let the tenant/entity rights scripts decide
([concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md#script-based-enforcement-the-real-control-point)):
`all`/`query`/`byid` use `view`, `new` uses `add`, `remove/{id}` uses `delete`, `save`/`savebatch` use
`edit` unless `identifier` is present and not `primary` — then the identifier itself is the right.
`export`/`exporturl` always use the identifier as the right. `import` only requires the scope at
request time; the scheduled job checks `meta.mlmodel.<identifier>` per item.

## Operation overview

| # | Method | Path | Parameters | Request body | Success response | Right candidate |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | GET | `/ml/mlmodel/all` | `identifier` (query, required) | – | `200`, array of `MlModel` | `meta.mlmodel.view` |
| 2 | GET | `/ml/mlmodel/query` | `identifier` (query, required) + free query params | – | `200`, array of `MlModel` | `meta.mlmodel.view` |
| 3 | GET | `/ml/mlmodel/new` | `identifier` (query, required) | – | `200`, `MlModel` | `meta.mlmodel.add` |
| 4 | GET | `/ml/mlmodel/byid` | `identifier`, `id` (both query, required; `id` is uuid) | – | `200`, `MlModel`; `404` if not found | `meta.mlmodel.view` |
| 5 | POST | `/ml/mlmodel/save` | `identifier` (query, required) | `MlModel` JSON | `200` empty | `meta.mlmodel.edit` or `meta.mlmodel.<identifier>` |
| 6 | POST | `/ml/mlmodel/savebatch` | `identifier` (query, required) | array of `MlModel` JSON | `200` empty | `meta.mlmodel.edit` or `meta.mlmodel.<identifier>` |
| 7 | DELETE | `/ml/mlmodel/remove/{id}` | `id` (path, uuid) | – | `200` empty; `404` if missing; `400` if a remove precondition fails | `meta.mlmodel.delete` |
| 8 | POST | `/ml/mlmodel/import` | `identifier` (query, required) | `multipart/form-data`, field `files` (file array) | `201` empty; schedules a job | scope only (job checks `meta.mlmodel.<identifier>`) |
| 9 | GET | `/ml/mlmodel/export` | `identifier` (query, required) + query params | – | `200`, raw export body | `meta.mlmodel.<identifier>` |
| 10 | POST | `/ml/mlmodel/exporturl` | `identifier` (query, required) | `application/x-www-form-urlencoded` fields | `200`, `ExportUrlResult` | `meta.mlmodel.<identifier>` |
| 11 | GET | `/ml/mlmodel/download/{tenantId}/{id}` | `tenantId`, `id` (path, uuid) | – | `200`, file content; `404` if missing/expired | **anonymous** |

## What `identifier` means here

The editing handlers use `identifier` for two things:

1. the **right candidate** (`meta.mlmodel.<operation>` or `meta.mlmodel.<identifier>`), and
2. the **export definition** name — `export` produces `<identifier>.json`
   (`TenantableBaseRepository.cs:256-263`) and the anonymous download file is named
   `<Query>_<timestamp><ext>` where `Query` is the identifier
   (`TenantableEndpointHandlerFactory.cs:308`).

The data access itself is EF Core against `ml_model`, not the SQL stored in the entity metadata:
`TenantableBaseRepository` filters by `TenantId` and, on `query`/`export`, additionally by any `id`
query parameters (`TenantableBaseRepository.cs:25-44`). Conventions used by `meta.mlmodel` metadata
(rights catalog and export/query definitions in the tenant metadata):

| Identifier | Conventional meaning |
| --- | --- |
| `primary` | default for `all`/`query`/`new`/`byid`/`save`/`savebatch` (`TenantableEndpointHandlerFactory.cs:17`); `remove/{id}` always uses it (`:158`) |
| `exportjson` | export definition for the full model list (used with `export`/`exporturl`, right `meta.mlmodel.exportjson`) |
| `trainingstate` | dedicated save statement for the training-state fields; calling `save?identifier=trainingstate` requires the right `meta.mlmodel.trainingstate` instead of `meta.mlmodel.edit` |

> Those definitions live in the tenant's `meta.mlmodel` entity metadata and are authored per
> deployment. This page documents the **conventions** the service expects; the concrete query texts are
> not part of this repository.

## Query and by-id semantics

- Extra query parameters on `query` become query parameters of the query definition
  (`TenantableEndpointHandlerFactory.cs:71-84`). A parameter repeated multiple times arrives merged as
  `|value1|value2|` (`TenantableEndpointHandlerFactory.cs:312-329`):

  ```text
  GET /ml/mlmodel/query?identifier=primary&Identifier=model-a&Identifier=model-b   →  Identifier = "|model-a|model-b|"
  ```

- Repeated `id` parameters filter the result by uuid (`TenantableBaseRepository.cs:25-44`).
- `byid` takes `identifier` **and** `id` as query parameters:
  `GET /ml/mlmodel/byid?identifier=primary&id=<guid>` (`TenantantableEditingEndpoint.cs:52-60`).
  The item is loaded **before** the right check, so a missing model answers `404` before any rights
  evaluation (`TenantableEndpointHandlerFactory.cs:101-111`).

## Save

`save` posts one document, `savebatch` an array (checked item by item,
`TenantableEndpointHandlerFactory.cs:133-152`). Responses are empty `200` — re-read via `byid`.

```bash
curl -s -X POST "https://localhost:6003/ml/mlmodel/save?identifier=primary" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{
    "Id": "<guid>",
    "Identifier": "daytime-personal-count",
    "Type": 1,
    "TrainSql": "select ... as Day, ... as Hour, ... as Result from <tenant source>",
    "TrainState": 0,
    "TrainResult": null,
    "Options": "{ \"trainingTime\": 120, \"trainingFileName\": \"model_current.zip\", \"featureFields\": [ { \"type\": 1, \"name\": \"Day\" }, { \"type\": 1, \"name\": \"Hour\" } ], \"predictionField\": { \"type\": 1, \"name\": \"Result\" } }"
  }'
```

Notes:

- `Options` and `TrainResult` are **JSON strings** — escape them (`src/Ballware.Ml.Data/Public/MlModel.cs:18-20`).
- The service writes the payload as sent: `TrainState` is **not** reset automatically on save
  (`TenantableBaseRepository.cs:156-193`, no hook overrides in
  `src/Ballware.Ml.Data.Ef/Repository/MlModelBaseRepository.cs`). Set it to `1` (`Outdated`) yourself
  when a definition changed, or let the tenant's `primary` save statement convention do it in
  deployments where the entity metadata defines one.
- A non-`primary` identifier (e.g. `?identifier=trainingstate`) requires the right
  `meta.mlmodel.<identifier>` (`TenantableEndpointHandlerFactory.cs:122-123, 140-141`).
- `(TenantId, Identifier)` is unique, so reusing an existing identifier with a new `Id` fails with a
  `500` `application/problem+json` (`MlModelBaseCustomizer.cs:41`, `Startup.cs:330-355`).

## Remove

`remove/{id}` loads the model with `primary`, checks `meta.mlmodel.delete`, then removes it
(`TenantableEndpointHandlerFactory.cs:154-184`). A failing remove precondition returns `400` with the
joined messages as body (`:177-179`). Trained artifacts in the Storage Service are **not** deleted with
the model row — remove them separately if required.

## Export → exporturl → download

```text
GET  /ml/mlmodel/export?identifier=exportjson&<query params>   200 raw body: JSON array of MlModel
POST /ml/mlmodel/exporturl?identifier=exportjson               form fields → query params → ExportUrlResult {TenantId, Id}
     form body: param1=value1&param2=value2                   Export record expires = now + 1 day (TenantableEndpointHandlerFactory.cs:275)
GET  /ml/mlmodel/download/<tenantId>/<id>                      anonymous; 404 when missing or expired (L296-299)
```

- The export body is produced by `TenantableBaseRepository.ExportAsync`: items serialized with
  Newtonsoft (`TenantableBaseRepository.cs:256-263`), media type `application/json`, suggested file name
  `<identifier>.json`. The snapshot declares `string` responses
  (`TenantantableEditingEndpoint.cs:103-111`); at runtime the content type comes from the export result
  (`TenantableEndpointHandlerFactory.cs:241-244`).
- `exporturl` creates an export record in the **Meta Service** (`ExportCreateForTenantBehalfOfUserAsync`,
  `src/Ballware.Ml.Service/Adapter/MetaServiceMetadataAdapter.cs:34-44`) and stores the payload as a
  temporary file in the Storage Service (`MetaServiceMetadataAdapter`/`StorageServiceFileStorageAdapter`
  wiring at `Startup.cs:266, 268-269`).
- `ExportUrlResult` = `{ "TenantId": "<guid>", "Id": "<guid>" }` (schema in [schemas.md](schemas.md)).
- The anonymous download resolves the export record via Meta (`MetaServiceMetadataAdapter.cs:46-59`)
  and streams the stored file (`TenantableEndpointHandlerFactory.cs:290-309`).

## Import

`import` uploads one or more files (form field `files`), stores each in the Storage Service as a
temporary file, creates a job record in the Meta Service and triggers the Quartz job
`import`/group `mlmodel` (`TenantableEndpointHandlerFactory.cs:188-220`, job registered at
`src/Ballware.Ml.Jobs/ServiceCollectionExtensions.cs:18`). It answers `201 Created` immediately; the
actual import runs in the background and checks `meta.mlmodel.<identifier>` per item.

```bash
curl -s -X POST "https://localhost:6003/ml/mlmodel/import?identifier=primary" \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@models.json"
```

An import file is a JSON array of `MlModel` documents in the same shape `export` produces.

## Status codes

`401` (missing scope / denied right), `404` (tenant, `mlmodel` entity metadata or item not found),
`400` (remove preconditions), `500` as `application/problem+json` — see
[concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md#expected-status-codes).
