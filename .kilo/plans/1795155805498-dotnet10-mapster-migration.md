# Migration ballware-ml-service: .NET 8 → .NET 10 + AutoMapper → Mapster

Referenz-Umsetzung: `ballware-meta-service` (gleiche Struktur, bereits auf net10.0 + Mapster 7.4.0 migriert).

## Kontext (verifiziert)

- 15 csproj (12 src / 3 test) Zielen aktuell auf `net8.0`.
- AutoMapper 13.0.1 in `Ballware.Ml.Data.Ef` und `Ballware.Ml.Service`; `AddAutoMapper` in `Startup.cs:249` + 4 Test-Registrierungen; Profile: `StorageMappingProfile`, `MetaServiceMlMetadataProfile`.
- `ballware.shared.data.ef` v1.2.0 (stable, net10.0) nutzt `MapsterMapper.IMapper` in `BaseRepository`/`TenantableBaseRepository` — Diff 0.1.0→1.2.0 ist minimal (Mapper-Using + TFM + Package-Updates). ml-service muss daher zwingend auf Shared 1.x + Mapster wechseln.
- Shared-Repo-Tag `v1.2.0` existiert; stabile 1.2.0-Pakete für `Ballware.Shared.Data`, `Data.Ef`, `Authorization`, `Authorization.Jint` verifiziert. Für `Api.Endpoints`/`Api.Jobs` ist nur alpha verifiziert → Fallbacks unten.
- `ballware.meta.service.client` 1.4.0 (Tag v1.4.0 existiert): benötigte Methoden (`TenantServiceMetadataAsync`, `EntityServiceMetadataForTenantByIdentifierAsync`, `ExportCreateForTenantBehalfOfUserAsync`, `ExportFetchForTenantByIdAsync`, `JobCreateForTenantBehalfOfUserAsync`, `JobUpdateForTenantBehalfOfUserAsync`) und `CustomScripts.ExtendedRightsCheck` wurden per DLL-Inspektion gegen 1.2.0 verglichen — identische API-Oberfläche.
- Duende 4.x API-Adaption ist nötig (meta-service zeigt das Muster: `ClientId/ClientSecret/Scope/ClientCredentialsClientName.Parse`).
- GitHub-Packages-Feed `https://nuget.pkg.github.com/ballware` wird für Restore benötigt (CI konfiguriert ihn explizit; lokal vorhanden).

## Entscheidung (mit User abgestimmt)

Pinning: **stabile Versionen, wo verfügbar** (Shared-Pakete 1.2.0, meta.client 1.4.0, storage.client 9.3.1); Fallback auf meta-service-Pins, falls ein Package nicht als stabil published ist:
- `ballware.shared.api.endpoints` → `1.2.0-alpha.14` (meta-Pin)
- `ballware.shared.api.jobs` → `1.1.0`
- EF-Pakete: meta-Pins 10.0.2/10.0.3 verwenden.

## Aufgaben

### 1. TFM-Upgrade
Alle 15 csproj: `net8.0` → `net10.0` (`src/Ballware.Ml.*` + `test/Ballware.Ml.*`).

### 2. Package-Versionen pro Projekt

