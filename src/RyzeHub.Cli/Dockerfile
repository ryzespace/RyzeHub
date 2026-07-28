# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the manifests only, so the layer caches across source edits.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/RyzeHub.Domain/RyzeHub.Domain.csproj src/RyzeHub.Domain/
COPY src/RyzeHub.Application/RyzeHub.Application.csproj src/RyzeHub.Application/
COPY src/RyzeHub.Contracts/RyzeHub.Contracts.csproj src/RyzeHub.Contracts/
COPY src/RyzeHub.Infrastructure/RyzeHub.Infrastructure.csproj src/RyzeHub.Infrastructure/
COPY src/RyzeHub.Cli/RyzeHub.Cli.csproj src/RyzeHub.Cli/
RUN dotnet restore src/RyzeHub.Cli/RyzeHub.Cli.csproj

COPY src/ src/
RUN dotnet publish src/RyzeHub.Cli/RyzeHub.Cli.csproj \
    -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# git is needed by `ryzehub hub`, which clones the organization.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && adduser --disabled-password --gecos "" --uid 1000 ryzehub

COPY --from=build /app/publish .
USER ryzehub

# No HEALTHCHECK: this is a task container, not a long-running service.
ENTRYPOINT ["/app/ryzehub"]
CMD ["--help"]
