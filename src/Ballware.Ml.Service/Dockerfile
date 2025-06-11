FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

COPY ./Ballware.Ml.Api ./Ballware.Ml.Api
COPY ./Ballware.Ml.Authorization ./Ballware.Ml.Authorization
COPY ./Ballware.Ml.Caching ./Ballware.Ml.Caching
COPY ./Ballware.Ml.Engine ./Ballware.Ml.Engine
COPY ./Ballware.Ml.Engine.AutoMl ./Ballware.Ml.Engine.AutoMl
COPY ./Ballware.Ml.Jobs ./Ballware.Ml.Jobs
COPY ./Ballware.Ml.Metadata ./Ballware.Ml.Metadata
COPY ./Ballware.Ml.Service ./Ballware.Ml.Service
COPY ./Ballware.Meta.Client ./Ballware.Meta.Client
COPY ./Ballware.Generic.Client ./Ballware.Generic.Client
COPY ./Ballware.Storage.Client ./Ballware.Storage.Client

WORKDIR "/src/Ballware.Ml.Service"
RUN dotnet build "Ballware.Ml.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ballware.Ml.Service.csproj" -c Release -o /app/publish

FROM base AS final
USER app
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Ballware.Ml.Service.dll"]