**Ballware.Ml.Data**: `ballware.shared.data` 0.1.0 → 1.2.0
**Ballware.Ml.Metadata**: `Ballware.Shared.Authorization` 0.1.0 → 1.2.0
**Ballware.Ml.Data.Ef**: `AutoMapper` entfernen → `Mapster` 7.4.0; `ballware.shared.data.ef` 0.1.0 → 1.2.0; `Microsoft.EntityFrameworkCore.Design` 8.0.17 → 10.0.3
**Ballware.Ml.Data.Ef.Postgres**: `EFCore.NamingConventions` → 10.0.1; Design → 10.0.3; `Microsoft.Extensions.Hosting` → 10.0.3; `Npgsql` 8.0.7 → 10.0.1; `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11 → 10.0.0
**Ballware.Ml.Data.Ef.SqlServer**: `EFCore.NamingConventions` → 10.0.1; `Microsoft.Data.SqlClient` 5.2.2 → 6.1.4; Design → 10.0.3; `Microsoft.EntityFrameworkCore.SqlServer` 8.0.17 → 10.0.2; Hosting → 10.0.3
**Ballware.Ml.Engine**: `Microsoft.Extensions.Primitives` 8.0.0 → 10.0.3
**Ballware.Ml.Engine.AutoMl**: `Microsoft.Extensions.Logging.Abstractions` → 10.0.3; `Newtonsoft.Json` 13.0.3 → 13.0.4; `Microsoft.ML.AutoML` 0.21.1 belassen (neueste Version; Kompatibilität siehe Risiken); **redundanten `<Reference Include="Ballware.Ml.Metadata">` mit HintPath `bin\Debug\net8.0\...` entfernen** (ProjectReference existiert bereits, net8-Path bricht)
**Ballware.Ml.Api**: `ballware.shared.api.endpoints` 0.1.0 → 1.2.0 (Fallback 1.2.0-alpha.14); `ballware.shared.authorization` 0.1.0 → 1.2.0; `Newtonsoft.Json` → 13.0.4; Mapster 7.4.0 hinzufügen (TenantEndpoint nutzt `IMapper`); Quartz 3.8.1 bleibt
**Ballware.Ml.Jobs**: `ballware.shared.api.jobs` 0.1.0 → 1.2.0 (Fallback 1.1.0); `Configuration.Abstractions` → 10.0.3; `DependencyInjection.Abstractions` 8.0.2 → 10.0.3; `Newtonsoft.Json` → 13.0.4; Quartz 3.8.1 bleibt
**Ballware.Ml.Caching**: `Caching.Abstractions`/`Logging.Abstractions`/`Options` → 10.0.3
**Ballware.Ml.Service**: `AutoMapper` entfernen → `Mapster` 7.4.0 + `Mapster.DependencyInjection` 1.0.1; `ballware.meta.service.client` 1.2.0 → 1.4.0; `Ballware.Generic.Service.Client` 1.3.0-alpha.2 → 1.3.0-alpha.4; `Ballware.Shared.Authorization.Jint` 0.1.0 → 1.2.0; `Ballware.Storage.Service.Client` 9.3.0 → 9.3.1; `Duende.AccessTokenManagement`(.OpenIdConnect) 3.1.1 → 4.1.1; `JwtBearer` 8.0.11 → 10.0.3; Design → 10.0.3; `Caching.StackExchangeRedis` 8.0.16 → 10.0.3; DI.Abstractions → 10.0.3; `Npgsql.EntityFrameworkCore.PostgreSQL` → 10.0.0; `Serilog.AspNetCore` 8.0.3 → 10.0.0; `Serilog.Sinks.Console` 6.0.0 → 6.1.1 (Serilog 4.3.0 bleibt); `Swashbuckle.AspNetCore`(.Annotations) 7.2.0 → 10.1.3; `System.Linq.Async` 6.0.1 → 7.0.0; Quartz-Pakete 3.8.1 bleiben
**Ballware.Ml.Service.Client**: `Newtonsoft.Json` → 13.0.4
**Tests (Api.Tests, Data.Ef.Postgres.Tests, Data.Ef.SqlServer.Tests)**: `Microsoft.AspNetCore.Mvc.Testing`/`TestHost` 8.0.11 → 10.0.3; `Mapster.DependencyInjection` 1.0.1 + `Mapster` 7.4.0 hinzufügen; `Dapper` 2.1.35 → 2.1.66; NUnit/Testcontainers/Moq/coverlet bleiben

### 3. Mapster-Code-Migration (Muster = meta-service)

1. `src/Ballware.Ml.Data.Ef/Mapping/StorageMappingProfile.cs`: `Profile` → `IRegister`
   ```csharp
   public void Register(TypeAdapterConfig config)
   {
       config.NewConfig<Public.MlModel, Persistables.MlModel>()
           .Ignore(dest => dest.Id).Map(dest => dest.Uuid, src => src.Id);
       config.NewConfig<Persistables.MlModel, Public.MlModel>()
           .Map(dest => dest.Id, src => src.Uuid);
   }
   ```
2. `src/Ballware.Ml.Data.Ef/MapperConfigurationExtensions.cs`: Extension auf `TypeAdapterConfig` (`AddBallwareMlStorageMappings`), wie `AddBallwareStorageMappings` in meta: `new StorageMappingProfile().Register(configuration); return configuration;`
3. `src/Ballware.Ml.Service/Mappings/MetaServiceMlMetadataProfile.cs`: `Profile` → `IRegister`
   ```csharp
   config.NewConfig<Ballware.Meta.Service.Client.ServiceEntity, EntityMetadata>()
       .Map(dest => dest.RightsCheckScript, src => src.CustomScripts.ExtendedRightsCheck);
   config.NewConfig<Ballware.Meta.Service.Client.ServiceTenant, Tenant>();
   config.NewConfig<Ballware.Shared.Api.Public.JobStates, Ballware.Meta.Service.Client.JobStates>();
   config.NewConfig<Ballware.Meta.Service.Client.JobStates, Ballware.Shared.Api.Public.JobStates>();
   ```
4. `src/Ballware.Ml.Service/Startup.cs:249-253`: `AddAutoMapper(...)` ersetzen durch meta-Muster (`Startup.cs:292-300`):
   ```csharp
   var mapsterConfig = new TypeAdapterConfig().AddBallwareMlStorageMappings();
   new MetaServiceMlMetadataProfile().Register(mapsterConfig);
   Services.AddSingleton(mapsterConfig);
   Services.AddScoped<IMapper, ServiceMapper>();   // MapsterMapper.ServiceMapper
   ```
5. `using AutoMapper;` → `using MapsterMapper;` in: `MlModelBaseRepository.cs`, `MlModelRepository.cs` (SqlServer + Postgres), `TenantEndpoint.cs`, `MetaServiceMetadataAdapter.cs`. `Mapper.Map<T>()`-Aufrufe bleiben unverändert (IMapper-Interface identisch nutzbar).
6. Tests: `AddAutoMapper(...)` in `RepositoryBaseTest.cs` (Postgres:82, SqlServer:82) und `EfMigrationsTest.cs` (Postgres:24, SqlServer:26) ersetzen durch:
   ```csharp
   var mapsterConfig = new TypeAdapterConfig().AddBallwareMlStorageMappings();
   PreparedBuilder.Services.AddSingleton(mapsterConfig);
   PreparedBuilder.Services.AddScoped<IMapper, ServiceMapper>();
   ```

### 4. Duende 4.x-Adaption (`Startup.cs`)

Muster aus meta-service `Startup.cs:229-273`:
- `AddClientCredentialsTokenManagement().AddClient("meta"/"storage"/"generic", ...)` bleibt, aber `client.ClientId = ClientId.Parse(...)`, `client.ClientSecret = ClientSecret.Parse(...)`, `client.Scope = Scope.Parse(...)` (stark typisierte Strings aus den Options), `TokenEndpoint` als `Uri`.
- `AddClientCredentialsTokenHandler("meta")` → `AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse("meta"))` (3 Stellen: meta/storage/generic).
- `using Duende.AccessTokenManagement;` ergänzen.

### 5. Build-/Infra-Anpassungen

- `src/Ballware.Ml.Service/Dockerfile`: `aspnet:8.0-bookworm-slim` → `mcr.microsoft.com/dotnet/aspnet:10.0`, `sdk:8.0-bookworm-slim` → `mcr.microsoft.com/dotnet/sdk:10.0` (meta-Muster).
- `.github/workflows/publish-packages.yml`: Swagger-Pfad `./bin/Release/net8.0/Ballware.Ml.Service.dll` → `net10.0` (SDK-Image und Swashbuckle.Cli 10.1.3 sind bereits korrekt).
- `src/Ballware.Ml.Service.Client/nswag.json`: `"runtime": "Net80"` → `"Net100"`.
- `README.md` Zeile 17: `.NET 8` → `.NET 10`, `AutoMapper` → `Mapster`.
- Sonar-Workflow ist bereits auf 10.0 — keine Änderung.

### 6. Validierung

1. `dotnet restore ballware-ml-service.sln` (GitHub-Packages-Feed erforderlich; fehlende 1.2.0-Stable-Pakete → dokumentierte Fallbacks einsetzen).
2. `dotnet build ballware-ml-service.sln -c Release`.
3. `dotnet test test/Ballware.Ml.Api.Tests`.
4. `dotnet test test/Ballware.Ml.Data.Ef.Postgres.Tests` und `...SqlServer.Tests` (Docker/Testcontainers erforderlich).
5. EF-Migrationen unverändert lassen; nach Upgrade pro Provider prüfen: `dotnet ef migrations has-pending-model-changes` (SqlServer + Postgres). Erwartung: keine neuen Model-Änderungen; bestehende Initial-Migrationen bleiben gültig (meta-Verfahren: neue Migrations nur bei Modell-Diffs).
6. Optional: Service-Start lokal (appsettings) zum Smoke-Test der Duende-4-Token-Flows und Mapster-Mappings.

## Risiken / offene Punkte

- `Microsoft.ML.AutoML` 0.21.1 ist die aktuelle Version (Microsoft.ML 3.3, net6-Ära) — Build-/Lauffähigkeit unter net10 in Schritt 2/6 verifizieren; kein Upgrade-Pfad vorhanden.
- Verfügbarkeit stabiler `ballware.shared.api.endpoints`/`api.jobs` 1.2.0 in GitHub Packages nicht garantiert → Fallback-Pins oben.
- Duende 4.x: Verhalten der Client-Credentials-Flows gegen Keycloak-Konfiguration testen (Token-Caching/Scope-Semantik geändert).
- EF-Core-10-Snapshots der bestehenden Migrations bleiben EF8-generiert; erst die nächste neue Migration wird unter EF 10 designt — keine Aktion nötig, aber in Schritt 5 bestätigen.
