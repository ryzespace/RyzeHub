using RyzeHub.Application.Hub;

namespace RyzeHub.Api.Endpoints;

public static class HubManagementEndpoints
{
    public static IEndpointRouteBuilder MapHubManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/hub").WithTags("Hub Management").RequireAuthorization("PlatformAdmin");

        group.MapGet("/repositories", () => Results.Ok(new
        {
            active = HubCatalog.ActiveRepositoryNames(),
            future = HubCatalog.FutureRepositoryNames(),
            tracked = HubCatalog.TrackedRepositoryNames()
        })).AllowAnonymous();

        group.MapPost("/update", async (IHubManager hubManager, bool dockerize, CancellationToken cancellationToken) =>
        {
            var result = await hubManager.UpdateHubAsync(dockerize, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result)
                : Results.Json(result, statusCode: StatusCodes.Status500InternalServerError);
        });

        group.MapGet("/paths", (IHubManager hubManager) => Results.Ok(hubManager.GetRepositoryPaths()));

        return endpoints;
    }
}
