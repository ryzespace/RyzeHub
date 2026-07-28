using Microsoft.Extensions.Logging;

namespace RyzeHub.Application.Hub;

public sealed record DockerTemplate(string Image, string BuildCommand, string RunCommand);

/// <summary>
/// Generates Dockerfiles and a docker-compose file for repositories cloned into the hub.
/// </summary>
public sealed class DockerManager(ILogger<DockerManager> logger, string hubDirectory)
{
    private static readonly Dictionary<string, DockerTemplate> Templates = new(StringComparer.Ordinal)
    {
        ["dotnet"] = new(
            "mcr.microsoft.com/dotnet/sdk:10.0",
            "RUN dotnet restore && dotnet publish -c Release -o /app/publish",
            "CMD [\"dotnet\", \"/app/publish/app.dll\"]"),
        ["python"] = new(
            "python:3.13-slim",
            "RUN pip install --no-cache-dir -r requirements.txt || echo 'No requirements.txt found'",
            "CMD [\"python\", \"main.py\"]"),
        ["nodejs"] = new(
            "node:22-slim",
            "RUN npm ci || npm install || echo 'No package.json found'",
            "CMD [\"npm\", \"start\"]"),
        ["rust"] = new("rust:1.83-slim", "RUN cargo build --release", "CMD [\"./target/release/app\"]"),
        ["golang"] = new("golang:1.23-alpine", "RUN go build -o app .", "CMD [\"./app\"]"),
        ["java"] = new("eclipse-temurin:21-jdk", "RUN ./mvnw -q -DskipTests package || mvn -q -DskipTests package", "CMD [\"java\", \"-jar\", \"target/app.jar\"]"),
        ["generic"] = new("debian:bookworm-slim", string.Empty, "CMD [\"sh\"]")
    };

    public string DetectLanguage(string repositoryPath)
    {
        string[] files;
        try
        {
            files = [.. Directory.EnumerateFiles(repositoryPath).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "generic";
        }

        if (files.Any(file => file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            || Directory.EnumerateFiles(repositoryPath, "*.csproj", SearchOption.AllDirectories).Any())
        {
            return "dotnet";
        }

        if (files.Contains("requirements.txt", StringComparer.Ordinal)
            || files.Contains("pyproject.toml", StringComparer.Ordinal)
            || files.Contains("setup.py", StringComparer.Ordinal))
        {
            return "python";
        }

        if (files.Contains("package.json", StringComparer.Ordinal))
        {
            return "nodejs";
        }

        if (files.Contains("Cargo.toml", StringComparer.Ordinal))
        {
            return "rust";
        }

        if (files.Contains("go.mod", StringComparer.Ordinal))
        {
            return "golang";
        }

        return files.Contains("pom.xml", StringComparer.Ordinal) ? "java" : "generic";
    }

    public bool GenerateDockerfile(string repositoryPath, string language)
    {
        var dockerfilePath = Path.Combine(repositoryPath, "Dockerfile");

        if (File.Exists(dockerfilePath))
        {
            logger.LogDebug("Dockerfile already exists at {DockerfilePath}", dockerfilePath);
            return true;
        }

        var template = Templates.GetValueOrDefault(language, Templates["generic"]);
        var content = $"""
            FROM {template.Image}
            WORKDIR /app
            COPY . .
            {template.BuildCommand}
            {template.RunCommand}

            """;

        try
        {
            File.WriteAllText(dockerfilePath, content);
            logger.LogInformation("Generated Dockerfile at {DockerfilePath}", dockerfilePath);
            return true;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to write Dockerfile");
            return false;
        }
    }

    public bool GenerateDotnetDockerfile(string repositoryPath)
    {
        var dockerfilePath = Path.Combine(repositoryPath, "Dockerfile");
        if (File.Exists(dockerfilePath))
        {
            return true;
        }

        const string Content = """
            # Build stage
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            WORKDIR /src
            COPY . .
            RUN dotnet restore
            RUN dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false

            # Runtime stage
            FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
            WORKDIR /app
            COPY --from=build /app/publish .
            RUN adduser --disabled-password --gecos "" --uid 1000 appuser
            USER appuser
            ENTRYPOINT ["dotnet", "app.dll"]

            """;

        try
        {
            File.WriteAllText(dockerfilePath, Content);
            logger.LogInformation("Generated .NET multi-stage Dockerfile");
            return true;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to write Dockerfile");
            return false;
        }
    }

    public bool CreateDockerCompose(IReadOnlyDictionary<string, string> clonedRepositories)
    {
        var services = string.Concat(clonedRepositories.Select(pair => $"""
              {pair.Key.ToLowerInvariant().Replace('.', '-')}:
                build: {pair.Value}
                container_name: hub_{pair.Key.ToLowerInvariant().Replace('.', '-')}
                restart: unless-stopped

            """));

        var composePath = Path.Combine(hubDirectory, "docker-compose.yml");

        try
        {
            Directory.CreateDirectory(hubDirectory);
            File.WriteAllText(composePath, $"services:\n{services}");
            logger.LogInformation("Generated docker-compose.yml at {ComposePath}", composePath);
            return true;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to write docker-compose.yml");
            return false;
        }
    }

    public IReadOnlyDictionary<string, string> ProcessRepositories(IReadOnlyList<(string Name, string Path)> repositories)
    {
        var processed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, path) in repositories)
        {
            var language = DetectLanguage(path);
            logger.LogInformation("Detected language for {Repository}: {Language}", name, language);

            var success = language == "dotnet"
                ? GenerateDotnetDockerfile(path)
                : GenerateDockerfile(path, language);

            if (success)
            {
                processed[name] = path;
            }
        }

        if (processed.Count > 1)
        {
            CreateDockerCompose(processed);
        }

        return processed;
    }
}
