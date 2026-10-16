using NetPilot.Core.Security;

namespace NetPilot.Core.Tests;

public class DashboardAccessTests
{
    [Theory]
    [InlineData("shared", "shared", true)]
    [InlineData("shared", "Shared", false)]
    [InlineData("shared", "", false)]
    [InlineData("shared", null, false)]
    [InlineData(null, "shared", false)]
    [InlineData("", "", false)]
    [InlineData(null, null, false)]
    [InlineData("كلمة مرور 🔑", "كلمة مرور 🔑", true)]
    [InlineData(" shared ", "shared", false)]
    public void PasswordMustMatchExactlyAndCannotBeMissing(string? expected, string? supplied, bool matches) =>
        Assert.Equal(matches, DashboardAccess.VerifyPassword(expected, supplied));

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/devices?name=a%20b#details", "/devices?name=a%20b#details")]
    [InlineData("https://evil.test", "/")]
    [InlineData("//evil.test", "/")]
    [InlineData("/\\evil.test", "/")]
    [InlineData("devices", "/")]
    [InlineData(" /devices", "/")]
    [InlineData("/ok\r\nLocation: evil", "/")]
    [InlineData("/ok\u0000", "/")]
    public void RedirectsStayOnThisOrigin(string? supplied, string expected) =>
        Assert.Equal(expected, DashboardAccess.LocalReturnUrl(supplied));

    [Theory]
    [InlineData("GET", "/login", true)]
    [InlineData("POST", "/login", true)]
    [InlineData("POST", "/logout", true)]
    [InlineData("POST", "/api/notifications/action", true)]
    [InlineData("GET", "/api/notifications/action", false)]
    [InlineData("GET", "/sw.js", true)]
    [InlineData("HEAD", "/manifest.webmanifest", true)]
    [InlineData("GET", "/icons/icon-192.png", true)]
    [InlineData("GET", "/favicon.png", true)]
    [InlineData("GET", "/app.css", true)]
    [InlineData("GET", "/lib/bootstrap/dist/css/bootstrap.min.css", true)]
    [InlineData("GET", "/_framework/blazor.web.js", false)]
    [InlineData("GET", "/_blazor", false)]
    [InlineData("POST", "/_blazor/negotiate", false)]
    [InlineData("GET", "/login/extra", false)]
    [InlineData("GET", "/icons-other/private", false)]
    [InlineData("POST", "/icons/icon.png", false)]
    [InlineData("PUT", "/login", false)]
    [InlineData("GET", "/private.js", false)]
    [InlineData("GET", "/", false)]
    public void OnlyLoginAssetsAndTokenActionArePublic(string method, string path, bool expected) =>
        Assert.Equal(expected, DashboardAccess.IsPublicPath(method, path));
}
