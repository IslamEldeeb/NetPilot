using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using NetPilot.Core.Security;

namespace NetPilot.Web;

public static class DashboardLogin
{
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    private const string LoginPolicy = "dashboard-login";

    public static void AddDashboardLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new DashboardLoginOptions(configuration["NetPilot:Web:Password"]);
        services.AddSingleton(options);
        if (!options.Enabled) return;
        services.AddAuthentication(Scheme).AddCookie(cookie =>
        {
            cookie.Cookie.Name = "netpilot.auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
            cookie.SlidingExpiration = true;
        });
        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = 1;
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
        });
        services.AddRateLimiter(limits => limits.AddPolicy(LoginPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })));
    }

    public static void UseDashboardLogin(this WebApplication app)
    {
        var enabled = app.Services.GetRequiredService<DashboardLoginOptions>().Enabled;
        app.Logger.LogInformation("Dashboard login {Mode}", enabled ? "enabled" : "disabled");
        if (!enabled) return;
        app.UseForwardedHeaders();
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true ||
                DashboardAccess.IsPublicPath(context.Request.Method, context.Request.Path.Value ?? "/"))
                await next(context);
            else
                RejectAnonymous(context);
        });
    }

    private static void RejectAnonymous(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var request = context.Request;
        var browserNavigation = (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) &&
            !request.Path.StartsWithSegments("/api") && !request.Path.StartsWithSegments("/_blazor") &&
            request.Headers.Accept.Any(h => h?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true) &&
            (request.Headers["Sec-Fetch-Mode"].Count == 0 || request.Headers["Sec-Fetch-Mode"] == "navigate");
        if (browserNavigation)
            context.Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(request.PathBase + request.Path + request.QueryString));
        else
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    public static void MapDashboardLogin(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<DashboardLoginOptions>().Enabled) return;
        app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery) =>
            Form(context, antiforgery, "login", null));
        app.MapPost("/login", (Delegate)SignInAsync).RequireRateLimiting(LoginPolicy);
        app.MapGet("/logout", (HttpContext context, IAntiforgery antiforgery) =>
            Form(context, antiforgery, "logout", null));
        app.MapPost("/logout", (Delegate)SignOutAsync);
    }

    private static async Task<IResult> SignInAsync(HttpContext context, IAntiforgery antiforgery, IConfiguration configuration)
    {
        if (!await ValidAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (!DashboardAccess.VerifyPassword(configuration["NetPilot:Web:Password"], form["password"]))
            return Form(context, antiforgery, "login", "Wrong password");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "household")], Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
        return Results.LocalRedirect(DashboardAccess.LocalReturnUrl(form["returnUrl"]));
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await ValidAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
        await context.SignOutAsync(Scheme);
        return Results.LocalRedirect("/login");
    }

    private static async Task<bool> ValidAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 16384;
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
        catch (BadHttpRequestException) { return false; }
    }

    private static IResult Form(HttpContext context, IAntiforgery antiforgery, string action, string? error)
    {
        context.Response.Headers.CacheControl = "no-store";
        var token = antiforgery.GetAndStoreTokens(context);
        var encode = HtmlEncoder.Default;
        var returnUrl = DashboardAccess.LocalReturnUrl(context.Request.HasFormContentType
            ? context.Request.Form["returnUrl"] : context.Request.Query["returnUrl"]);
        var title = action == "login" ? "Sign in" : "Sign out";
        var password = action == "login" ? """
            <label for="password" class="form-label">Password</label>
            <input id="password" name="password" type="password" autocomplete="current-password" class="form-control mb-3" required autofocus>
            """ : "";
        return Results.Content($"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{title} · NetPilot</title><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/app.css"></head>
            <body><main class="container py-5"><h1>NetPilot</h1><h2>{title}</h2><p role="alert">{encode.Encode(error ?? "")}</p>
            <form method="post" action="/{action}"><input type="hidden" name="{encode.Encode(token.FormFieldName)}" value="{encode.Encode(token.RequestToken!)}">
            <input type="hidden" name="returnUrl" value="{encode.Encode(returnUrl)}">{password}<button type="submit" class="btn btn-primary">{title}</button></form></main></body></html>
            """, "text/html; charset=utf-8", statusCode: error is null ? 200 : 401);
    }
}
