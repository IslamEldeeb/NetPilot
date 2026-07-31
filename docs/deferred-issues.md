# NetPilot — Deferred Issues (needs live verification)

Parking lot for open items that require checking something against the real router or a real
deployment — not solvable from a sandbox session, and not urgent enough to block on. The user
picks these up when convenient (a live Cowork/Chrome session against the router, or a post-deploy
data check) and reports back; whoever resolves one should fold the finding into the relevant
phase doc and remove the entry here.

Each entry names where it came from and exactly what would resolve it, so picking one up doesn't
require re-deriving context.

---

## 1. `trafficUsage` unit — bytes, or something else?

**From:** `docs/phase2-usage-tracking-plan.md` §2 item 1, `docs/phase2-implementation-plan.md` intro,
`docs/phase2-usage-tracking-feature.md` §6.

**What's assumed today:** `TpLinkUsageParser.TryParseBytes` treats the raw `trafficUsage` value as
a plain integer count of bytes, with a fallback for human-formatted strings (`"1.2 GB"`). This is a
documented guess, isolated to that one file specifically so it's a one-file fix if wrong.

**Impact if wrong:** every usage number in the dashboard is off by a fixed multiplier (e.g. showing
raw KB as if it were bytes). Usage tracking itself is fully built and works structurally either way
— only the scale of the numbers is in question.

**What resolves it:** either (a) a raw network capture of the `loadDevice` JSON response for a
device with known real-world usage, or (b) deploying and eyeballing day-one numbers against another
source (the router's own Tether app, or an ISP portal). Both require the real router/network.

**Also covers:** the related "first-observation-counts-in-full" assumption in the same file/doc —
verifying real numbers post-deploy answers both at once.
