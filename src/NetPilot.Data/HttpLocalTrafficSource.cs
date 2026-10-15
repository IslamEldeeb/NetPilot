using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Core.Usage;

namespace NetPilot.Data;

/// <summary>Optional HTTP adapter. Failures throw and are isolated by LocalTrafficTracker.</summary>
public sealed class HttpLocalTrafficSource(HttpClient client, Uri url, string? token) : ILocalTrafficSource
{
    public async Task<LocalTrafficReading?> ReadAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        // Bound memory even if a misconfigured exporter returns an unbounded response.
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, timeout.Token);
        var payload = await JsonSerializer.DeserializeAsync<Payload>(
            await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token)
            ?? throw new JsonException("Missing LAN reading.");
        if (payload.Flows is null || payload.Flows.Any(f => f is null || f.Src is null || f.Dst is null || f.Bytes is null))
            throw new JsonException("Incomplete LAN flows.");
        return new(payload.Flows.Select(f => new LanFlowCounter(f!.Src!, f.Dst!, f.Bytes!.Value)).ToList(), payload.GeneratedAtUtc);
    }

    private sealed record Payload(
        [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset? GeneratedAtUtc,
        [property: JsonPropertyName("flows")] List<Flow?>? Flows);
    private sealed record Flow(
        [property: JsonPropertyName("src")] string? Src,
        [property: JsonPropertyName("dst")] string? Dst,
        [property: JsonPropertyName("bytes")] long? Bytes);
}
