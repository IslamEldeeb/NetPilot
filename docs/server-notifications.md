# Server notifications

Agent quota events pass through `NotificationQuotaEventSink` and `NotificationDispatcher` to registered channels. Both Agent and Web call `AddNetPilotWebPush`; Web can resolve `NotificationService.SendTestAsync(label?, ct)` for the Notifications tab. Test sends go directly to every subscription, bypassing quiet hours and dedupe, and return a result per subscription. T5 adds the browser client and token-authorized quota actions described below.

Configuration is bound from `NetPilot:Notifications` in both hosts:

```json
{
  "NetPilot": {
    "Notifications": {
      "VapidSubject": "mailto:admin@example.com",
      "DedupeWindowHours": 24,
      "ActionTokenHours": 24,
      "QuietHours": { "Enabled": false, "Start": "23:00", "End": "07:00" }
    }
  }
}
```

In Docker Compose, set `NETPILOT_VAPID_SUBJECT` in `deploy/docker/.env`; it maps to `NetPilot__Notifications__VapidSubject` in both Agent and Web. `NETPILOT_TIMEZONE` maps to `NetPilot__TimeZone` in both, defaulting to `Africa/Cairo`. Recreate with `up -d --build` using your selected Compose file set after changes; see [deployment](deployment.md).

Set `VapidSubject` to a real contact email (`mailto:`) or contact page (`https:`). The fallback `mailto:netpilot@localhost` is only a placeholder. Invalid subjects, times outside the `HH:mm` format, and non-finite or out-of-range dedupe windows fail registration. Dedupe accepts 0–87600 hours; zero disables suppression.

Quiet hours use `UsagePeriodClock.TimeZone` (`NetPilot:TimeZone`, default `Africa/Cairo`). Start is inclusive and end exclusive; windows can wrap midnight, and equal endpoints disable the window. Only `Warning` and `DryRun` quota event kinds are quiet-hours eligible. `Exceeded`, `ActionApplied`, `Reverted`, and `ActionSkippedProtected` always bypass quiet hours, including an `Exceeded` event whose policy is dry-run. Suppressed notifications are logged and dropped, not queued.

Tags are `quota:{canonicalMac}:{periodKind}:{periodKey}:{kind}`, with `:{warnPercent}` appended for warnings. Kind and period names use the existing enum names, for example `quota:AA-BB-CC-DD-EE-01:Day:2026-10-15:Warning:80`. Byte values use decimal MB/GB. Messages open `/`. Live `Exceeded` and `ActionApplied` messages carry fresh `extend-1gb` and `restore-now` action tokens. Events with the dry-run flag, warnings, reversions and protected-device skips have no actions.

Dedupe is per channel and process, measured from a successful send, with a default 24-hour window. It expires at the exact end of that window and resets on process restart. Failed and quiet-hours-suppressed attempts do not claim the tag. Channels run concurrently and isolate transport failures; caller cancellation propagates. `notification_log` records each channel attempt and is pruned after dispatch to the newest 500 rows. Errors contain safe exception categories rather than transport exception messages.

`LitePushSubscriptionStore` stores subscriptions in `push_subscriptions`. IDs are lowercase SHA-256 hex hashes of the exact endpoint string, recomputed on upsert. Re-subscribing updates keys and label while preserving creation time and delivery history. Successful sends set `LastSuccessAtUtc` and reset the consecutive failure count; failures increment it. A deleted subscription is never recreated by recording a late delivery result.

`VapidKeyStore.GetOrCreateAsync` returns the browser-ready base64url uncompressed public P-256 point and the server-only private scalar. It uses standard .NET ECDsa key generation; Lib.Net.Http.WebPush 3.3.1 owns payload encryption and VAPID signing. The single `vapid_keys` document has fixed ID `default`. First use inserts, never overwrites: a duplicate-key loser re-reads the winning document. The private scalar is protected with the shared Data Protection key ring, application name `NetPilot`, and its own purpose string. Preserve both the shared database and key ring when backing up or moving the deployment. Expose only the public key to browsers; never serialize the server key pair.

