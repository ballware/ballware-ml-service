# OpenAPI snapshots

The committed files in this folder are the source of truth the API pages were written against:

| File | Content |
| --- | --- |
| [ml.json](ml.json) | `ballware Ml user API` — 15 paths, 6 component schemas, OpenAPI `3.0.4` |
| [service.json](service.json) | `ballware Ml service API` — 3 paths, 3 component schemas, OpenAPI `3.0.4` |
| [manifest.json](manifest.json) | Provenance: generator tool + version, environment, git describe reference, `fetchedAt`, per-document openapi/title/pathCount/schemaCount |
| [fetch-spec.sh](fetch-spec.sh) | Generator script |

## How the snapshot is produced

Generation runs **offline from the built assembly** — no database, no IdP. The CI pipeline uses the same
mechanism for the client build (`.github/workflows/publish-packages.yml:29-33, 50-62`).

Run from the repository root (idempotent — it rebuilds and rewrites the three files):

```bash
docs/openapi/fetch-spec.sh
```

What it does:

1. `dotnet build src/Ballware.Ml.Service --configuration Release`
2. Ensures the pinned tool is available at `$HOME/.dotnet/openapi-tools/swagger`:

   ```bash
   dotnet tool install Swashbuckle.AspNetCore.Cli --version 10.1.3 --tool-path "$HOME/.dotnet/openapi-tools"
   ```

3. Exports both documents with the Swagger environment:

   ```bash
   export ASPNETCORE_ENVIRONMENT=Swagger
   export Authorization__Authority=https://localhost
   cd src/Ballware.Ml.Service
   "$HOME/.dotnet/openapi-tools/swagger" tofile --output ../../docs/openapi/ml.json \
       bin/Release/net10.0/Ballware.Ml.Service.dll ml
   "$HOME/.dotnet/openapi-tools/swagger" tofile --output ../../docs/openapi/service.json \
       bin/Release/net10.0/Ballware.Ml.Service.dll service
   ```

4. Validates each file (parses as JSON, `openapi` starts with `3.`, `paths` non-empty) and writes
   `manifest.json`.

The `swagger tofile` tool resolves the host from the assembly **without starting it**, so the EF
initialization worker never runs — no Postgres, no Keycloak needed.

## Tool version pin

Keep `Swashbuckle.AspNetCore.Cli` at **10.1.3** — it matches the service's Swashbuckle packages
(`src/Ballware.Ml.Service/Ballware.Ml.Service.csproj:31-32`) and the CI tool pin
(`publish-packages.yml:32`). Other versions fail outright against this assembly
(`Could not load file or assembly 'Microsoft.OpenApi, Version=2.7.5.0, …'`) or reformat the documents and
produce spurious diffs.

The script installs the pinned CLI into its **own tool path** (`$HOME/.dotnet/openapi-tools`) instead of
the global tool folder, so a different global version of the tool cannot silently change the output.
The CI equivalent is the tool-manifest variant used in `publish-packages.yml:30-33`.

## Why `ASPNETCORE_ENVIRONMENT=Swagger`

`appsettings.Swagger.json` (`src/Ballware.Ml.Service/appsettings.Swagger.json`) supplies **fake**
Authority/clients (`https://localhost`, `fake*` clients/secrets, `Server=localhost;Database=ml_dev;…`
placeholder connection string). Consequently the `oidc` security definition in the snapshot points at a
non-existent endpoint:

```json
"oidc": { "type": "openIdConnect", "openIdConnectUrl": "https://localhost/.well-known/openid-configuration" }
```

That URL is a placeholder, **not** a real IdP discovery endpoint. Do not wire clients to it.

> **Leak warning:** configuration order is `appsettings.json` → `appsettings.{Environment}.json` →
> `appsettings.local.json` → environment variables (`src/Ballware.Ml.Service/Program.cs:19-23`). A
> developer `appsettings.local.json` (git-ignored via `.gitignore:487` but present in dev checkouts)
> overrides the Swagger values — including the Authority — and its real IdP URL would be baked into the
> committed snapshot. The script therefore pins `Authorization__Authority=https://localhost` as an
> environment override. When regenerating by hand (or in CI), generate from a clean checkout without
> `appsettings.local.json`.

## What changed when the editing API joined the `ml` group

The editing mount used `apiGroup "mlmodel"`, which is not a declared `SwaggerDoc`
(`Startup.cs:291-301`), so its 11 operations appeared in **neither** document. The mount now uses
`"ml"` (`Startup.cs:364`), so `ml.json` has 15 paths (4 user + 11 editing) instead of 4.

- No operation-id collisions: `MlModelAll/Query/New/ById/Save/SaveBatch/Remove/Import/Export/ExportUrl/Download`
  vs `MlModelSelectList/SelectById/ConsumeById/Train`.
- `service.json` is unchanged (3 paths), and the NSwag client is generated from the `service` document
  only (`publish-packages.yml:50-62`, `src/Ballware.Ml.Service.Client/nswag.json`) — unaffected.
- Downstream consumers that fetch the live `ml` document will see 11 new operations.

## Refresh cadence and drift

The snapshot is a manual artifact: regenerate after endpoint changes (new/renamed routes, DTO changes,
Swagger config). A re-run should only change `manifest.json`'s `fetchedAt`; if `ml.json`/`service.json`
also change, review the diff — it is an API surface change and the API pages here should be reviewed
against it. Regenerating does not require touching any source code.

Optional follow-up (not part of this setup): extend CI to export `ml.json`/`service.json` next to the
client's `swagger.json` so refreshes can be automated.

## Snapshot ↔ docs mapping

| Snapshot | Documented by |
| --- | --- |
| `ml.json` user-API paths | [../api/ml-api.md](../api/ml-api.md) |
| `ml.json` editing paths | [../api/generic-crud.md](../api/generic-crud.md) |
| `service.json` paths | [../api/service-api.md](../api/service-api.md) |
| `ml.json` schemas (`MlModel`, `MlModelSelectListEntry`, `MlModelTypes`, `MlModelTrainingStates`, `ExportUrlResult`, `StringStringValuesKeyValuePair`) | meta tables in [../api/schemas.md](../api/schemas.md) |
| `service.json` schemas (`ReportDatasource*`) | service tables in [../api/schemas.md](../api/schemas.md) |
| both | [../api/examples.md](../api/examples.md) (every example path exists in the snapshots) |

## Live-server fallback

If `swagger tofile` ever fails to resolve the entry point, generate from a running instance instead
(needs a reachable database when `Storage.AutoMigrations` is set):

```bash
ASPNETCORE_ENVIRONMENT=Swagger dotnet run --project src/Ballware.Ml.Service
curl -s http://localhost:5003/swagger/ml/swagger.json -o docs/openapi/ml.json
curl -s http://localhost:5003/swagger/service/swagger.json -o docs/openapi/service.json
```

Prefer the offline path; a live-server export inherits the developer's local configuration.
