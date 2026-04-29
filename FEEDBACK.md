# FEEDBACK.md — Lessons & Corrections

> Append-only log of corrections, standard changes, and lessons learnt. Read at the start of every session — past mistakes do not get to repeat.
>
> Format: newest at top. Each entry has a date, a one-line summary, and the rationale.

---

## 2026-04-29 — User feedback on architectural framing of the CIPP rebuild

**Source:** Initial planning session, owner-direct feedback while authoring `CLAUDE.md`.

**Three directives that are now foundational** to all future work — these are not commentary, they are architectural baselines:

### 1. CIPP is "just a Graph wrapper" — do not rebuild that wrapper

CIPP-API is, structurally, a 700+-branch `if/else` over Microsoft Graph endpoints written in PowerShell. Roughly 90% of the code is proxying Graph calls. The PowerShell ecosystem lacked typed Graph clients, so the project built its own wrappers for auth, scopes, app registrations, batch, retry, and pagination. **In .NET, those wrappers are unnecessary.** `Microsoft.Graph` SDK v5 + `Microsoft.Identity.Web` natively provide:

- Typed entity models for every Graph resource.
- `PageIterator<T>` for pagination + throttling + `Retry-After`.
- `BatchRequestContent` with per-subresponse status inspection (no more silent inner 429 losses).
- `Delta()` extensions on `users`, `groups`, `directoryObjects`, `devices`, `messages`, etc.
- OBO, refresh, certificate, MSI, and confidential-client OAuth flows.
- Token caching with distributed-cache providers.

**Rule:** if a feature needs more than ~50 lines of Graph plumbing, stop and check whether the SDK already does it. The rebuild's value-add is **caching, multi-tenant orchestration, the Standards engine, the UX, and the security posture** — not Graph proxying.

This is now codified in `CLAUDE.md` §8b ("The 'it's just a Graph wrapper' rule").

### 2. CIPP cannot cache effectively — the rebuild must, and it must be the centerpiece of read performance

CIPP's user-facing performance complaints (`#1064`, `#2883`, `#75`, Discussion `#4979`) all trace to the same root cause: **a JS frontend hydrating from a stateless PowerShell Function App has nowhere to cache.** The choices CIPP has are:

- **(a)** Page hit → paginate Graph until done → block render until everything's loaded. Works at small scale; times out at 100+ tenants.
- **(b)** Load each page on demand without aggressive cache. Fast first paint; slow steady state; every page hit re-pays the Graph cost.

There is no third option **for CIPP's architecture**, because there is no real backend to manage cache state across requests, no shared in-memory store, and Azure Table Storage cannot provide TTL/invalidate/pre-warm semantics.

**A real .NET backend changes the equation entirely.** The rebuild's reads run on this pattern:

1. **L1** in-memory cache (per node, seconds–minutes).
2. **L2** Redis distributed cache (cluster-wide, minutes–hours).
3. **L3** Postgres projection tables (durable, queryable, refreshed on schedule and via Graph delta queries).
4. **Background warmer** kicks Graph — never the request thread. Page renders from L3 with a "data refreshed N minutes ago" footer.

After first-hit warm, every subsequent read is a database/Redis operation, **not** an API operation. Writes go through Graph, then update L3, invalidate L2, push a SignalR refresh to L1.

This is now codified in `CLAUDE.md` §8c ("Caching is a first-class architectural concern") with explicit anti-rules:
- ❌ No request-time pagination of Graph.
- ❌ No "cache for a few minutes and call Graph anyway."
- ❌ No per-request token re-acquisition.
- ❌ No "store the JSON blob and re-parse it on every read."

### 3. The auth complexity in CIPP is an artifact, not a requirement

CIPP's "wackiness" around scopes, app registrations, the SAM (Secure Application Model) wrapper, refresh-token mirroring into env vars, the custom `CIPP.CIPPTokenCache` .NET shim compiled into `CIPPSharp.dll` — none of this is inherent to delegated M365 administration. It exists because PowerShell did not have first-class confidential-client identity primitives.

**In .NET, this collapses to:** `Microsoft.Identity.Web` + `IConfidentialClientApplication` + `ITokenAcquisition` + a thin `IRefreshTokenStore` backed by `IDataProtectionProvider`-protected Postgres rows. Refresh tokens never touch env vars. Token caching is `IDistributedCache` (Redis) with `SemaphoreSlim` coalescing. Rotation is a scheduled Hangfire job with a graceful retry path, not a "rerun the setup wizard" recovery story.

**Rule:** treat the SAM ceremony as legacy mitigation. The new build uses the Microsoft.Identity.Web idioms throughout. There is no per-tenant token spaghetti — there is a typed `IGraphTenantClient` factory keyed on `(MspId, CustomerTenantId)` and the SDK handles the rest.

---

**Why this matters going forward:** every phase plan, every architectural choice, and every code review must hold the rebuild to these three standards. If a proposed design starts looking like it needs hand-rolled Graph plumbing, hand-rolled caching, or hand-rolled OAuth, **stop and re-architect** before code is written. These aren't optimisations — they are the reason a rebuild is worth doing at all.
