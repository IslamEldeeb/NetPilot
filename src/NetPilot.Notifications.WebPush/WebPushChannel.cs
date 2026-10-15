using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using NetPilot.Core.Notifications;

namespace NetPilot.Notifications.WebPush;

public class WebPushChannel : INotificationChannel
{
    private readonly PushServiceClient _client;
    private readonly IPushSubscriptionStore _subscriptions;
    private readonly IVapidKeyStore _keys;
    private readonly NotificationOptions _options;

    public WebPushChannel(PushServiceClient client, IPushSubscriptionStore subscriptions,
        IVapidKeyStore keys, NotificationOptions options)
    {
        // The library otherwise retries 429 responses indefinitely, delaying all later subscriptions.
        client.AutoRetryAfter = false;
        _client = client;
        _subscriptions = subscriptions;
        _keys = keys;
        _options = options;
    }

    public string Id => "webpush";

    public async Task SendAsync(NotificationMessage message, CancellationToken ct)
    {
        var results = await SendToSubscriptionsAsync(message, ct);
        if (results.Any(r => !r.Success))
            throw new WebPushDeliveryException(results.Count(r => !r.Success));
    }

    public async Task<IReadOnlyList<PushDeliveryResult>> SendToSubscriptionsAsync(NotificationMessage message, CancellationToken ct)
    {
        var keyPair = await _keys.GetOrCreateAsync(ct);
        using var authentication = new VapidAuthentication(keyPair.PublicKey, keyPair.PrivateKey) { Subject = _options.VapidSubject };
        var payload = JsonSerializer.Serialize(new
        {
            title = message.Title, body = message.Body, url = message.Url, tag = message.Tag,
            actions = message.Actions.Select(a => new { action = a.Id, title = a.Title })
        });
        var results = new List<PushDeliveryResult>();
        foreach (var subscription in await _subscriptions.GetAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await SendOneAsync(subscription, payload, authentication, ct));
        }
        return results;
    }

    private async Task<PushDeliveryResult> SendOneAsync(PushSubscriptionInfo subscription, string payload,
        VapidAuthentication authentication, CancellationToken ct)
    {
        try
        {
            var target = new PushSubscription { Endpoint = subscription.Endpoint };
            target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
            target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
            await _client.RequestPushMessageDeliveryAsync(target, new PushMessage(payload) { TimeToLive = 86400 },
                authentication, VapidAuthenticationScheme.Vapid, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            await _subscriptions.RemoveAsync(subscription.Id, ct);
            return new(subscription.Id, subscription.Label, false, $"HTTP {(int)ex.StatusCode}: subscription removed");
        }
        catch (Exception ex)
        {
            await _subscriptions.RecordResultAsync(subscription.Id, false, ct);
            // Transport exceptions can include the endpoint and client keys; return only a safe category.
            var error = ex is PushServiceClientException response ? $"HTTP {(int)response.StatusCode}" : ex.GetType().Name;
            return new(subscription.Id, subscription.Label, false, error);
        }
        await _subscriptions.RecordResultAsync(subscription.Id, true, ct);
        return new(subscription.Id, subscription.Label, true);
    }
}

public record PushDeliveryResult(string SubscriptionId, string? Label, bool Success, string? Error = null);

public class WebPushDeliveryException(int failureCount) : Exception($"Web Push failed for {failureCount} subscription(s).");
