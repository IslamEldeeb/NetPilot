using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NetPilot.Core.Quota;

namespace NetPilot.Web;

public static class NotificationEndpoints
{
    private const string ActionPolicy = "notification-actions";

    public static IServiceCollection AddNotificationActionEndpoints(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(ActionPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                { status = "RateLimited", message = "Too many notification actions. Try again in a minute." }, ct);
            };
        });
        return services;
    }

    public static void MapNotificationActionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/notifications/action", (Delegate)ApplyAsync)
            .DisableAntiforgery().RequireRateLimiting(ActionPolicy);
    }

    private static async Task<IResult> ApplyAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = 8192;
        if (context.Request.ContentLength is > 8192)
            return Results.Json(new { status = "Invalid", message = "The notification action is invalid." }, statusCode: 400);
        if (!context.Request.HasJsonContentType())
            return Results.Json(new { status = "Invalid", message = "Send a JSON notification action." }, statusCode: 400);
        try
        {
            var request = await context.Request.ReadFromJsonAsync<ActionRequest>(context.RequestAborted);
            var handler = context.RequestServices.GetRequiredService<QuotaActionHandler>();
            var result = await handler.ApplyAsync(request?.Token, request?.Action, context.RequestAborted);
            return Results.Json(new { status = result.Status.ToString(), message = result.Message });
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or BadHttpRequestException)
        {
            return Results.Json(new { status = "Invalid", message = "Send a JSON notification action." }, statusCode: 400);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do not include exceptions: request bodies contain bearer credentials.
            return Results.Json(new { status = "Unavailable", message = "NetPilot could not complete this action. Try again." }, statusCode: 503);
        }
    }

    private sealed record ActionRequest(string? Token, string? Action);
}
