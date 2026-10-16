using System.Security.Cryptography;
using System.Text;

namespace NetPilot.Core.Security;

public static class DashboardAccess
{
    public static bool VerifyPassword(string? expected, string? supplied)
    {
        var matches = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? "")));
        return !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(supplied) && matches;
    }

    // Only root-relative paths; reject browser URL normalization and header controls.
    public static string LocalReturnUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' &&
        (url.Length == 1 || url[1] != '/') &&
        !url.Any(c => c == '\\' || char.IsControl(c)) ? url : "/";

    public static bool IsPublicPath(string method, string path)
    {
        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            return path.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/logout", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/notifications/action", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) return false;
        return path.ToLowerInvariant() is "/login" or "/logout" or "/sw.js" or
            "/manifest.webmanifest" or "/favicon.png" or "/app.css" or
            "/lib/bootstrap/dist/css/bootstrap.min.css" ||
            path.StartsWith("/icons/", StringComparison.OrdinalIgnoreCase);
    }
}
