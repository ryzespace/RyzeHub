using RyzeHub.Application.Hub;

namespace RyzeHub.UnitTests;

public sealed class HubCatalogTests
{
    [Fact]
    public void ResolvesCanonicalNamesFromAliases()
    {
        HubCatalog.ResolveCanonicalRepositoryName("@ryzespace/client").Should().Be("RyzeSpace.Client");
        HubCatalog.ResolveCanonicalRepositoryName("help-center").Should().Be("RyzeSpace.HelpCenter");
        HubCatalog.ResolveCanonicalRepositoryName("ryzeauth").Should().Be("RyzeAuth");
        HubCatalog.ResolveCanonicalRepositoryName("totally-unknown").Should().BeNull();
    }

    [Fact]
    public void TracksRyzeAuthAsAnActiveRepository()
    {
        HubCatalog.ActiveRepositoryNames().Should().Contain("RyzeAuth");
        HubCatalog.FutureRepositoryNames().Should().Contain("RyzeSpace.AdminPanel");
        HubCatalog.TrackedRepositoryNames().Should().HaveCount(6);
    }

    [Fact]
    public void DependencyIdentifiersIncludeAliasesAndDerivedForms()
    {
        var identifiers = HubCatalog.DependencyIdentifiers("RyzeSpace.Client");

        identifiers.Should().Contain("RyzeSpace.Client");
        identifiers.Should().Contain("@ryzespace/client");
    }
}

public sealed class DependencyGraphTests
{
    [Fact]
    public void TopologicalSortOrdersDependenciesFirst()
    {
        var graph = new DependencyGraph();
        graph.AddRepository("lib-a", []);
        graph.AddRepository("lib-b", ["lib-a"]);
        graph.AddRepository("app", ["lib-b"]);

        var order = graph.TopologicalSort();

        order.Should().NotBeNull();
        order!.Should().HaveCount(3);

        var ordered = order.ToList();
        ordered.IndexOf("lib-a").Should().BeLessThan(ordered.IndexOf("lib-b"));
        ordered.IndexOf("lib-b").Should().BeLessThan(ordered.IndexOf("app"));
    }

    [Fact]
    public void TopologicalSortReturnsNullOnCycles()
    {
        var graph = new DependencyGraph();
        graph.AddRepository("a", ["b"]);
        graph.AddRepository("b", ["a"]);

        graph.TopologicalSort().Should().BeNull();
    }

    [Fact]
    public void DependentsAreResolved()
    {
        var graph = new DependencyGraph();
        graph.AddRepository("client", ["RyzeAuth"]);
        graph.AddRepository("helpcenter", ["RyzeAuth"]);

        graph.GetDependents("RyzeAuth").Should().BeEquivalentTo(new[] { "client", "helpcenter" });
    }
}

public sealed class DependencyManagerTests
{
    [Fact]
    public void FindsDependenciesInPackageJsonAliases()
    {
        var directory = Directory.CreateTempSubdirectory("ryzehub-deps");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "package.json"), """
                {
                  "dependencies": {
                    "@ryzespace/client": "workspace:*",
                    "@ryzespace/helpcenter": "^1.0.0"
                  }
                }
                """);

            var manager = new DependencyManager(
                TestSupport.Logger<DependencyManager>(),
                ["RyzeSpace.Client", "RyzeSpace.HelpCenter"]);

            manager.FindInternalDependencies(directory.FullName)
                .Should().Contain(new[] { "RyzeSpace.Client", "RyzeSpace.HelpCenter" });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void FindsRyzeAuthDependencyInProjectFiles()
    {
        var directory = Directory.CreateTempSubdirectory("ryzehub-deps-dotnet");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "Service.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="RyzeAuth.Contracts" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """);

            var manager = new DependencyManager(TestSupport.Logger<DependencyManager>(), ["RyzeAuth"]);

            manager.FindInternalDependencies(directory.FullName).Should().Contain("RyzeAuth");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}

public sealed class DockerManagerTests
{
    [Fact]
    public void DetectsDotnetProjects()
    {
        var directory = Directory.CreateTempSubdirectory("ryzehub-docker-dotnet");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "App.csproj"), "<Project/>");

            var manager = new DockerManager(TestSupport.Logger<DockerManager>(), directory.FullName);
            manager.DetectLanguage(directory.FullName).Should().Be("dotnet");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void GeneratesDotnetDockerfile()
    {
        var directory = Directory.CreateTempSubdirectory("ryzehub-docker-gen");
        try
        {
            var manager = new DockerManager(TestSupport.Logger<DockerManager>(), directory.FullName);

            manager.GenerateDotnetDockerfile(directory.FullName).Should().BeTrue();

            var content = File.ReadAllText(Path.Combine(directory.FullName, "Dockerfile"));
            content.Should().Contain("mcr.microsoft.com/dotnet/sdk:10.0");
            content.Should().Contain("USER appuser");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DetectsPythonProjects()
    {
        var directory = Directory.CreateTempSubdirectory("ryzehub-docker-py");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "requirements.txt"), "flask==3.0");

            var manager = new DockerManager(TestSupport.Logger<DockerManager>(), directory.FullName);
            manager.DetectLanguage(directory.FullName).Should().Be("python");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
