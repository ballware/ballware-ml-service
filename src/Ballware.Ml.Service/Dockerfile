FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

ARG GITHUB_PAT

WORKDIR /src

COPY ./Ballware.Ml.Api ./Ballware.Ml.Api
COPY ./Ballware.Ml.Caching ./Ballware.Ml.Caching
COPY ./Ballware.Ml.Data ./Ballware.Ml.Data
COPY ./Ballware.Ml.Data.Ef ./Ballware.Ml.Data.Ef
COPY ./Ballware.Ml.Data.Ef.Postgres ./Ballware.Ml.Data.Ef.Postgres
COPY ./Ballware.Ml.Data.Ef.SqlServer ./Ballware.Ml.Data.Ef.SqlServer
COPY ./Ballware.Ml.Engine ./Ballware.Ml.Engine
COPY ./Ballware.Ml.Engine.AutoMl ./Ballware.Ml.Engine.AutoMl
COPY ./Ballware.Ml.Jobs ./Ballware.Ml.Jobs
COPY ./Ballware.Ml.Metadata ./Ballware.Ml.Metadata
COPY ./Ballware.Ml.Service ./Ballware.Ml.Service

WORKDIR "/src/Ballware.Ml.Service"
RUN dotnet nuget add source "https://nuget.pkg.github.com/ballware/index.json" --name github --username frankball --password "$GITHUB_PAT" --store-password-in-clear-text
RUN dotnet restore "Ballware.Ml.Service.csproj"
RUN dotnet build "Ballware.Ml.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ballware.Ml.Service.csproj" -c Release -o /app/publish

FROM base AS final
USER app
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Ballware.Ml.Service.dll"]