# =============================================================
# ROTA API — production image (T66)
# Multi-stage: SDK builds + publishes, slim ASP.NET runtime runs.
# Host-agnostic: ALL secrets/connection strings come from env vars
# (see docs/DEPLOYMENT.md). No secrets are baked into the image.
# =============================================================

# ---- build stage --------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first on csproj-only layers so dependency restore caches
# across source-only changes.
COPY src/ROTA.Domain/ROTA.Domain.csproj          src/ROTA.Domain/
COPY src/ROTA.Shared/ROTA.Shared.csproj          src/ROTA.Shared/
COPY src/ROTA.Application/ROTA.Application.csproj src/ROTA.Application/
COPY src/ROTA.Infrastructure/ROTA.Infrastructure.csproj src/ROTA.Infrastructure/
COPY src/ROTA.Api/ROTA.Api.csproj                src/ROTA.Api/
RUN dotnet restore src/ROTA.Api/ROTA.Api.csproj

# Icons before source, deliberately. This layer is ~28 MB and changes only on an art pass, while
# src/ changes constantly — copying the big stable thing first means an ordinary code change reuses
# the cached asset layer instead of re-sending every PNG.
#
# ROTA.Api.csproj links these in from ..\..\assets\icons, which resolves to /src/assets/icons here.
# Remove this COPY and the publish fails outright with MSB3030 — verified by building without it.
# That loud failure is worth understanding, because it is half an accident: the csproj Include names
# atlas.json EXPLICITLY alongside the **\*.png glob, and a named file that is missing is an error
# while a glob that matches nothing is not. Had it been globs alone, the image would have built
# clean, started clean, and 404'd every picture in the game, with only the startup warning in
# Program.cs to say so. Keep atlas.json named.
COPY assets/icons/ assets/icons/

COPY src/ src/
RUN dotnet publish src/ROTA.Api/ROTA.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime stage ------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Run as the image's built-in non-root user. The base image listens on 8080
# (ASPNETCORE_HTTP_PORTS=8080); TLS terminates at the host's proxy/load balancer.
USER $APP_UID
EXPOSE 8080

ENV ASPNETCORE_ENVIRONMENT=Production

# NOTE: production deployments run `dotnet ef database update` (or apply the
# idempotent SQL script) BEFORE starting this container — the app only
# auto-migrates in Development.
ENTRYPOINT ["dotnet", "ROTA.Api.dll"]
