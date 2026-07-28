# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/RyzeHub.Domain/RyzeHub.Domain.csproj src/RyzeHub.Domain/
COPY src/RyzeHub.Application/RyzeHub.Application.csproj src/RyzeHub.Application/
COPY src/RyzeHub.Contracts/RyzeHub.Contracts.csproj src/RyzeHub.Contracts/
COPY src/RyzeHub.Infrastructure/RyzeHub.Infrastructure.csproj src/RyzeHub.Infrastructure/
COPY src/RyzeHub.Api/RyzeHub.Api.csproj src/RyzeHub.Api/
RUN dotnet restore src/RyzeHub.Api/RyzeHub.Api.csproj

COPY . .
RUN dotnet publish src/RyzeHub.Api/RyzeHub.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos "" --uid 1000 ryzehub
COPY --from=build /app/publish .
USER ryzehub

ENV ASPNETCORE_URLS=http://+:8082
EXPOSE 8082

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD ["dotnet", "/app/RyzeHub.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "RyzeHub.Api.dll"]
