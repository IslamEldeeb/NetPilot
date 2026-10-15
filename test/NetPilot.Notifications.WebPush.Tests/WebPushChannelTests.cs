using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NetPilot.Core.Notifications;
using NetPilot.Core.Usage;
using NetPilot.Data;

namespace NetPilot.Notifications.WebPush.Tests;

public class WebPushChannelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-webpush-{Guid.NewGuid():N}");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly NotificationMessage Message = new("Hello", "Private body", "/", "quota:test", [new("extend-1gb", "+1 GB", "opaque-action-token")]);
    public WebPushChannelTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(201, true, false, 0)]
    [InlineData(410, false, true, 0)]
    [InlineData(404, false, true, 0)]
    [InlineData(500, false, false, 1)]
    [InlineData(429, false, false, 1)]
    public async Task EncryptedVapidRequestRecordsSuccessOrCleansUpDeadSubscriptions(int status, bool success, bool removed, int failures)
    {
        using var db = new NetPilotDatabase(Path.Combine(_directory, "test.db"));
        var store = new LitePushSubscriptionStore(db, new UsagePeriodClock());
        var subscription = Subscription("one");
        await store.UpsertAsync(subscription, Ct);
        var handler = new CapturingHandler(_ => (HttpStatusCode)status);
        using var http = new HttpClient(handler);
        var client = new PushServiceClient(http);
        var channel = new WebPushChannel(client, store, Keys(db), new());
        var delivery = Assert.Single(await channel.SendToSubscriptionsAsync(Message, Ct));
        Assert.Equal(success, delivery.Success);
        Assert.Equal(subscription.Id, delivery.SubscriptionId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(subscription.Endpoint, request.Endpoint);
        Assert.StartsWith("vapid ", request.Authorization);
        Assert.Contains("t=", request.Authorization);
        Assert.Contains("k=", request.Authorization);
        Assert.Equal("aes128gcm", request.Encoding);
        Assert.Equal("86400", request.Ttl);
        Assert.True(request.Body.Length > 86);
        Assert.DoesNotContain("Private body", Encoding.UTF8.GetString(request.Body));
        var remaining = await store.GetAllAsync(Ct);
        if (removed) Assert.Empty(remaining);
        else
        {
            var saved = Assert.Single(remaining);
            Assert.Equal(failures, saved.FailureCount);
            Assert.Equal(success, saved.LastSuccessAtUtc.HasValue);
        }
    }

    [Fact]
    public async Task FailedSubscriptionDoesNotPreventLaterDelivery_AndDispatcherRecordsPartialFailure()
    {
        using var db = new NetPilotDatabase(Path.Combine(_directory, "test.db"));
        var store = new LitePushSubscriptionStore(db, new UsagePeriodClock());
        await store.UpsertAsync(Subscription("one"), Ct);
        await store.UpsertAsync(Subscription("two"), Ct);
        var handler = new CapturingHandler(endpoint => endpoint.EndsWith("one") ? HttpStatusCode.InternalServerError : HttpStatusCode.Created);
        using var http = new HttpClient(handler);
        var client = new PushServiceClient(http);
        var channel = new WebPushChannel(client, store, Keys(db), new());
        var log = new LiteNotificationLogStore(db);
        await new NotificationDispatcher([channel], log, new UsagePeriodClock(), new()).DispatchAsync(Message, Ct);
        Assert.Equal(2, handler.Requests.Count);
        var subscriptions = await store.GetAllAsync(Ct);
        Assert.Equal(1, subscriptions.Single(s => s.Endpoint.EndsWith("one")).FailureCount);
        Assert.NotNull(subscriptions.Single(s => s.Endpoint.EndsWith("two")).LastSuccessAtUtc);
        Assert.Equal(NotificationOutcome.Failed, Assert.Single(await log.GetRecentAsync(10, Ct)).Outcome);
    }

    [Fact]
    public async Task TestServiceSendsEveryTimeDuringQuietHours_AndReturnsPerSubscriptionResults()
    {
        using var db = new NetPilotDatabase(Path.Combine(_directory, "test.db"));
        var store = new LitePushSubscriptionStore(db, new UsagePeriodClock());
        await store.UpsertAsync(Subscription("one"), Ct);
        await store.UpsertAsync(Subscription("two"), Ct);
        var handler = new CapturingHandler(endpoint => endpoint.EndsWith("one") ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
        using var http = new HttpClient(handler);
        var client = new PushServiceClient(http);
        var channel = new WebPushChannel(client, store, Keys(db), new() { QuietHours = new() { Enabled = true } });
        var service = new NotificationService(channel);
        for (var i = 0; i < 2; i++)
        {
            var deliveries = await service.SendTestAsync("Phone", Ct);
            Assert.Equal(2, deliveries.Count);
            Assert.Single(deliveries, r => r.Success);
            Assert.Equal("HTTP 500", Assert.Single(deliveries, r => !r.Success).Error);
        }
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public void PushPayloadCarriesTokensPerAction_AndEmptyActionsStayEmpty()
    {
        using var payload = System.Text.Json.JsonDocument.Parse(WebPushChannel.SerializePayload(Message));
        var action = Assert.Single(payload.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal("extend-1gb", action.GetProperty("action").GetString());
        Assert.Equal("+1 GB", action.GetProperty("title").GetString());
        Assert.Equal("opaque-action-token", action.GetProperty("token").GetString());
        using var noActions = System.Text.Json.JsonDocument.Parse(WebPushChannel.SerializePayload(Message with { Actions = [] }));
        Assert.Empty(noActions.RootElement.GetProperty("actions").EnumerateArray());
    }

    private readonly List<ServiceProvider> _providers = [];
    private VapidKeyStore Keys(NetPilotDatabase db)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_directory, "keys"))).SetApplicationName("NetPilot");
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return new(db, provider.GetRequiredService<IDataProtectionProvider>());
    }

    private static PushSubscriptionInfo Subscription(string suffix)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        var point = new byte[65];
        point[0] = 4;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 33);
        var endpoint = $"https://push.example/send/{suffix}";
        return new(PushSubscriptionInfo.IdForEndpoint(endpoint), endpoint, Base64Url(point), Base64Url(RandomNumberGenerator.GetBytes(16)), "Phone", DateTimeOffset.UtcNow);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record CapturedRequest(string Endpoint, string? Authorization, string Encoding, string Ttl, byte[] Body);
    private sealed class CapturingHandler(Func<string, HttpStatusCode> response) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var endpoint = request.RequestUri!.AbsoluteUri;
            Requests.Add(new(endpoint, request.Headers.Authorization?.ToString(), string.Join(",", request.Content!.Headers.ContentEncoding),
                Assert.Single(request.Headers.GetValues("TTL")), await request.Content.ReadAsByteArrayAsync(ct)));
            var reply = new HttpResponseMessage(response(endpoint)) { Content = new StringContent("Push response") };
            if (reply.StatusCode == HttpStatusCode.TooManyRequests)
                reply.Headers.RetryAfter = new(TimeSpan.FromHours(1));
            return reply;
        }
    }

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