The sender sends encrypted `aes128gcm` payloads with `vapid` authorization and a TTL of 86400 seconds. JSON fields are `title`, `body`, `url`, `tag`, and `actions` (each action has `action`, `title`, and `token`). HTTP 404/410 removes the subscription; other failures keep it and record a failed result, then continue to the remaining subscriptions. The library’s automatic 429 retry is disabled so a long Retry-After cannot stall later deliveries. Any failed subscription makes the channel attempt `Failed`; a later retry can also resend to subscriptions that previously succeeded. Test sends return these per-subscription results without throwing for push-service failures. Storage/configuration errors can still propagate.

Sender tests inject a fake `HttpMessageHandler` into the library's real `PushServiceClient(HttpClient)`, exercising its encryption, headers, and HTTP status handling without a network call. Phone delivery and Docker builds require separate environment verification.


## Client side (T5)

On Android Chrome, open NetPilot over trusted HTTPS → **Notifications** → optionally name the device → **Enable notifications on this device**, then allow the prompt. PWA installation is optional. If permission is denied, allow Notifications in Chrome site settings and refresh. **Send test notification** sends to all registrations and shows per-device results; push-service acceptance does not prove that the OS displayed it. **Disable on this device** unsubscribes Chrome and removes its registration. **Remove** deletes only the server registration; Enable can register the browser again. Subscription rotation requires reopening the tab and enabling again; no automatic renewal is attempted.

For desktop Chrome, run `dotnet run --project src/NetPilot.Web --launch-profile http` and open `http://localhost:5036`. Chrome treats localhost as secure. Plain HTTP at a LAN IP is insecure: the status card explains this and disables Enable. For private Tailscale HTTPS or the LAN-only Caddy alternative, follow [HTTPS access](https-access.md). The dashboard has no authentication: keep it on your private LAN/tailnet and never expose it publicly. Web leaves HTTP available; deployment HTTPS is handled separately. The root `/sw.js` uses scope `/`, no fingerprint endpoints, and `Cache-Control: no-cache, no-store, max-age=0`; it does not cache pages. The manifest's 192 and 512 PNG icons are enlarged copies of the existing favicon.

Action buttons POST JSON `{ "token": "…", "action": "extend-1gb" }` (or `restore-now`) to `/api/notifications/action` and show the returned message. The action ID is optional but must match when supplied. Results are `{status,message}` with `Ok`, `AlreadyUsed`, `Invalid`, `Expired`, or `NotFound`; malformed JSON returns HTTP 400, overload HTTP 429 (`RateLimited`), and service failure HTTP 503 (`Unavailable`). Only this route disables antiforgery. Its per-peer-IP fixed window allows 6 requests/minute, no queue, with a 60-second retry hint. Behind a proxy, clients share a bucket until trusted forwarding is configured.

The token is the credential; there is no login. Data Protection binds MAC, period kind/key and action using the shared key ring. A time-limited protector uses `NetPilot:Notifications:ActionTokenHours` (default 24; greater than zero, at most 168). An authenticated expiry envelope distinguishes expiry from tampering. Canonical base64url prevents alternative spellings from bypassing replay hashes. SHA-256 used-token hashes live in the named `quota_period_state` row; consumption and mutation share one LiteDB transaction. Agent saves preserve Web-owned fields. Tokens and subscription keys are never logged. Anyone holding an unused token can perform its scoped action until expiry or period end; keep notification contents private. Inactive state and hashes are pruned after 62 days.

**+1 GB today** adds exactly 1,000,000,000 bytes to the named daily row; monthly notifications say **+1 GB this month** and extend only that monthly row. **Restore now** sets `ReleaseRequested`; the Agent releases that claim through the existing revert/event path and sets `ActionSuppressedForPeriod`, preventing re-enforcement for that period. A request before enforcement suppresses the period without a router write or `Reverted` event. Another active daily/monthly claim can keep the shared router action in place. New periods enforce again; an extended cap can be exceeded again. Both operations change persisted state only: **the Agent applies effects on its next tick (default 180 seconds)**. The Web action route never calls the router.

The Agent image now uses the ASP.NET runtime for the Data implementation's time-limited Data Protection API; no new NuGet package was added.
