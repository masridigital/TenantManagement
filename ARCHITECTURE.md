# ARCHITECTURE.md — System Mental Model

> The "how the pieces fit together" document. Read this after `README.md` (the what) and before `PHASES.md` (the plan). Pairs with `CLAUDE.md` for the coding-standard side of the same architecture.
>
> Status: **draft** — sections will land incrementally. Each section is a self-contained chunk; review and push happen per section.

---

## 0. The thesis (read this once and remember it)

The CIPP rebuild's value is **not** in proxying Microsoft Graph. Microsoft already ships a typed Graph SDK and a typed identity library; everything CIPP-API hand-rolls in PowerShell — pagination, throttling, batch, OAuth refresh, token caching, scope wrangling — is solved primitives in `Microsoft.Graph` v5 and `Microsoft.Identity.Web`. The rebuild's value lives in the **four things CIPP cannot do well today** because of its runtime:

1. **A real cache.** A stateless PowerShell Function App has nowhere to cache. The user-visible performance complaints (`#1064`, `#2883`, `#75`, Discussion `#4979`) all collapse to the same root cause. We solve it with a three-tier cache (L1 in-memory → L2 Redis → L3 Postgres projection) where the request thread never paginates Graph; a background warmer does. Reads serve from the projection with a "data refreshed N minutes ago" footer. Writes go through Graph, then synchronously update the projection and invalidate L2.
2. **A real backend.** Typed handlers, typed DTOs, typed validators, one controller registry, OpenAPI document, structured logs, distributed tracing, and a single deployable. No 533 `Invoke-*.ps1` files; no per-file error handling; no 64 KB row limit on templates because Postgres is the store, not Azure Tables.
3. **A real auth posture.** `Microsoft.Identity.Web` + `IConfidentialClientApplication` + `ITokenAcquisition` replaces the SAM ceremony, the `CIPPSharp.dll` token-cache shim, and the refresh-token-into-env-var pattern. Refresh tokens live encrypted in Postgres via `IDataProtectionProvider`. Access tokens live in Redis with `SemaphoreSlim` coalescing. Rotation is a Hangfire job, not a setup-wizard rerun.
4. **A real distribution model.** Single managed multi-MSP SaaS as the primary product, with a self-host container image as a secondary option. The fork-and-deploy-per-MSP model — the documented source of every "upstream broke my fork" support ticket — is gone.

Everything below is in service of those four. If a proposed component does not advance one of them, it does not belong in the rebuild.

---

## 1. System context

The product is a multi-tenant web application operated by **us** (the SaaS provider) and used by **MSPs** to administer their **customer M365 tenants** via delegated admin (GDAP). Three actor classes, three data classes, four upstream dependencies.

### Actors

