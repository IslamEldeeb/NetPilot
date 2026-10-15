# Rules

The **Rules** tab creates, edits, enables, dry-runs and deletes rules. Templates only prefill an editor; click **Save rule** to persist. Every new rule starts enabled and in dry-run. The Web process writes rule data and release requests only. The Agent evaluates rules after usage tracking and quotas on its next tick (default 180 seconds, about 3 minutes), then applies or releases effects. Refresh the tab to read current firings and claims. No browser or real-router verification was performed for T7c; the checks below remain deployment work.

## Trigger, scope, actions and revert

Choose all devices, one category, or selected devices. Usage thresholds use **Internet** bytes after configured LAN exclusions, in decimal GB: **1 GB = 1,000,000,000 bytes**. Daily/monthly boundaries and time windows use `NetPilot:TimeZone`, shown in the editor and history (default `Africa/Cairo`). Compare usage with the router and complete [LAN counter verification](lan-traffic-exclusion.md) before enabling enforcement.

| Trigger | Meaning |
|---|---|
| Device usage over | Each matching device reaches the chosen daily/monthly byte threshold |
| Household usage over | The whole household reaches the threshold; scope selects the devices targeted by actions, not the usage being summed |
| Time window | Start inclusive, end exclusive, in the app timezone; overnight windows belong to the selected weekday on which they start |
| New device joined | Device first discovered at or after rule creation, once per rule/device; existing devices do not count as new |
| Device idle | Device has not been seen for the chosen number of days; one firing per idle episode until it is seen again |

Add one or more actions: **Notify**, **Throttle** (same Kbps cap for upload/download), **Block**, or **Set guest/IoT WiFi** on/off. Notification title/body support `{rule}`, `{device}`, `{used}` and `{cap}`; the last two are raw byte counts. Notification subscriptions, quiet hours and delivery history live in [Notifications](server-notifications.md). Dry-run firings can still send notifications.

Revert policies are manual release, period end, after a delay, or **when the time window ends** (only available for a Time window trigger). Period end uses the usage trigger's period; other triggers use midnight. Notify-only results have nothing to restore. Cooldown limits repeat firings when **Once per** is None; choosing Day/Month suppresses repeats within that period instead. New-device and idle/window episode suppression also applies. A still-active firing prevents another firing for that rule/subject.

Invalid forms show validation errors and disable Save. For example, an empty device scope needs at least one device, usage needs positive decimal GB (up to nine decimal places, within a signed 64-bit byte count), and a time window needs different valid HH:mm endpoints and at least one weekday. A template can intentionally start with missing inputs.

## Dry-run, forced dry-run and the kill switch

A rule's **Dry-run** flag logs/notifies what it would do without creating router effects. Separately, `NetPilot:Rules:ForceDryRun=true` forces every rule to dry-run regardless of its own flag. The Docker deployment defaults **NETPILOT_RULES_FORCE_DRYRUN=true**. Both Agent and Web receive `NetPilot__Rules__ForceDryRun` and bind `RulesOptions` from the same `NetPilot:Rules` section. The prominent dashboard banner reports that Web configuration; keep both processes configured identically when running outside Compose.

`NetPilot:Rules:Enabled=false` is the global kill switch: the Agent stops new evaluations and releases existing rule effects. Compose maps **NETPILOT_RULES_ENABLED** to both processes (default true). Existing live effects are also released when forced dry-run becomes active or their rule becomes disabled, deleted or dry-run. These changes happen on the Agent's next tick, with retries and per-tick router-write limits; the displayed interval is approximate, not a completion deadline. Compose shares **NETPILOT_POLL_INTERVAL_SECONDS** (default 180) with both processes as `NetPilot:PollIntervalSeconds` for the dashboard timing note.

To enable enforcement safely:

