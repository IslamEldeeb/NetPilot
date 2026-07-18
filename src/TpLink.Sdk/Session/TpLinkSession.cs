namespace TpLink.Sdk.Session;

/// <summary>The router allows exactly one active session — a second login elsewhere kicks this one out.</summary>
public record TpLinkSession(string Stok, DateTimeOffset AuthenticatedAtUtc);
