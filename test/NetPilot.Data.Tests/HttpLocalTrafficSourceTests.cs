using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetPilot.Core.Usage;
using System.Net;
using NetPilot.Data;

namespace NetPilot.Data.Tests;

public class HttpLocalTrafficSourceTests
{
    [Fact]
    public async Task ValidResponse_IgnoresExtraFields_AndSendsTokenOnlyInHeader()
    {
        const string token = "private-test-token";
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(token, request.Headers.Authorization?.Parameter);
            Assert.DoesNotContain(token, request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"generatedAtUtc":"2026-10-15T12:00:00Z","extra":42,"flows":[{"src":"192.168.1.20","dst":"192.168.1.50","bytes":123456,"other":true}]}""") });
        });
        using var client = new HttpClient(handler);
        var source = new HttpLocalTrafficSource(client, new("http://exporter/flows"), token);
        var reading = (await source.ReadAsync(CancellationToken.None))!;
        Assert.Equal(123456, Assert.Single(reading.Flows).Bytes);
        Assert.Equal("192.168.1.20", reading.Flows[0].SrcIp);
        Assert.Equal("192.168.1.50", reading.Flows[0].DstIp);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), reading.GeneratedAtUtc);
    }

    [Theory]
    [InlineData("{", 200)]
    [InlineData("{}", 200)]
    [InlineData("{\"flows\":[{\"src\":\"192.168.1.20\",\"dst\":\"192.168.1.50\"}]}", 200)]
    [InlineData("{\"flows\":[]}", 503)]
    public async Task MalformedOrNonSuccessResponse_ThrowsForTrackerFallback(string json, int status)
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json) });
        });
        using var client = new HttpClient(handler);
        var source = new HttpLocalTrafficSource(client, new("http://exporter/flows"), null);
        var failure = await Record.ExceptionAsync(() => source.ReadAsync(CancellationToken.None));
        Assert.True(failure is System.Text.Json.JsonException or HttpRequestException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task CallerCancellationOrHttpTimeout_StopsRead(bool? callerCancellation)
    {
        using var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage();
        });
        using var client = new HttpClient(handler) { Timeout = callerCancellation == false ? TimeSpan.FromMilliseconds(30) : Timeout.InfiniteTimeSpan };
        using var cancellation = new CancellationTokenSource();
        if (callerCancellation == true) cancellation.CancelAfter(30);
        var source = new HttpLocalTrafficSource(client, new("http://exporter/flows"), "private-test-token");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(cancellation.Token));
    }

    [Fact]
    public async Task ConfiguredRegistration_RedactsBearerTokenFromTraceLogs_DefaultIsInert()
    {
        const string secret = "must-never-be-logged";
        using var disabled = new ServiceCollection().AddNetPilotLocalTraffic(new ConfigurationBuilder().Build()).BuildServiceProvider();
        Assert.IsType<NullLocalTrafficSource>(disabled.GetRequiredService<ILocalTrafficSource>());
        var logs = new RecordingLogger();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["NetPilot:LocalTraffic:Url"] = "http://exporter/flows", ["NetPilot:LocalTraffic:Token"] = secret }).Build();
        var registrations = new ServiceCollection();
        registrations.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        registrations.AddNetPilotLocalTraffic(configuration);
        registrations.AddHttpClient("LocalTraffic").ConfigurePrimaryHttpMessageHandler(() => new FakeHandler((request, _) =>
        {
            Assert.Equal(secret, request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"flows\":[]}") });
        }));
        using var services = registrations.BuildServiceProvider();
        Assert.Empty((await services.GetRequiredService<ILocalTrafficSource>().ReadAsync(CancellationToken.None))!.Flows);
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(secret));
    }

    private sealed class RecordingLogger : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
        public void Dispose() { }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
