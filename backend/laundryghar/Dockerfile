# syntax=docker/dockerfile:1
# ─────────────────────────────────────────────────────────────────────────────
# Parameterized Dockerfile for every LaundryGhar backend host.
# Build context: backend/laundryghar (this directory).
#
#   docker build --build-arg PROJECT=core.WebApi         -t laundryghar-core .
#   docker build --build-arg PROJECT=operations.WebApi   -t laundryghar-operations .
#   docker build --build-arg PROJECT=commerce.WebApi     -t laundryghar-commerce .
#   docker build --build-arg PROJECT=laundryghar.Gateway -t laundryghar-gateway .
#
# Runtime listens on :8080 (aspnet image default, non-root `app` user).
# All configuration comes from environment variables — see PRODUCTION_ENV.md.
# ─────────────────────────────────────────────────────────────────────────────

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src

# The solution is a dense project graph (SharedDataModel/Utilities/ServiceDefaults are
# referenced by every host), so copy the whole backend context and publish one project.
COPY . .
RUN dotnet restore "$PROJECT/$PROJECT.csproj"
RUN dotnet publish "$PROJECT/$PROJECT.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG PROJECT

# curl is needed only for the container HEALTHCHECK.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER app

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    HealthChecks__Expose=true \
    ENTRY_DLL=$PROJECT.dll

# /alive is the liveness probe mapped by ServiceDefaults (HealthChecks__Expose=true).
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/alive || exit 1

ENTRYPOINT ["/bin/sh", "-c", "exec dotnet $ENTRY_DLL"]
