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
        var options = new DashboardLoginOptions(
            configuration["NetPilot:Web:Username"],
            configuration["NetPilot:Web:Password"]);
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

    private static async Task<IResult> SignInAsync(HttpContext context, IAntiforgery antiforgery, DashboardLoginOptions options)
    {
        if (!await ValidAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var remember = form["remember"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!DashboardAccess.VerifyUsername(options.Username, form["username"]) ||
            !DashboardAccess.VerifyPassword(options.Password, form["password"]))
            return Form(context, antiforgery, "login", "Wrong username or password");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, options.Username)], Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            // Remember me keeps a persistent 30-day cookie; unchecked falls back to a session cookie.
            IsPersistent = remember
        });
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
        var fields = action == "login" ? """
                <div class="lp-field">
                    <label for="username">Username</label>
                    <input id="username" name="username" type="text" autocomplete="username" autocapitalize="none" spellcheck="false" required autofocus>
                </div>
                <div class="lp-field">
                    <label for="password">Password</label>
                    <input id="password" name="password" type="password" autocomplete="current-password" required>
                </div>
                <div class="lp-remember">
                    <input id="remember" name="remember" type="checkbox" value="true" checked>
                    <div>
                        <label for="remember">Remember me</label>
                        <small>Keeps you signed in for 30 days on this device.</small>
                    </div>
                </div>
                <button type="submit">{title}</button>
                """ : """
                <p class="lp-body">You are signed in to this NetPilot dashboard.</p>
                <button type="submit">{title}</button>
                <a class="lp-cancel" href="/">Cancel</a>
                """;
        var html = $"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{title} · NetPilot</title><link rel="icon" href="/favicon.png">
            <meta name="theme-color" media="(prefers-color-scheme: light)" content="#f6faf7">
            <meta name="theme-color" media="(prefers-color-scheme: dark)" content="#0e1512">
            <style>{LoginCss}</style></head>
            <body>
            <main class="lp-card">
                <header class="lp-brand"><span class="brand-mark">n<span>p</span></span><span class="lp-wordmark">NetPilot</span></header>
                <h1>{title}</h1>
                {(error is null ? "" : $"<p role=\"alert\" class=\"lp-error\">{encode.Encode(error)}</p>")}
                <form method="post" action="/{action}">
                    <input type="hidden" name="{encode.Encode(token.FormFieldName)}" value="{encode.Encode(token.RequestToken!)}">
                    <input type="hidden" name="returnUrl" value="{encode.Encode(returnUrl)}">
                    {fields}
                </form>
            </main>
            </body></html>
            """;
        return Results.Content(html.Replace("{title}", title), "text/html; charset=utf-8",
            statusCode: error is null ? 200 : 401);
    }

    // Self-contained NetPilot sign-in styling: Material-3 palette from NetPilotTheme/AppShell,
    // centered card, dark mode via prefers-color-scheme (no JS/theme assets behind auth).
    private const string LoginCss = """
        * { box-sizing: border-box; }
        html, body { margin: 0; }
        body {
            min-block-size: 100dvh;
            display: grid;
            place-items: center;
            padding: 24px;
            font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            color: var(--lp-text);
            background: var(--lp-bg);
            color-scheme: light dark;
        }
        .lp-card {
            inline-size: min(400px, 100%);
            background: var(--lp-surface);
            border: 1px solid var(--lp-line);
            border-radius: 16px;
            padding: 32px 30px 28px;
            box-shadow: var(--lp-shadow);
        }
        .lp-brand { display: flex; justify-content: center; align-items: center; gap: 12px; margin-block-end: 22px; }
        .brand-mark {
            inline-size: 44px; block-size: 44px;
            background: var(--lp-primary); color: var(--lp-on-primary);
            display: flex; align-items: center; justify-content: center;
            letter-spacing: -5px; padding-inline-end: 7px;
            border-radius: 14px; font-size: 25px;
        }
        .brand-mark span { transform: translateY(4px); }
        .lp-wordmark { font-size: 23px; font-weight: 650; }
        .lp-card h1 { font-size: 1.375rem; font-weight: 650; text-align: center; margin: 0 0 18px; }
        .lp-error {
            background: var(--lp-error-container); color: var(--lp-error);
            border-radius: 10px; padding: 10px 14px; font-size: 14px;
            margin: 0 0 18px; text-align: center;
        }
        form { display: flex; flex-direction: column; gap: 16px; }
        .lp-field label, .lp-remember label { display: block; font-size: 13px; font-weight: 550; color: var(--lp-secondary); margin-block-end: 6px; }
        .lp-field input {
            inline-size: 100%; padding: 12px 14px;
            border: 1px solid var(--lp-line); border-radius: 12px;
            background: var(--lp-field); color: var(--lp-text);
            font: inherit;
        }
        .lp-field input:focus { outline: 2px solid var(--lp-primary); outline-offset: 1px; border-color: transparent; }
        .lp-remember { display: flex; align-items: center; gap: 10px; }
        .lp-remember input { flex: none; inline-size: 18px; block-size: 18px; accent-color: var(--lp-primary); }
        .lp-remember small { display: block; font-size: 12px; color: var(--lp-secondary); margin-block-start: 2px; }
        form button {
            padding: 12px; border: 0; border-radius: 12px;
            background: var(--lp-primary); color: var(--lp-on-primary);
            font: inherit; font-weight: 600; cursor: pointer; margin-block-start: 4px;
        }
        form button:hover { background: var(--lp-primary-strong); }
        .lp-body { font-size: 15px; color: var(--lp-secondary); margin: 0; }
        .lp-cancel { text-align: center; font-size: 14px; color: var(--lp-secondary); text-decoration: none; }
        .lp-cancel:hover { text-decoration: underline; }
        @media (prefers-color-scheme: dark) {
            body { background: #0e1512; color: #dee4e0; }
            .lp-card {
                background: #171d1a; border-color: #3f4945;
                box-shadow: 0 10px 30px rgb(0 0 0 / 0.5);
            }
            .brand-mark { background: #80d5c5; color: #00382f; }
            .lp-field label, .lp-remember label, .lp-remember small, .lp-body, .lp-cancel { color: #bec9c2; }
            .lp-field input { background: #0e1512; border-color: #3f4945; color: #dee4e0; }
            .lp-field input:focus { outline-color: #80d5c5; }
            .lp-remember input { accent-color: #80d5c5; }
            form button { background: #80d5c5; color: #00382f; }
            form button:hover { background: #a3e3d6; }
            .lp-error { background: #730e1c; color: #ffdadc; }
        }
        :root {
            --lp-bg: #f6faf7;
            --lp-surface: #ffffff;
            --lp-field: #ffffff;
            --lp-text: #171d1b;
            --lp-secondary: #3f4945;
            --lp-line: #bec9c2;
            --lp-primary: #006a60;
            --lp-on-primary: #ffffff;
            --lp-primary-strong: #00584b;
            --lp-error: #690005;
            --lp-error-container: #ffdadc;
            --lp-shadow: 0 10px 30px rgb(0 0 0 / 0.08);
        }
        """;
}
