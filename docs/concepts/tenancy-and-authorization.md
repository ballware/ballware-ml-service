# Tenancy and authorization

## Authentication

The service validates JWT bearer tokens against an OIDC issuer (`src/Ballware.Ml.Service/Startup.cs:123-137`):

- `Authorization.Authority` / `Authorization.Audience` / `Authorization.RequireHttpsMetadata` drive the
  JWT handler (`src/Ballware.Ml.Service/Configuration/AuthorizationOptions.cs:8-14, 23`).
- `ValidIssuer` is pinned to `Authorization.Authority` (`Startup.cs:133-136`).
- Inbound claim mapping is disabled (`Startup.cs:129`), so claim names appear exactly as issued.

Claims the service relies on (config keys and defaults from `AuthorizationOptions.cs:14-21`):

| Claim | Config key | Default | Used for |
| --- | --- | --- | --- |
| `tenant` | `Authorization.TenantClaim` | `tenant` | Tenant of the caller (user API + editing operations) |
| `sub` | `Authorization.UserIdClaim` | `sub` | Acting user id |
| `right` | `Authorization.RightClaim` | `right` | Rights granted to the user (read by the rights scripts) |
| `scope` | – | – | Policy selection (see below) |

Tenant and user id are resolved from the claims by `IPrincipalUtils.GetUserTenandId` /
`GetUserId` (`Ballware.Shared.Authorization`, used e.g. in `src/Ballware.Ml.Api/Endpoints/MlModelEndpoint.cs:101, 131-132`).

## Scope → policy mapping

Two policies are built in `Startup.cs:139-153`. Both split the `scope` claim value on spaces, so
multi-scope tokens work:

| Policy | Matches | Required for |
| --- | --- | --- |
| `mlApi` | `Authorization.RequiredMetaScope` (default `mlApi`, `AuthorizationOptions.cs:24`) | all `/ml/mlmodel/...` user API and editing operations (`MlModelEndpoint.cs:23`, `Startup.cs:364`) |
| `serviceApi` | `Authorization.RequiredServiceScope` (default `serviceApi`, `AuthorizationOptions.cs:25`) | the two `consume*behalfofuser` operations (`MlModelEndpoint.cs:71`) and `/ml/tenant/reportmldatasourcesfortenant` (`TenantEndpoint.cs:23`) |

A missing or invalid token, or a token without the required scope, yields `401 Unauthorized`.

> Spec vs. runtime: both Swagger documents declare a global security requirement of
> `openid mlApi` because `Swagger.RequiredScopes` defaults to `"openid mlApi"`
> (`src/Ballware.Ml.Service/Configuration/SwaggerOptions.cs:8`, `Startup.cs:311-317`) — even in the
> `service` document, whose operations actually require the `serviceApi` scope. Use the scope the
> endpoint requires, not the one the document advertises.

## Tenant resolution: two styles

- **User API + editing API** — the tenant comes from the `tenant` claim; there is no tenant path
  segment, so a caller can only act on its own tenant (`MlModelEndpoint.cs:99-131`, and the shared
  editing handlers).
- **Service API** — service-to-service callers act on a tenant given in the path (`{tenantId}`) plus
  the acting `{userId}`, because they run under their own client-credentials token
  (`MlModelEndpoint.cs:74-90`, `TenantEndpoint.cs:26`). There is **no** comparison between the path
  `tenantId` and any claim: the scope is the only gate. Treat `serviceApi` as a highly privileged scope.

## Endpoint policy only vs. rights checks

Only the 11 generic editing operations evaluate a right candidate. The 4 user API operations and the 3
service API operations stop at the endpoint policy — they never call the rights checkers
(`MlModelEndpoint.cs:99-127, 129-168, 171-184`, `TenantEndpoint.cs:38-61`).

Rights candidates are composed as `<application>.<entity>.<operation>` by the shared Jint checker
(`ballware-shared-dotnet/src/Ballware.Shared.Authorization.Jint/Internal/JavascriptTenantRightsChecker.cs:19`).
The Ml mount passes `application="meta"`, `entity="mlmodel"` (`Startup.cs:364`), producing
`meta.mlmodel.view`, `meta.mlmodel.add`, `meta.mlmodel.edit`, `meta.mlmodel.delete` and
`meta.mlmodel.<identifier>`:

