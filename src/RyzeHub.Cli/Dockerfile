# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/RyzeHub.Domain/RyzeHub.Domain.csproj src/RyzeHub.Domain/
COPY src/RyzeHub.Application/RyzeHub.Application.csproj src/RyzeHub.Application/
COPY src/RyzeHub.Contracts/RyzeHub.Contracts.csproj src/RyzeHub.Contracts/
COPY src/RyzeHub.Infrastructure/RyzeHub.Infrastructure.csproj src/RyzeHub.Infrastructure/
COPY src/RyzeHub.Cli/RyzeHub.Cli.csproj src/RyzeHub.Cli/
RUN dotnet restore src/RyzeHub.Cli/RyzeHub.Cli.csproj

COPY . .
RUN dotnet publish src/RyzeHub.Cli/RyzeHub.Cli.csproj \
    -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN adduser --disabled-password --gecos "" --uid 1000 ryzehub
COPY --from=build /app/publish .
USER ryzehub

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD ["/app/ryzehub", "health"]

ENTRYPOINT ["/app/ryzehub"]
CMD ["--help"]