| Actor | Identity | Purpose | Auth |
| ----- | -------- | ------- | ---- |
| **MSP user** | An employee of an MSP (Editor / Admin / SuperAdmin / custom roles) | Day-to-day administration of customer tenants | Entra ID OIDC at the MSP's home tenant; carries `MspId` and role claims |
| **MSP automation principal** | Per-MSP service principal in our tenant or theirs | Scheduler jobs, webhook receivers, PSA integrations | Confidential client + cert; never password |
| **Platform operator** | Us (the provider's SREs) | Tenancy onboarding, billing, incident response | SuperAdmin role on the platform tenant; access scoped, audited, MFA-required |

End users of the customer M365 tenant are **not** actors. They are subjects of records the MSP manages. There is no end-user-facing surface in this product.

### Data classes

| Class | Examples | Where it lives | Sensitivity |
| ----- | -------- | -------------- | ----------- |
| **Platform data** | MSP records, users-of-MSP, roles, billing, audit log | Postgres (platform schema) | High — cross-tenant blast radius if leaked |
| **Customer projection data** | Cached lists of customer-tenant users / groups / devices / policies, Standards reports, drift baselines, delta cursors | Postgres (per-MSP schema or row-filtered tables) + Redis | Medium — single-MSP blast radius; refreshable from Graph |
| **Operational secrets** | Customer-tenant refresh tokens, partner integration keys, webhook signing secrets | Postgres (encrypted via DataProtection) + Key Vault for the master key | **Critical** — leak compromises every customer tenant of every MSP using the platform |

Operational secrets **never** appear in env vars, log lines, structured-log properties, error messages, or audit-trail bodies. This is enforced by `IPiiRedactor` middleware and lint rules on `ILogger` calls.

### Upstream dependencies

| Dependency | What we use it for | Failure mode |
| ---------- | ------------------ | ------------ |
| **Microsoft Graph v1.0 + beta** | Every customer-tenant operation: identity, Intune, Exchange, Teams, SharePoint, Defender, audit log | Throttled per-tenant by Microsoft; we honor `Retry-After`, fan out under our own bulkhead, and shed load to L3 cache during incidents |
| **Microsoft Partner Center** | GDAP relationship enumeration, delegated invite creation, customer onboarding | Lower throughput than Graph; cached aggressively; outage degrades onboarding only |
| **Entra ID (multi-tenant)** | OIDC sign-in for MSP users, OBO token exchange for Graph, PIM activation | Outage means no new sign-ins; cached sessions continue; background jobs degrade gracefully |
| **PSA / RMM partners** (Halo, NinjaOne, Pax8, Hudu, IT Glue, Datto) | Outbound webhook delivery, ticket sync, license sync | Per-integration bulkhead; failures retry-with-backoff and surface in the integrations health page |

### Inbound interfaces

| Interface | Consumer | Protocol |
| --------- | -------- | -------- |
| `app.{domain}` — Blazor Web App | MSP user browser | HTTPS, server-rendered, SignalR over WebSockets |
| `api.{domain}` — REST API | Internal (the Blazor client), third-party integrators (rate-limited public surface for select endpoints) | HTTPS, JSON, OpenAPI 3.1 documented |
| `hooks.{domain}/v1/{provider}` | PSA / monitoring webhooks | HTTPS, per-provider HMAC signature verification |
| `/hubs/{domain}` — SignalR hubs | Blazor client | WebSockets, MSP-scoped groups |

### Outbound interfaces

| Interface | Direction | Auth |
| --------- | --------- | ---- |
| Microsoft Graph | Per customer tenant via OBO or app-only with GDAP scope | Refresh token (delegated) or cert + tenant claim (app-only) |
| Partner Center | Platform-wide, on behalf of the MSP's CSP relationship | Refresh token (delegated GDAP) |
| PSA / RMM webhooks | Per integration | Provider-specific (API key, OAuth, signed JWT) |
| Email (alerts) | Outbound to MSP users | SMTP relay or Graph `sendMail` from a platform mailbox |

### Out of scope — explicitly

- We do **not** terminate end-user sign-ins for customer tenants. This is not a CIAM product.
- We do **not** persist customer-tenant content (mail bodies, document text, chat messages). Metadata only; bodies are streamed when needed and not stored.
- We do **not** build a forked PowerShell runtime. The rebuild does not run any CIPP `.ps1` file in production. Migration tools may parse CIPP exports, but no execution.

---

## 2. Bounded contexts

The feature surface decomposes into **13 bounded contexts**, each with its own folder under `Application/` and `Domain/`, its own MediatR handlers, its own DTOs, its own background jobs, and (where the resource is large) its own L3 projection table set. A context owns its data; cross-context reads happen via **public DTOs**, never via reaching into another context's entities or its `DbContext`.

The map below is the canonical list. Filenames inside each context follow the same pattern: `<Context>/<Aggregate>/<Action>/<ActionCommandOrQuery>.cs`.

| # | Context | Owns | Primary upstream | L3 projection? | Notes |
| - | ------- | ---- | ---------------- | -------------- | ----- |
| 1 | **Identity** | Users, groups, devices, app registrations, GDAP/JIT, risky users, sign-in logs, breach search | Graph: `users`, `groups`, `devices`, `applications`, `directoryRoles`, `riskyUsers`, `signIns`, `auditLogs/directoryAudits`; HIBP for breach search | Yes (users, groups, devices) — delta-driven | Hosts the MSP-side identity (us → MSP) **and** the customer-side identity (MSP → customer tenant); these are different aggregates that happen to share Graph plumbing |
| 2 | **EndpointManagement** (Intune) | Devices (managed), policies, autopilot, applications, assignment filters, compliance, BitLocker / LAPS recovery, scripts | Graph `deviceManagement/*`, `deviceAppManagement/*` | Yes (managed devices, policies, applications) | Recovery-key reads are a sensitivity hotspot — separate audit channel and just-in-time access |
| 3 | **ExchangeOnline** | Mailboxes, mailbox permissions, mailbox rules, transport rules, connectors, anti-spam / phishing / malware filters, safe links / attachments, quarantine, message trace, mailbox restore | Graph `users/{id}/mailboxSettings`, `users/{id}/messages`; **Exchange Online PowerShell** for the bits Graph still doesn't cover | Partial — mailbox metadata only; message bodies are not stored | The PowerShell-only fallback is isolated in `TenantManagement.Graph.ExchangeShim` so the rest of the app stays Graph-typed |
| 4 | **Collaboration** (Teams / SharePoint / OneDrive) | Sites, voice, activity, OneDrive provisioning, sharing settings | Graph `sites`, `teams`, `chats`; Teams admin endpoints | Yes (sites list per tenant) | Voice / LIS is a discrete sub-aggregate; can be feature-flagged off for MSPs without voice |
| 5 | **Security** | Alerts, incidents, Defender state / TVM, secure score, BEC check & remediate, tenant allow/block lists, audit log search, named locations | Graph `security/*`, `auditLogs/auditLogQueries`, M365 Defender API | Yes (alerts, incidents, secure score history) | Audit-log search is a long-running query; modelled as a saga, not a request handler |
| 6 | **Tenants** | Onboarding (GDAP invites, role mapping, SAM bootstrap), offboarding, alignment / drift, all-tenant BPA / compliance / domain health | Partner Center, Graph delegated relationships | Yes (the canonical customer-tenant table) | The platform's "spinal cord" — every other context reads from this for `(MspId, CustomerTenantId)` lookups |
| 7 | **Standards** | The Standards engine: typed `IStandardHandler` registry, per-tenant settings, Report / Remediate / Alert modes, drift detection | All Graph endpoints (per-handler) | Yes (Standards reports, drift baselines blob refs) | One handler per standard — see `CLAUDE.md` §8 |
| 8 | **Templates** | CA, Intune, transport rule, group, app, BPA, standards, spam / connection / safe-links, JIT | None — internal store | n/a (this **is** the canonical store) | Postgres `jsonb` column, no 64 KB limit; versioned with row-versioning |
| 9 | **Reports** | Domain analyser, license usage, inactive accounts, MFA report, app consents, OAuth apps | Graph + DNS lookups | Yes (license usage history, inactive accounts) | Reports are a read-side projection; they never write to Graph |
| 10 | **Automation** | Recurring jobs, webhooks, alert configurations, PSA integrations | Hangfire / Service Bus internally; PSA APIs externally | Partial (job history) | Hosts the scheduler. Per-integration adapters live in `TenantManagement.Integrations.<Provider>` |
| 11 | **Bpa** (Best Practice Analyzer) | Legacy BPA support; one-way migration off to Standards | Same Graph endpoints as Standards | Read-through to Standards' projection | Exists for migration only — see §10 of `PHASES.md` |
| 12 | **PlatformAdmin** | Backend health, MSP users & roles, partner webhooks, branding, integrations, app permissions, GDAP role mapping, custom data | None upstream; this is purely platform-side | n/a | The "settings" surface — administered by SuperAdmin |
| 13 | **Observability** | Activity feed, audit trail, scheduled-item history, distributed-tracing surface | Internal + OTel exporter | Yes (audit log, activity feed) | This is a context, not just plumbing — it has a UI surface and a query model |

### Cross-context rules

- **No `using TenantManagement.Application.<OtherContext>.Entities`** — period. Cross-context reads go through DTOs published by the source context's `Application` project.
- **Domain events** are how contexts react to each other. Example: `Tenants.OnboardingCompleted` → `Identity` warms the user / group projection; `Standards` schedules first ReportAsync; `Automation` enables default schedules.
- **No shared DbContext.** Each context has its own `IModuleDbContext` partial, composed at startup into a single `AppDbContext`. Migrations are owned per-context.
- **Cross-context queries that span 3+ contexts** (e.g., "show me every customer tenant where the secure-score handler last reported red AND there's a high-severity incident open AND BitLocker-recovery-key reads happened in the last 24h") go through a dedicated **read model** in the `Reports` context, not through ad-hoc joins.

### Why 13, not 380

CIPP-API exposes **~300 HTTP endpoints + ~80 background functions** with no controller registry. We collapse that to ~13 controller groups (one per context) hosting endpoint groups. The endpoint count drops by an order of magnitude not because we lose features but because:

- One handler replaces many one-off `Invoke-*.ps1` files (e.g., one `UsersController` group, not 22 user-related `Invoke-*.ps1` files).
- "Get a list" + "get one" + "delta since cursor" share one query handler with a parameter, not three files.
- Sub-resources are URL nesting, not new top-level endpoints.

---

## 3. The cache hierarchy and read path

This is the architectural centrepiece. Read it twice.

### Why this exists

CIPP's read path is forced into a binary choice:

- **(a) Block-and-paginate** — page hit → request thread paginates Graph until done → render. Works at small scale; times out at 100+ tenants. Source of `#1064`, `#2883`.
- **(b) Lazy paginate** — load each page on demand, no aggressive cache. Fast first paint, slow steady state, every page hit re-pays the Graph cost. Source of Discussion `#4979`.

There is no third option for CIPP's architecture because there is no real backend. A JS frontend hydrating from a stateless PowerShell Function App has nowhere to share cache state across requests. Azure Tables / Blobs cannot provide TTL, invalidate, or pre-warm semantics that a real cache layer needs.

The rebuild has a real backend, so the read path runs on a different model entirely: **the request thread never paginates Graph**. A background warmer does, on a schedule, using delta queries. Pages render from a local projection.

### The four layers

```
┌────────────────────────────────────────────────────────────────────┐
│  L1 — IMemoryCache (per node)                                      │
│  Lifetime: seconds–minutes                                         │
│  Scope: single Web/API instance                                    │
│  Use: hot reads inside a single request burst (tenant list per     │
│       signed-in user, role lookup, MSP context object)             │
└────────────────────────────────────────────────────────────────────┘
                              ▲ miss
┌────────────────────────────────────────────────────────────────────┐
│  L2 — Redis (cluster-wide distributed cache)                       │
│  Lifetime: minutes–hours                                           │
│  Scope: every Web/API/Worker node                                  │
│  Use: tenant lists, user lists, group lists, license SKUs, role    │
│       definitions, recently fetched device records                 │
│  Notes: stampede protection via SemaphoreSlim per cache key;       │
│         keys versioned by schema rev so a deploy invalidates       │
└────────────────────────────────────────────────────────────────────┘
                              ▲ miss
┌────────────────────────────────────────────────────────────────────┐
│  L3 — PostgreSQL projection tables                                 │
│  Lifetime: hours–days, refreshed on schedule + via Graph delta     │
│  Scope: the canonical durable cache                                │
│  Use: every cross-tenant grid (every user across every customer    │
│       tenant; every Intune policy; every CA policy)                │
│  Notes: typed columns, not JSON blobs; indexed for the queries     │
│         the UI actually issues; partitioned by MspId                │
└────────────────────────────────────────────────────────────────────┘
                              ▲ miss / cold tenant
┌────────────────────────────────────────────────────────────────────┐
│  Graph — BACKGROUND ONLY for warm tenants                          │
│           BOUNDED FALLBACK (one page) for cold tenants             │
│  Notes: never paginate-until-done on a request thread              │
└────────────────────────────────────────────────────────────────────┘
```

### Read-path resolution order

For every "list X for tenant Y" query, the resolver does this:

1. **L1 (`IMemoryCache`).** Return if hot.
2. **L2 (Redis).** Return if present; backfill L1 with a short TTL.
3. **L3 (Postgres projection).** Return if `last_synced_at` is within the freshness window for that resource type (e.g., users: 15 min, group memberships: 1 h, license SKUs: 24 h). Backfill L1 + L2.
4. **Cold-tenant fallback.** If L3 has *zero* rows for `(MspId, CustomerTenantId, ResourceType)` — i.e., the tenant was just onboarded — the request is allowed to fall through to a **single bounded Graph page** (server-paginated, default 999 items) so the UI doesn't render an empty grid. Simultaneously, the resolver enqueues a warmer job to populate the rest. Subsequent reads hit L3.
5. **Stale-but-not-cold.** If L3 has rows but `last_synced_at` is past the freshness window, the resolver returns the stale rows immediately and **kicks the warmer**. The UI footer shows "data refreshed N minutes ago" so users know the state. **Never block on the refresh.**

### Freshness windows (defaults; overridable per-MSP)

| Resource | L2 TTL | L3 freshness | Refresh cadence | Refresh mechanism |
| -------- | ------ | ------------ | --------------- | ----------------- |
| Tenants list (per MSP) | 5 min | 1 h | 15 min | Partner Center delta |
| Users list | 1 min | 15 min | 15 min | Graph users delta |
| Groups list | 5 min | 1 h | 1 h | Graph groups delta |
| Group memberships | 5 min | 1 h | 1 h | Graph delta |
| Devices (Entra) | 5 min | 1 h | 1 h | Graph delta |
| Devices (Intune managed) | 5 min | 1 h | 1 h | Graph delta on managedDevices |
| CA policies | 5 min | 6 h | 6 h | Full read (small list, no delta on CA) |
| License SKUs | 1 h | 24 h | Daily | Full read |
| Sign-in logs | 1 min | n/a (queried, not projected) | On demand | Live query against Graph with cursor pagination |
| Audit log search | n/a | n/a | On demand | Saga; results land in projection on completion |

`last_synced_at` lives on the parent (e.g., `CustomerTenantUserSync` row keyed by `(MspId, CustomerTenantId)`) and is updated transactionally with the projection rows. It is **not** computed from `MAX(updated_at)` — that conflates "I refreshed and saw nothing" with "I never refreshed."

### Cache key conventions

```
tenant:{mspId}:{customerTenantId}:users:list                 # L2 list cache
tenant:{mspId}:{customerTenantId}:users:{userId}             # L2 entity cache
tenant:{mspId}:{customerTenantId}:users:delta-cursor         # delta token (Redis hot mirror; canonical row in Postgres GraphDeltaCursors)
tenant:{mspId}:{customerTenantId}:groups:list
tenant:{mspId}:{customerTenantId}:devices:list
mspscope:{mspId}:tenants:list                                # MSP-level cache
mspscope:{mspId}:user:{userObjectId}:context                 # logged-in MSP user context object
schema:v{schemaRevision}                                     # appended to every key so deploys invalidate without flush
```

Keys are constructed by `IGraphCacheKey` factory methods, never concatenated by hand. The `schemaRevision` segment guarantees that a model change in a deploy doesn't serve stale shapes; old keys age out by TTL.

### Anti-rules — codified in `CLAUDE.md` §8c

- ❌ **No request-time pagination of Graph.** A handler that walks `nextLink` on the request thread is a bug.
- ❌ **No "cache for a few minutes and call Graph anyway."** Either it's served from cache, or it triggers a refresh that updates the cache. There is no third path.
- ❌ **No per-request token re-acquisition.** Tokens are cached in Redis with `SemaphoreSlim` coalescing; one fetch per `(MspId, CustomerTenantId, scope)` even under stampede.
- ❌ **No "store the JSON blob and re-parse it on every read."** L3 is typed columns; the Graph response is not the persistence shape.

This single rule eliminates the largest class of CIPP's user-facing performance complaints.

---

## 4. The write path

Where the read path optimises for "the request thread never paginates Graph", the write path optimises for "the cache never disagrees with reality for longer than one user-perceptible tick".

### The five steps

A command (`CreateUserCommand`, `EditCaPolicyCommand`, `RemediateStandardCommand`, etc.) executes in this order:

```
1. AUTHORIZE            → policy + ICustomerTenantAuthorizationService.AssertAccess(...)
                          (denials are audited as signal, not noise)

2. WRITE-AHEAD AUDIT    → record { actor, msp, customerTenant, command, payload-hash, traceId, "pending" }
                          in a SINGLE Postgres transaction with the next step

3. CALL GRAPH           → IGraphTenantClient.<Action>(...)
                          ▸ Polly v8 pipeline (throttle / retry / bulkhead)
                          ▸ batch when the command writes >1 entity
                          ▸ inspect every batch subresponse (no silent inner 429s)

4. UPDATE PROJECTION    → in the SAME Postgres transaction as the audit-log row's
                          status flip ("pending" → "succeeded"), upsert the
                          authoritative shape returned by Graph into the L3 table.
                          This is the canonical post-write state — not the request
                          DTO, not what the client sent.

5. INVALIDATE + NOTIFY  → del L2 keys for the affected lists/entities; publish a
                          SignalR message on the per-MSP/per-tenant group so all
                          connected clients refresh L1 and update their grids.
```

### Why this exact order

- **Audit before Graph.** If the process dies between the Graph call and the projection update, the audit log already shows "pending"; a reconciler resumes from there. CIPP loses operations that crashed mid-write because there was no pre-call durable record.
- **Projection update in the same Postgres transaction as the audit-log status flip.** If the projection update fails (e.g., a constraint violation we didn't catch in validation), the audit row stays "pending" and a metric increments. We **never** ack a write to the user that the projection didn't capture, because the next read would silently roll back.
- **Invalidate after the projection write.** The order matters: if invalidation happens before projection write, a concurrent read repopulates L2 from the **stale** L3 row. Invalidate-after-write closes the window.
- **SignalR last.** Real-time notification is a courtesy, not a correctness mechanism. Treat it as best-effort; the UI's polling fallback (every 60s for the visible grid) catches anything SignalR drops.

### Idempotency

Every command carries an `IdempotencyKey` header (UUID, client-generated). The handler:

1. Looks up `(MspId, IdempotencyKey)` in `CommandIdempotency`.
2. If hit and complete: return the original result.
3. If hit and pending: return 409 with `Retry-After`.
4. If miss: insert the row, proceed.

This survives client retries without double-creating users / policies / etc. CIPP has no equivalent — its retry semantics are a function of whatever Azure Function host happens to do, which means duplicate `addUser` invocations under load.

### Bulk writes

Bulk operations (e.g., bulk license assignment, bulk user creation) **never** become 1,000 sequential Graph calls. They are:

1. Validated and **decomposed into Graph batch requests** (max 20 per batch, per Microsoft's limit) on the API node.
2. Per batch: dispatched, every subresponse status inspected, partial success captured.
3. Per batch: results streamed back through SignalR with `{ index, status, error? }` so the UI shows row-level progress.
4. The whole bulk is **one** audit record with line items, not 1,000 audit records.

For very large bulks (>500 items), the API immediately enqueues a Hangfire job and returns `202 Accepted` with a poll URL; the SignalR stream replaces the poll for connected clients.

### Compensating actions

When Graph succeeds but the projection update fails, we **do not** roll back Graph. The Graph state is now reality; the projection is wrong. The handler:

1. Logs the divergence with `log.Severity = Error, log.Category = "ProjectionDrift"`.
2. Marks the audit row `succeeded-with-drift`.
3. Enqueues a targeted refresh job for the affected `(tenant, resource, entityId)` so the projection re-syncs from Graph.
4. Returns success to the user — the operation **happened**.

Compensating Graph deletes after a partial failure are explicitly rejected: they introduce a new failure mode (the compensation itself can fail) and confuse audit history. The convention is: write through, log the drift, reconcile from upstream truth.

### What writes never go through

- **No client-driven cache pokes.** The frontend never tells the backend to invalidate a key. Invalidation is owned by the write handler.
- **No "save and refresh page".** After a write, the SignalR broadcast updates the visible grid in place. The page does not reload.
- **No write that bypasses the audit log.** If a code path doesn't write an audit row, it is not allowed to call Graph. This is enforced by the `IAuditingGraphInterceptor` registered on the SDK pipeline.

---

## 5. Multi-MSP isolation

This is a multi-tenant SaaS administering **other** multi-tenant systems. Two layers of tenancy stack:

- **MSP tenant** — our customer (the MSP). Carried in the auth principal's `MspId` claim.
- **Customer tenant** — the M365 tenant the MSP is acting on. Carried explicitly per request as `CustomerTenantId`, validated against the MSP's GDAP relationships before any Graph call.

Cross-MSP data access must be **impossible by construction**, not "we remembered to add a where-clause." Five layers of defense.

### Layer 1 — Auth (primary gate)

- Sign-in is OIDC at the MSP's home tenant via `Microsoft.Identity.Web`.
- The token's `tid` claim, mapped through `MspDirectoryLookup`, sets `MspContextAccessor.MspId` for the entire request scope.
- Every endpoint has `.RequireAuthorization("<policy>")`. There is **no** default-allow path.
- The platform-tenant (us) is its own MSP record; SuperAdmin operations require an explicit `MspId == PlatformMspId` policy.

### Layer 2 — `MspContextAccessor` (DI-scoped, ambient)

- A scoped service populated once per request from the auth principal.
- Injected into `AppDbContext`, MediatR handlers, the Graph client factory, and Hangfire job activators.
- **Never** mutable mid-request. There is no "switch MSP" within a request — that's a new request.
- For background jobs, the job payload carries `MspId` and the Hangfire activator builds the scope around it.

### Layer 3 — EF Core global query filters

Every aggregate root has:

```csharp
modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.MspId == _mspContext.MspId);
```

- Bypassing requires an explicit `IgnoreQueryFilters()` call. CodeQL / lint rule fails the build if `IgnoreQueryFilters` appears outside an allow-listed set of platform-admin queries.
- `MspId` is a `NOT NULL` column on every multi-tenant table, with a CHECK constraint that it matches the row's parent.

### Layer 4 — Customer-tenant authorization

Customer-tenant access is **not** a claim. It is a runtime lookup:

```csharp
await _customerTenantAuth.AssertAccessAsync(mspId, customerTenantId, ct);
```

This service:

1. Reads `MspCustomerTenantRelationship` (our store) to confirm the MSP onboarded the customer.
2. Reads the cached GDAP relationship state (refreshed every 15 min by a background job) to confirm the relationship is still active.
3. Logs the assertion to the audit log.
4. Throws `CustomerTenantAccessDenied` (mapped to 403) if either check fails.

It is called by:

- The Graph client factory before issuing a token.
- Every command handler before mutating projection rows for that customer tenant.
- The SignalR hub on group join.

### Layer 5 — Graph token scoping

Each `IGraphTenantClient` is constructed for **one specific** `(MspId, CustomerTenantId)`. The token it carries:

- Was acquired with that customer tenant's tenant-scoped refresh token (delegated GDAP) **or** the platform's confidential-client cert with the customer-tenant claim (app-only with GDAP scope).
- Is cached under `tenant:{mspId}:{customerTenantId}:graph-token:{scope}`.
- Cannot be reused for a different `(MspId, CustomerTenantId)` — the cache key wouldn't match and the SDK won't accept a token whose `tid` mismatches the call target.

### Cross-MSP defenses summarised

| Defense | What it stops | Where it lives |
| ------- | ------------- | -------------- |
| OIDC `tid` → `MspId` mapping | A user from MSP A signing in and seeing MSP B | `MspContextMiddleware` |
| Endpoint policies | Unauthenticated traffic; insufficient role | ASP.NET Core authorization |
| `MspContextAccessor` ambient | Code paths that "forgot" to filter | DI scope |
| EF global filters | Hand-written queries missing the where-clause | `OnModelCreating` |
| `IgnoreQueryFilters` lint rule | Unsanctioned filter bypass | CI: build failure |
| `ICustomerTenantAuthorizationService` | Cross-customer-tenant access within an MSP | Per command + Graph client factory |
| Token scoping | A leaked token serving a different tenant | Graph SDK + `IGraphTokenProvider` |

Five layers, each independently sufficient to block cross-MSP access. None is the primary gate; all are enforced.

### Platform-admin operations

A small set of operations legitimately span MSPs (billing rollups, fleet health, incident response). These:

- Run only under the `Platform.SuperAdmin` policy.
- Use a dedicated `IPlatformAdminContext` that, when populated, allows `IgnoreQueryFilters()`.
- Are **always audited** with `audit.scope = "cross-msp"` and `audit.justification` (a free-text reason captured at the UI).
- Cannot read operational secrets — those are sealed even from SuperAdmin via Key Vault access policy and require a separate, ticketed break-glass flow.

---

## 6. Graph integration

The first instinct on a CIPP rebuild is to replicate the PowerShell helpers in C#. **Do not do this.** The PowerShell helpers exist because the PowerShell ecosystem lacked typed Graph clients in 2021. .NET has had them since `Microsoft.Graph` v3 and they have only improved. Concretely, the rebuild leans on the SDK for everything the SDK already does, and only writes code where the SDK leaves a gap.

### What `Microsoft.Graph` SDK v5 already gives us

- Typed entity models for every Graph resource (`User`, `Group`, `Device`, `ConditionalAccessPolicy`, `ManagedDevice`, ...).
- `PageIterator<T>` — pagination + throttling + `Retry-After` honouring + cancellation. Replaces 100% of CIPP-API's `nextLink` chasing.
- `BatchRequestContent` — JSON `$batch` with per-subresponse status inspection. Replaces the PowerShell helper that silently lost inner 429s.
- `Delta()` extensions on `users`, `groups`, `directoryObjects`, `devices`, `messages`, with `deltaLink` extraction.
- Native cancellation token plumbing through the Kiota request pipeline.
- A `IRequestAdapter` we can replace or wrap to add custom telemetry, audit, and resilience policies.

### What `Microsoft.Identity.Web` already gives us

- OBO (`AcquireTokenOnBehalfOfAsync`), refresh, certificate auth, MSI, confidential client.
- Distributed token cache via `MicrosoftIdentityWebChallengeUserException`-aware middleware.
- App-only token acquisition with `client_credentials` flow for background workers.
- Multi-tenant sign-in with home-tenant `tid` claim resolution.

The CIPP "SAM ceremony" — refresh tokens mirrored into env vars, the `CIPP.CIPPTokenCache` shim compiled into `CIPPSharp.dll`, the per-tenant token plumbing — collapses to: `IConfidentialClientApplication` + `ITokenAcquisition` + a thin `IRefreshTokenStore` over `IDataProtectionProvider`-protected Postgres rows.

### `IGraphTenantClient` — what we *do* write

The one thin abstraction we own:

```csharp
public interface IGraphTenantClient
{
    GraphServiceClient Client { get; }                // typed SDK surface
    string TenantId { get; }
    Guid MspId { get; }
    Task<HttpResponseMessage> SendBatchAsync(
        BatchRequestContent batch, CancellationToken ct);
}
```

It exists to:

1. **Carry tenant identity** so every call is tagged with `(MspId, CustomerTenantId)` for telemetry without the caller passing them again.
2. **Wrap the request adapter** in a Polly v8 pipeline (see below).
3. **Force batch subresponse inspection** — `SendBatchAsync` returns success **only if every subresponse was 2xx**. A 200 outer response containing 429 inner responses is a failure.
4. **Audit** — every Graph call is recorded with endpoint, status, retry count, latency.

It does **not** wrap individual Graph operations (`AddUserAsync`, `GetUsersAsync`, etc.). Application code calls `_client.Client.Users.PostAsync(...)` — straight SDK. The factory is the abstraction; the operations are not.

### The Polly v8 pipeline

Built once in `Program.cs`, applied to the SDK's `HttpClient` via a `DelegatingHandler`:

```
┌────────────────────────────────────────────────────────────────────┐
│ Outer:   Bulkhead per MSP (concurrency cap N=64 in-flight)         │
│ │                                                                  │
│ ├─ Inner: Per-customer-tenant rate limiter (token bucket; Redis)   │
│ │                                                                  │
│ ├─ Inner: Retry on 429 honoring Retry-After (cap 60s, max 5)       │
│ │                                                                  │
│ ├─ Inner: Retry on transient 5xx with jittered exponential backoff │
│ │                                                                  │
│ ├─ Inner: Timeout per attempt (30s default)                        │
│ │                                                                  │
│ └─ Inner: Circuit breaker per (msp, customerTenant)                │
└────────────────────────────────────────────────────────────────────┘
```

- **Bulkhead per MSP** stops a noisy MSP from starving others' Graph quota.
- **Per-customer-tenant rate limiter** stops a single MSP's fan-out (Standards run across 100 tenants) from tripping Graph's per-tenant quota.
- **`Retry-After` honouring** is non-negotiable — Microsoft documents this; ignoring it is how you get 429-banned.
- **Circuit breaker per `(msp, customerTenant)`** stops flooding a tenant whose Graph endpoint is failing systemically (e.g., directory replication issue) and surfacing the failure to the UI fast.

### Pagination — the rule

The application never writes:

```csharp
var users = new List<User>();
var page = await _client.Client.Users.GetAsync(...);
while (page.OdataNextLink != null) { /* ... */ }
```

The application writes:

```csharp
var page = await _client.Client.Users.GetAsync(rb => { rb.QueryParameters.Top = 999; });
var iterator = PageIterator<User, UserCollectionResponse>
    .CreatePageIterator(_client.Client, page, async user => { /* project */ return true; });
await iterator.IterateAsync(ct);
```

— and only ever inside a **background warmer**, never on a request thread (see §3).

### Batching — the rule

Bulk writes (multi-user create, multi-policy assign, etc.) build a `BatchRequestContent` of up to 20 sub-requests, dispatch via `_client.SendBatchAsync(batch, ct)`, and inspect the `BatchResponseContent` for **every** sub-response status. Inner 429s honour the inner `Retry-After`; the un-acked sub-requests are repacked into a follow-up batch. CIPP's PowerShell batch helper does not do this — that bug is the source of the "BPA ran but half my tenants didn't get touched" reports.

### Delta queries — the rule

Every list resource that supports `delta` is synced via `delta` on every refresh except the first. The `deltaLink` cursor is persisted in `GraphDeltaCursors`:

```sql
CREATE TABLE GraphDeltaCursors (
    MspId UUID NOT NULL,
    CustomerTenantId UUID NOT NULL,
    ResourceType TEXT NOT NULL,        -- 'users', 'groups', 'devices', ...
    DeltaLink TEXT NOT NULL,
    LastSyncedAt TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (MspId, CustomerTenantId, ResourceType)
);
```

A failed delta cursor (token expired, resync required) drops back to a full sync **and** emits a metric. If the delta-failure rate exceeds 5% across the fleet, alarms fire — that's a signal of a regression in either Graph or our cursor handling, not a routine.

### Where the SDK leaves a gap

A small set of M365 surfaces (Exchange Online cmdlets, Teams CAP edge cases, some legacy Skype-for-Business voice settings) still require PowerShell or REST calls outside the typed SDK. These are isolated in:

- `TenantManagement.Graph.ExchangeShim` — runs inside our process via `System.Management.Automation` against an in-process runspace pool (one per `(MspId, CustomerTenantId)`), strictly bounded with timeouts.
- `TenantManagement.Graph.LegacyRest` — direct `HttpClient` calls to specific endpoints we explicitly enumerate.

Both go through the same Polly pipeline and same audit hooks as the typed SDK. The rest of the app does not know they exist.

### Anti-rules

- ❌ **Do not hand-roll OAuth.** `Microsoft.Identity.Web` does it. If a feature seems to need a custom flow, the SDK probably does it under a different method name.
- ❌ **Do not hand-roll Graph batch handling.** Use `BatchRequestContent` and inspect every subresponse.
- ❌ **Do not hand-roll pagination.** Use `PageIterator<T>`. If the SDK's pager doesn't fit, file a bug at `microsoft/msgraph-sdk-dotnet`, do not vendor a copy.
- ❌ **Do not hand-roll delta.** Use the SDK's `Delta()` extensions; persist `deltaLink`; replay.
- ❌ **Do not write a "graph_request" generic forwarder** ([CIPP-API has one](https://github.com/KelvinTegelaar/CIPP-API/blob/master/Modules/CIPPCore/Public/GraphHelper)). It is unsafe (any caller can dispatch any URL) and pointless (the SDK already does this typed).

---