| Operation | Right used | Source |
| --- | --- | --- |
| `all`, `query` | `view` | `TenantableEndpointHandlerFactory.cs:66, 79` |
| `new` | `add` | `TenantableEndpointHandlerFactory.cs:91` |
| `byid` | `view` (checked **after** the item is loaded) | `TenantableEndpointHandlerFactory.cs:101-111` |
| `save`, `savebatch` | `edit` — **unless** `identifier` is present and not `primary`; then the identifier itself becomes the right | `TenantableEndpointHandlerFactory.cs:17, 122-123, 140-141` |
| `remove/{id}` | `delete` (item first loaded with the `primary` identifier) | `TenantableEndpointHandlerFactory.cs:158-169` |
| `export`, `exporturl` | the `identifier` itself (e.g. `exportjson`) | `TenantableEndpointHandlerFactory.cs:238, 265` |
| `import` | endpoint policy only; the scheduled job later checks `meta.mlmodel.<identifier>` | `TenantableEndpointHandlerFactory.cs:188-220` |
| `download/{tenantId}/{id}` | anonymous | `ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs:125-132` |

## Script-based enforcement (the real control point)

`Rights` rows in the `meta.mlmodel` entity metadata are a catalog consumed by scripts and client UI —
the endpoint pipeline decides through two script hooks
(`ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/EditingEndpointBuilder.cs:66-115`):

1. **Tenant level** — tenant metadata missing → `404` (`EditingEndpointBuilder.cs:70-74`). Otherwise
   the tenant's `RightsCheckScript` runs with the composed candidate and the caller's claims
   (`JavascriptTenantRightsChecker.cs:8-25`):

   ```javascript
   // available variables inside Tenant.RightsCheckScript (synthetic pattern):
   // right    -> "meta.mlmodel.<operation>"   (fully composed candidate)
   // userinfo -> claim map of the caller
   if (right.startsWith('meta.')) {
     return true;                       // allow all meta-catalog rights, for example
   }
   return userinfo.rights?.includes(right) ?? false;
   ```

2. **Entity level** — entity metadata for the tenant missing → `404` (`EditingEndpointBuilder.cs:81-85`).
   Otherwise the entity's `CustomScripts.extendedRightsCheck` runs with `application`, `entity`,
   `right`, `param` (item or query parameters), `result` (tenant verdict) and `userinfo`, and can
   override the tenant verdict
   (`JavascriptEntityRightsChecker.cs:8-29`).

If the combined verdict is `false`, the handler returns `401 Unauthorized`
(`EditingEndpointBuilder.cs:111-113`) — a rights denial is not `403`.

Because the Ml Service resolves this metadata from the Meta Service (`MetaServiceMetadataAdapter.cs:20-32`),
a tenant that has no `meta` tenant or `mlmodel` entity metadata cannot use the editing operations at
all: every rights-checked editing call answers `404`, not `401`.

## Expected status codes

| Situation | Status | Source |
| --- | --- | --- |
| Missing/invalid token or missing scope | `401` | `Startup.cs:139-153` |
| Right denied by tenant/entity scripts | `401` | `EditingEndpointBuilder.cs:111-113` |
| Tenant or `mlmodel` entity metadata not found for the right check | `404` | `EditingEndpointBuilder.cs:70-74, 81-85` |
| `selectbyid/{id}` with no matching model | `404` | `MlModelEndpoint.cs:112-115` |
| `byid` with no matching model | `404` | `TenantableEndpointHandlerFactory.cs:101-105` |
| `download/{tenantId}/{id}` export missing or expired | `404` | `TenantableEndpointHandlerFactory.cs:296-299` |
| Model not found at prediction time (`consume*`) | `500` (`ArgumentException`) | `AutoMlExecutor.cs:155-158, 177-180` |
| Unhandled exception | `500` `application/problem+json` | `Startup.cs:330-355` |
