# Server notifications (T4)

Agent quota events pass through `NotificationQuotaEventSink` and `NotificationDispatcher` to registered channels. Both Agent and Web call `AddNetPilotWebPush`; Web can resolve `NotificationService.SendTestAsync(label?, ct)` for the later subscribe UI. Test sends go directly to every subscription, bypassing quiet hours and dedupe, and return a result per subscription. T4 adds no HTTP endpoints or browser subscription code.

Configuration is bound from `NetPilot:Notifications` in both hosts:

```json
{
  "NetPilot": {
    "Notifications": {
      "VapidSubject": "mailto:admin@example.com",
      "DedupeWindowHours": 24,
      "QuietHours": { "Enabled": false, "Start": "23:00", "End": "07:00" }
    }
  }
}
```

Set `VapidSubject` to a real contact email (`mailto:`) or contact page (`https:`). The fallback `mailto:netpilot@localhost` is only a placeholder. Invalid subjects, times outside the `HH:mm` format, and non-finite or out-of-range dedupe windows fail registration. Dedupe accepts 0–87600 hours; zero disables suppression.

Quiet hours use `UsagePeriodClock.TimeZone` (`NetPilot:TimeZone`, default `Africa/Cairo`). Start is inclusive and end exclusive; windows can wrap midnight, and equal endpoints disable the window. Only `Warning` and `DryRun` quota event kinds are quiet-hours eligible. `Exceeded`, `ActionApplied`, `Reverted`, and `ActionSkippedProtected` always bypass quiet hours, including an `Exceeded` event whose policy is dry-run. Suppressed notifications are logged and dropped, not queued.

Tags are `quota:{canonicalMac}:{periodKind}:{periodKey}:{kind}`, with `:{warnPercent}` appended for warnings. Kind and period names use the existing enum names, for example `quota:AA-BB-CC-DD-EE-01:Day:2026-10-15:Warning:80`. Byte values use decimal MB/GB. Messages open `/` and have no actions yet; `NotificationActionIds` reserves `extend-1gb` and `restore-now` for T5.

Dedupe is per channel and process, measured from a successful send, with a default 24-hour window. It expires at the exact end of that window and resets on process restart. Failed and quiet-hours-suppressed attempts do not claim the tag. Channels run concurrently and isolate transport failures; caller cancellation propagates. `notification_log` records each channel attempt and is pruned after dispatch to the newest 500 rows. Errors contain safe exception categories rather than transport exception messages.

`LitePushSubscriptionStore` stores subscriptions in `push_subscriptions`. IDs are lowercase SHA-256 hex hashes of the exact endpoint string, recomputed on upsert. Re-subscribing updates keys and label while preserving creation time and delivery history. Successful sends set `LastSuccessAtUtc` and reset the consecutive failure count; failures increment it. A deleted subscription is never recreated by recording a late delivery result.

`VapidKeyStore.GetOrCreateAsync` returns the browser-ready base64url uncompressed public P-256 point and the server-only private scalar. It uses standard .NET ECDsa key generation; Lib.Net.Http.WebPush 3.3.1 owns payload encryption and VAPID signing. The single `vapid_keys` document has fixed ID `default`. First use inserts, never overwrites: a duplicate-key loser re-reads the winning document. The private scalar is protected with the shared Data Protection key ring, application name `NetPilot`, and its own purpose string. Preserve both the shared database and key ring when backing up or moving the deployment. Expose only the public key to browsers; never serialize the server key pair.

The sender sends encrypted `aes128gcm` payloads with `vapid` authorization and a TTL of 86400 seconds. JSON fields are `title`, `body`, `url`, `tag`, and `actions` (each action has `action` and `title`). HTTP 404/410 removes the subscription; other failures keep it and record a failed result, then continue to the remaining subscriptions. The library’s automatic 429 retry is disabled so a long Retry-After cannot stall later deliveries. Any failed subscription makes the channel attempt `Failed`; a later retry can also resend to subscriptions that previously succeeded. Test sends return these per-subscription results without throwing for push-service failures. Storage/configuration errors can still propagate.

Sender tests inject a fake `HttpMessageHandler` into the library's real `PushServiceClient(HttpClient)`, exercising its encryption, headers, and HTTP status handling without a network call. Phone delivery and Docker builds require separate environment verification.
