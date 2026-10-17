using System.Text.RegularExpressions;

namespace NetPilot.Core.Activity;

public static partial class ActivityPrivacy
{
    // Logs never need URLs or credentials; notification action tokens live only in their token store.
    public static string Redact(string text) => Credentials().Replace(Urls().Replace(text, "[link removed]"), "$1=[redacted]");

    [GeneratedRegex(@"https?://[^\s<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Urls();
    [GeneratedRegex(@"\b(token|password|authorization|secret|stok|privatekey)[""']?\s*[:=]\s*(?:""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|(?:Bearer\s+)?[^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();
}