1. Keep forced dry-run on and watch firing history for a few days. Use the **Dry-run results** filter; those rows say **Would have** and have a distinct background.
2. Confirm device and household Internet usage against the router, accounting for LAN traffic and period boundaries. Complete the T8 experiment before trusting exclusions.
3. Protect your management devices in **Quotas → Protected devices**. Review scopes, actions, revert policies and timezone.
4. Leave individual rules in dry-run, set `NETPILOT_RULES_FORCE_DRYRUN=false` in `deploy/docker/.env`, then recreate both services with `docker compose --env-file .env up -d --build` from `deploy/docker/`. Include your HTTPS override files when applicable; see [deployment](deployment.md).
5. Flip individual rules out of dry-run one at a time. Wait for the next Agent tick, compare router state with history, and test release/restoration before enabling the next rule.

## Why is this device restricted?

Under **Activity from rules**, select a device to see persisted action claims from `IActionClaimStore` and its active rule firings. Rule sources are `rule:{id}`; quota sources begin `quota:`. The lookup identifies each owner, action and scheduled release condition, including claims whose rule was deleted while awaiting the Agent. A firing's stored actions describe the intent when it fired, so later edits do not rewrite history.

**Block wins over every throttle. Otherwise the lowest Kbps cap wins**, symmetrically, across rule and quota claims. Releasing one owner does not release another; daily and monthly quota claims can both be active. When all automatic claims end, the normal device/category policy is restored. The lookup is persisted intent rather than a fresh router read: failed or interrupted firings can be pending or partly applied, and normal base speed limits can still remain without any automatic claims.

**Protected devices are never throttled or blocked by rules or quotas.** Notify and guest/IoT WiFi actions still apply. Protecting a device does not exempt an entire wireless network from changes.

History shows newest first, filterable by rule, device and dry-run/live results: local time, saved rule name, subject (device/MAC, household, plus affected wireless ids), outcome, status, actions, scheduled revert condition and error/detail. Outcomes include Applied, DryRun, SkippedProtected and Failed. Active means recovery or effects are still pending; it does not guarantee that every action succeeded.

Choose **Release now**, then confirm to atomically set `ReleaseRequested` only on that active firing. The Agent releases its effects on the next tick (about 3 minutes by default), possibly over more ticks if retries or the action budget require it. Repeated requests are safe. The Web never calls the router. History shows “Release requested” until the Agent finishes. This is also the approval step for **New device guard**. Usage rules may fire again after cooldown/once-per suppression expires; release is not a permanent exemption. Quota claims have their own release controls via notification actions.

## WiFi lockout guard

Only model-valid `guest-2.4g`, `guest-5g`, `guest-6g`, `iot-2.4g`, `iot-5g` and `iot-6g` are selectable. **Main networks are forbidden** by both the editor choices and model validation. The list describes model ids rather than router capabilities; choose a network your router actually provides or the firing will report failure. Wireless restoration compares the current enabled state with the last state written by the rule; a differing manual state is left alone. Unchanged enabled state cannot prove that no manual edit occurred.

## Starter templates

| Template | Prefilled behavior | Required choice / safety note |
|---|---|---|
| Bedtime throttle | 22:00–07:00 every day, 256 Kbps, revert when window ends | Choose devices or change to category scope; protect the management device |
| Binge alert | Device daily usage reaches 10 GB, Notify, Once per Day | Choose a category; Television or Game Console are examples; no restriction |
| New device guard | All newly discovered devices, Notify + 256 Kbps, manual release | No extra input required; Release now approves the device; protected devices skipped |
| Household monthly ISP cap warning | Whole-household monthly usage warning, Notify, Once per Month | Enter about 80% of the real ISP cap (e.g. 200 GB for a 250 GB plan); no restriction |
| Idle device cleanup reminder | Device idle 14 days, Notify, Once per None, seven-day cooldown | No extra input required; one reminder per idle episode; never deletes a device |
| Guest WiFi off at night | 01:00–06:00 every day, guest network off, restore when window ends | Pick the guest network id; main WiFi cannot be selected; preserves a differing manual enabled state |

Templates are static definitions in `NetPilot.Core/Rules/Presentation/TemplateCatalog.cs`; summaries, form mapping and GB conversion live alongside them and are covered by Core tests without a Web test package.
