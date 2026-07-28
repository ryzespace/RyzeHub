using Microsoft.AspNetCore.Diagnostics;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Api.Middleware;

/// <summary>Maps RyzeHub domain exceptions onto RFC 7807 problem responses.</summary>
internal static class ProblemDetailsHandler
{
    public static IApplicationBuilder UseRyzeHubProblemDetails(this IApplicationBuilder app) =>
        app.UseExceptionHandler(errors => errors.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var (status, title, detail) = Map(exception);

            context.Response.StatusCode = status;
            await Results.Problem(detail, statusCode: status, title: title).ExecuteAsync(context);
        }));

    private static (int Status, string Title, string Detail) Map(Exception? exception) => exception switch
    {
        TicketValidationException validation =>
            (StatusCodes.Status400BadRequest, "Validation failed", validation.Message),
        AuthorizationDeniedException denied =>
            (StatusCodes.Status403Forbidden, "Forbidden", denied.Message),
        AuthenticationFailedException =>
            (StatusCodes.Status401Unauthorized, "Unauthorized", "A valid token or API key is required."),
        TicketNotFoundException =>
            (StatusCodes.Status404NotFound, "Not found", "The requested resource does not exist."),
        RateLimitExceededException rateLimit =>
            (StatusCodes.Status429TooManyRequests, "Rate limited", rateLimit.Message),
        CircuitBreakerOpenException breaker =>
            (StatusCodes.Status503ServiceUnavailable, "Upstream unavailable", breaker.Message),
        ConfigurationException configuration =>
            (StatusCodes.Status500InternalServerError, "Configuration error", configuration.Message),
        _ =>
            (StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred.")
    };
}
