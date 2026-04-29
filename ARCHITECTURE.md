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

## 7. Auth and secret management

### MSP-user sign-in (front door)

- OpenID Connect against the MSP user's home Entra tenant via `Microsoft.Identity.Web.UI` (Blazor) and `Microsoft.Identity.Web` (API).
- The app is a **multi-tenant** Entra app registration in our platform tenant. MSPs consent at first use; consent is per-MSP and tracked by tenant.
- Sign-in resolves `tid → MspId` via `MspDirectoryLookup`. An unrecognised `tid` lands on a self-service onboarding page (only if the MSP has been provisioned), otherwise 403.
- The sign-in cookie is **encrypted server-side** (DataProtection key in Key Vault) and short-lived (8h). Refresh is silent via the auth cookie's sliding window; re-sign-in is required after 24h regardless.
- MFA is required at the home tenant — we don't enforce a second factor ourselves. CA policies on the home tenant are the gate. The app checks `acrs` / `amr` claims and refuses if they don't include MFA.

### BFF posture — tokens never leave the server

The Blazor Web App runs **server-rendered** by default, so:

- Access tokens for Graph never reach the browser.
- The browser holds only the auth cookie.
- Calls from Interactive Server components to Graph go through the API on the server.
- For the rare WebAssembly islands, the client uses the BFF cookie + `/api/...` calls; it never gets a Graph token.

This eliminates a class of CIPP frontend issues where tokens transit the browser via Static Web Apps EasyAuth headers.

### OBO for delegated Graph (the per-user case)

When an MSP user clicks "list users in Customer Tenant Foo":

```
1. The API receives the request with the user's bearer token (audience: api://.../).
2. ITokenAcquisition.GetAccessTokenForUserAsync(scopes, tenantId: foo)
   → On-Behalf-Of flow exchanges the API token for a Graph token in
     the Foo customer tenant, scoped to the user's GDAP relationship.
3. The token is cached in the distributed cache (Redis-backed
   IDistributedCache) keyed by (UserObjectId, MspId, CustomerTenantId, scope).
4. IGraphTenantClient(MspId=msp1, CustomerTenantId=foo) hands that
   token to the SDK.
```

OBO is the right answer for any UI-driven action: the audit trail records the **end-user** as the actor in Graph's own audit log, not a service principal. CIPP loses this because most paths run as a confidential client.

### App-only for background work (the system case)

Background jobs (delta sync, Standards run, Hangfire scheduled refreshers) act on behalf of the MSP, not a specific user. Auth is `client_credentials` with the platform's certificate, claiming the customer tenant as the target via the `tenant` parameter:

- Cert lives in Key Vault; `Microsoft.Identity.Web` reads it at startup.
- Token cached the same way as OBO but keyed by `(MspId, CustomerTenantId, scope)` (no user component).
- Audit attribution: `actor: "system"`, `actor_subject: <jobId>`, `principal: app://.../{platformAppId}`.
- The audit log clearly separates "user did X" from "scheduled job did X" so operators can answer "did a human or a robot do this?"

### Refresh tokens (delegated GDAP back-office paths)

Some legacy paths require a long-lived **refresh** token bound to the MSP user who consented to GDAP. These tokens:

- Are **never** in environment variables. CIPP's `RefreshToken` env-var pattern is explicitly forbidden.
- Live in `RefreshTokens` Postgres table, encrypted via `IDataProtectionProvider` (key in Key Vault, rotated annually).
- Are accessed only through `IRefreshTokenStore`, which:
  - Checks `MspId` matches the calling context.
  - Decrypts in-process; the plaintext never crosses a process boundary or appears in a log line.
  - Marks the row as `accessed_at` for audit / staleness detection.
- Are rotated by a Hangfire job that watches expiry and runs a refresh exchange before the 90-day window closes.

### Token caching

```
Layer    Store      Key                                                Purpose
-----    -----      ---                                                -------
L1       per-node   in-process IMemoryCache                            request-burst dedup
L2       Redis      tenant:{mspId}:{cTenantId}:graph-token:{scope}     cluster-wide token cache
canon    Postgres   RefreshTokens (encrypted)                          source of truth for refresh
```

`IGraphTokenProvider` coalesces token fetches: when N requests for the same `(MspId, CustomerTenantId, scope)` arrive simultaneously and L2 misses, **one** STS call happens; the rest await the result via a `SemaphoreSlim` keyed on the cache key. CIPP, lacking a real backend, pays the STS cost on every cache miss.

### Secret store

| Secret class | Where | Access pattern |
| ------------ | ----- | -------------- |
| App registration cert (private key) | Key Vault | `Microsoft.Identity.Web` reads at startup; reloaded on rotation |
| DataProtection master key | Key Vault | ASP.NET Core DataProtection unseals refresh-token rows |
| Postgres connection string | Key Vault reference in `appsettings.{env}.json` | Read at startup; `npgsql` connection pool |
| PSA / RMM API keys | Postgres `IntegrationCredentials` table, encrypted via DataProtection | Read by `IIntegrationCredentialStore` per outbound call |
| Webhook signing secrets | Same as PSA keys | Read by webhook receivers / senders |
| MSP-tenant refresh tokens | `RefreshTokens` table, encrypted | `IRefreshTokenStore` only |

Secrets **never** appear in:
- `appsettings.*.json` (only Key Vault references).
- environment variables (period — even local dev uses `.env` mounted as a Docker secret).
- log lines (lint rule: `ILogger` parameters are scanned; values matching token / key / secret regex throw at compile time).
- error messages or exception text.
- audit log payload bodies (redaction by `IPiiRedactor`).
- TraceId / span attributes.

### Rotation

- App reg certs: 90-day cycle, automated via Key Vault auto-rotate + a deploy-side reload (`X509Certificate2` is reloaded by `Microsoft.Identity.Web` without a restart).
- DataProtection keys: 90-day cycle, automated; key history retained 1y for retroactive decrypt.
- Postgres credentials: per-environment, rotated at deploy via Key Vault.
- Refresh tokens: rolling refresh on use; expired-without-use beyond 60 days triggers a "reauthorize" notice on the MSP's settings page.

### Anti-rules

- ❌ Refresh tokens in env vars.
- ❌ Plaintext secrets in `appsettings.{env}.json`.
- ❌ Static (non-rotating) symmetric secrets for service-to-service.
- ❌ A custom token cache (`CIPPSharp.dll`-equivalent). The `IDistributedCache` + `Microsoft.Identity.Web` cache provider is the cache.
- ❌ `Console.WriteLine`-ing a token "to debug." There is no scenario in which this is acceptable.

---

## 8. Background processing

The rebuild has three classes of background work and **one** rule that holds across all of them: **every job is idempotent**. If a job cannot be made idempotent, it isn't ready for production.

### The three classes

| Class | Tech | Lifetime | Examples |
| ----- | ---- | -------- | -------- |
| **Recurring / cron** | **Hangfire** (Redis storage) | seconds–minutes per run, every N min/h/d | Per-tenant delta refreshers, token expiry sweeps, Standards reports, license sync, GDAP relationship refresh |
| **Fan-out work queues** | **Azure Service Bus** (sessions for ordering when needed) | minutes–hours total | "Run Standard X across every tenant of MSP Y"; "Dispatch alert to all PSA integrations"; "Bulk user create across 1,000 rows" |
| **Long-running orchestrations / sagas** | **Code-defined sagas** persisted to Postgres (no Durable Functions) | hours–days, surviving deploys | Tenant onboarding (GDAP invite → role mapping → SAM bootstrap → first standards run); audit-log searches; mass remediation runs with rollback paths |

### Why Hangfire — and not the host's timer triggers

- Real cron with `* * * * *` semantics, not "every N seconds" approximation.
- Jobs survive restarts; Redis-backed persistence; built-in retry with exponential backoff and DLQ.
- Per-job state visible in the Hangfire dashboard — operators can see what's running, what failed, and re-queue without a deploy.
- One-shot fire-and-forget enqueues (`BackgroundJob.Enqueue<T>(j => j.Run(args))`) replace CIPP's Function host queue trigger ceremony.

### Why Service Bus — and not Hangfire — for fan-out

- Native session-based ordering when an MSP needs "process customers in this order" guarantees.
- Per-subscription dead-letter queues with replay tooling.
- Cross-cluster reliability: the orchestrator (the API node enqueuing work) and the workers (the worker nodes consuming) decouple cleanly; each scales on its own metric.
- Backpressure: a slow consumer doesn't queue-overflow Hangfire's Redis.

### Why sagas — and not Durable Functions

CIPP's Durable Functions caused two distinct problems documented in their own README and FAQ:

1. **Strict version matching.** Mid-deploy state nukes when the new code's orchestration version doesn't match the running one — leading to a user-facing "Clear Durable Queue" maintenance UI, which is the opposite of operational excellence.
2. **Opaque state.** Recovering from a partially completed orchestration requires Function-host log spelunking; the state isn't queryable from the app.

Our sagas are **code-defined** state machines persisted to a `Sagas` Postgres table:

```sql
CREATE TABLE Sagas (
    SagaId UUID PRIMARY KEY,
    MspId UUID NOT NULL,
    SagaType TEXT NOT NULL,            -- 'TenantOnboarding', 'AuditLogSearch', ...
    Version INT NOT NULL,              -- saga code version at last step
    State JSONB NOT NULL,              -- typed via System.Text.Json
    Status TEXT NOT NULL,              -- 'running' | 'awaiting' | 'completed' | 'failed'
    AwaitingMessage TEXT NULL,         -- correlation id for the message we're waiting for
    LastStepAt TIMESTAMPTZ NOT NULL,
    NextWakeAt TIMESTAMPTZ NULL,
    TraceId TEXT NOT NULL
);
```

- Every step is **idempotent** and writes the next state in the same Postgres transaction as any side-effect tracking.
- A deploy with a new saga version does **not** invalidate in-flight sagas. The handler resolves by `(SagaType, Version)`; old versions stay in the binary until their last in-flight saga completes (typically a few hours after rollout).
- Resumption uses `SagaId` from a Service Bus message, a Hangfire timer wake-up, or a webhook callback. There is no "magic" replay.
- Sagas are queryable: operators can `SELECT * FROM Sagas WHERE Status = 'awaiting' AND NextWakeAt < NOW() - INTERVAL '1 hour'` to find stuck flows.

### The warmer scheduler

The single most important background system is the per-tenant warmer. It exists to keep L3 fresh so request threads never paginate Graph (§3).

```
Hangfire recurring job: warm-msp-{mspId}     (every 1m)
    → enumerate active tenants for the MSP
    → for each tenant, check:
        - L3.last_synced_at vs. resource freshness window
        - GraphDeltaCursors row presence
    → enqueue per-tenant per-resource warm jobs (Hangfire fire-and-forget)
        - one job per (MspId, CustomerTenantId, ResourceType)
        - bounded concurrency via the per-MSP bulkhead (§6 Polly pipeline)
        - delta sync if cursor exists; full sync if not
    → write last_synced_at on success in the same txn as projection upserts
```

- The per-tenant warm jobs are **stateless**. Re-running one is safe — the projection upsert is keyed on the entity's stable id and the cursor advances only on success.
- Failure does **not** poison the queue. A failed warm logs, increments a metric, and the next scheduled run picks it up. Persistent failure (3 consecutive runs) raises an alert and pauses that resource for that tenant.
- The cadence per resource is set in `WarmerSchedule` config (defaults in §3), per-MSP-overridable. Hot resources (users) refresh every 15 min; cold ones (license SKUs) every 24 h.

### Fan-out: per-MSP bulkhead

When an MSP runs "deploy Standard X across all 200 customer tenants":

1. The API command enqueues **one** Service Bus message: `RunStandardCommand { MspId, StandardId, ScopeFilter }`.
2. The orchestrator consumer fans out to per-tenant messages: `RunStandardOnTenantCommand { MspId, StandardId, CustomerTenantId }`.
3. The per-tenant consumer respects:
   - The per-MSP bulkhead (concurrency cap N).
   - The per-customer-tenant rate limiter (Graph-friendly).
   - Retry policy with DLQ on persistent failure.
4. Progress is published via SignalR per-MSP group: `{ standardId, completed: K, total: N, failures: [...] }`.

CIPP fan-out is bounded only by the Function host's auto-scaling, which means a single MSP can saturate it (a regression noted in Discussion `#4979`). Our bulkhead is per-MSP, so MSP A's Standards run cannot starve MSP B's UI.

### Idempotency

- **Cron jobs**: idempotent by construction — re-running the warmer is the design.
- **Service Bus messages**: every message has a `MessageId`; consumers check an `IdempotencyKey` table before handling, write a row on success, and skip on duplicate.
- **Sagas**: each step writes the next state in the same transaction as any side-effect record, so a step re-run is a no-op.
- **Outbox pattern** for cross-bus invariants: when a command both writes to Postgres and emits a Service Bus message, the message is written to a `MessageOutbox` table in the same transaction; a relay sends it. No "I committed but the message vanished" race.

### Anti-rules

- ❌ "Run any handler by name from a queue payload" (CIPP's `& $cmdletName @args` pattern). Every handler is explicitly registered and its argument shape is typed.
- ❌ Polling Graph from a request thread to fake real-time updates. Use SignalR; the warmer does the polling.
- ❌ Sagas with mutable global state. State is the row; the row is the truth.
- ❌ "We retry forever." DLQ exists for a reason; persistent failures escalate.

---

## 9. Persistence model

PostgreSQL is the **only** relational store. Blob is for archives. Redis is for cache / rate-limit / SignalR backplane / Hangfire. Service Bus for fan-out. **No Azure Tables, no Cosmos, no per-feature DB sprawl** — that pattern is one of CIPP's main scalability ceilings (the 64 KB row limit on Tables is documented as breaking templates since 2023).

### Why Postgres specifically

- Mature `jsonb` for the small set of legitimately schemaless data (Standards settings, template payloads). `jsonb` indexes via GIN.
- Partial indexes (e.g., `WHERE is_deleted = false AND msp_id = ...`) for projection hot paths.
- Logical replication for read replicas / blue-green data migrations.
- Row-level security as a possible second-line defense (we don't rely on it primarily — see §5 — but it's there).
- `pg_partman` for time-partitioning of the audit log and activity feed.

### Schema layout

One database, multiple schemas. Each bounded context owns its schema; cross-context FKs are forbidden.

```
identity.users_projection
identity.groups_projection
identity.devices_projection
endpoint_management.managed_devices_projection
endpoint_management.intune_policies_projection
exchange_online.mailboxes_projection
collaboration.sites_projection
security.alerts_projection
tenants.customer_tenants
tenants.gdap_relationships
standards.standard_runs
standards.drift_baselines
templates.ca_templates
templates.intune_templates
reports.license_usage_history
automation.scheduled_jobs
automation.scheduled_job_runs
automation.integration_credentials
platform_admin.msps
platform_admin.msp_users
platform_admin.msp_roles
platform_admin.refresh_tokens
observability.audit_log
observability.activity_feed
graph.delta_cursors
graph.command_idempotency
graph.message_outbox
graph.sagas
```

EF Core composes this single database from per-context `DbContext` partials — there is one physical `AppDbContext` at runtime, but its `OnModelCreating` is split per context so contexts evolve independently.

### Mandatory columns on every multi-tenant table

```
MspId           UUID NOT NULL                               -- platform tenancy
CreatedAtUtc    TIMESTAMPTZ NOT NULL DEFAULT NOW()
CreatedById     UUID NOT NULL                               -- MspUserId or system principal
UpdatedAtUtc    TIMESTAMPTZ NOT NULL DEFAULT NOW()
UpdatedById     UUID NOT NULL
RowVersion      BYTEA NOT NULL                              -- xmin-based concurrency token
IsDeleted       BOOLEAN NOT NULL DEFAULT FALSE              -- soft-delete + global query filter
```

These are enforced by an EF Core `IModelConvention` that fails the build if a multi-tenant entity is missing any of them.

### Typed columns vs. jsonb — the rule

Default to typed columns. Use `jsonb` only when **the shape is genuinely free-form** and the queries against it are prefix-matched / containment-matched — not for every Graph response we happen to want to cache.

| Right use of jsonb | Wrong use of jsonb |
| ------------------ | ------------------ |
| Standards settings (different shape per standard) | Cached user object (typed columns + GIN on `extension_attributes`) |
| Template payloads (CA, Intune, transport rule — opaque to us until applied) | Audit log payload — typed columns for `actor`, `command`, `target`, `outcome`, `status` |
| PSA integration provider-specific config | License SKU list (typed) |
| Drift diffs as JSON Patch | Group memberships (typed join table) |

### Indexing strategy

- Every multi-tenant table has a composite index on `(MspId, CustomerTenantId, ...)` for the most common projection lookups.
- Lists that drive grids have **covering indexes** including the columns the UI sorts on.
- `(MspId, IsDeleted)` partial indexes on hot tables avoid scanning soft-deleted rows.
- `GIN` on jsonb columns where containment queries are real (e.g., `template_payload @> '{"type": "ca"}'`).
- Time-series tables (`audit_log`, `activity_feed`, `standard_runs`) are **time-partitioned** monthly; old partitions are detached and archived to Blob after 90 days.

### Soft-delete vs. hard-delete

- Default: **soft-delete** via `IsDeleted = TRUE` + global query filter. Reversible, audit-friendly.
- **Hard-delete** is allowed only for ephemeral records (job runs, idempotency keys, message outbox) older than 90 days, performed by a `pg_partman`-driven retention job.
- Compliance-driven hard-deletes (e.g., GDPR right-to-erasure on an MSP user record) go through `IPlatformAdminService.HardDeleteAsync`, which:
  1. Asserts SuperAdmin + an active deletion ticket id.
  2. Writes an audit row recording the ticket.
  3. Hard-deletes the record and any rows referencing it via cascade.

### Migrations

- EF Core migrations checked into source control under `Infrastructure/Persistence/Migrations`.
- Migrations **never run on app startup** in any non-dev environment. CIPP's pattern of running schema changes when the Function App boots is a recipe for half-migrated state under load.
- Migrations are a CI step against a target environment **before** the app rollout; rollout fails fast if migrations failed.
- Backward-compatible migrations only: add column nullable → backfill → mark NOT NULL in next deploy. No "stop the world" schema breaks.
- Migration tests run against a fresh Postgres + the previous-deploy snapshot to catch shape regressions.

### Concurrency control

- `RowVersion` (`xmin`) on every row for optimistic concurrency.
- Update commands check `RowVersion` and fail with `409 Conflict` if it changed; the UI re-fetches and shows a 3-way diff.
- Long-running edits (e.g., a multi-step CA policy editor) use **draft rows** in a separate `<table>_drafts` schema, materialized into the canonical row only on save.

### Read replicas and blue-green

- Production runs **one** primary + **one** read replica (initial scale; expand later).
- Read replica handles long-running report queries (`Reports` context) so OLTP is not perturbed.
- Blue-green data migrations use logical replication: new schema on the green side, dual-write during cutover, old side decommissioned after a soak period.

### Anti-rules

- ❌ Storing the JSON blob from Graph and re-parsing it on every read. Project to typed columns.
- ❌ Cross-context FKs. Use a `MspId + EntityId` "weak reference" in the dependent context, validated at the application layer.
- ❌ Auto-migration on app startup outside dev.
- ❌ Schema changes that aren't backward-compatible within a single deploy.
- ❌ One row per "every K-V config item" anti-pattern (e.g., a `Settings` table with `key, value` columns). Use a typed config row per concept.

---

## 10. Real-time push (SignalR)

CIPP polls. A page that "watches" a long-running operation re-fetches every N seconds, paying the Function-App cold-start tax over and over. The rebuild's UI is **push-driven** for every state that changes asynchronously.

### Hubs (one per domain)

```
/hubs/identity        — user/group/device CRUD progress; bulk results
/hubs/scheduler       — Hangfire job state, fan-out progress, saga events
/hubs/standards       — per-tenant standard run progress and drift events
/hubs/security        — alerts, incidents, secure-score deltas
/hubs/onboarding      — tenant onboarding saga step-by-step
```

Each hub:

- Authenticates via the auth cookie (BFF, no token in browser).
- Resolves `MspId` from the principal; the connection is bound to that MSP.
- Joins per-MSP groups (`msp:{mspId}`) and per-customer-tenant groups (`msp:{mspId}:tenant:{cTenantId}`) on demand from the client.
- Backend publishes via `IHubContext<TheHub>.Clients.Group(...)`; backplane is Redis.

### What goes over SignalR — and what doesn't

| Goes over SignalR | Stays out of SignalR |
| ----------------- | -------------------- |
| "Bulk add user 47/200 succeeded" | The full user list — that's an L2/L3 read after invalidation |
| "Standard X completed on tenant Foo with 3 drift items" | The per-tenant drift detail — fetch on click |
| "Saga 'TenantOnboarding-{id}' moved to step 'GdapInvited'" | Saga internal state — fetch on the saga details page |
| "New high-severity alert for tenant Bar" | The alert body — fetch on click |
| Ephemeral toast notifications | Anything that needs to be reliably delivered (use Service Bus + the audit log) |

SignalR is **best-effort, low-latency, ephemeral**. If a client missed a message because it disconnected, the next page load reads from L2/L3 — which is now correct because the write path invalidated cache before publishing.

### Backpressure

A noisy operation does not produce one SignalR message per Graph response. Producers throttle: at most 1 update per group per 200ms, debounced.

---

## 11. Observability

Three pillars (logs, metrics, traces) via OpenTelemetry, exported to Application Insights in Azure or Grafana Cloud for self-host.

### Logs

- `ILogger<T>` with **structured logging only**.
- Event ids in per-area static classes (e.g., `LogEvents.Identity.UserCreated = new EventId(1001, "UserCreated")`).
- Standard properties on every log: `MspId`, `CustomerTenantId` (when applicable), `TraceId`, `SpanId`, `EndpointName`.
- PII redaction by `IPiiRedactor` middleware on the structured log enricher — emails, phone numbers, sign-in identifiers are partial-masked.
- Secrets, tokens, and full request bodies of write operations are **never** logged.

### Metrics

Standard set captured per request and per background job:

| Metric | Type | Tags |
| ------ | ---- | ---- |
| `graph_request_duration_ms` | histogram | `customer.tenant.id`, `endpoint`, `status_code`, `retry.count` |
| `graph_request_total` | counter | same |
| `graph_throttle_total` | counter | `customer.tenant.id` |
| `cache_hit_total` | counter | `layer` (l1/l2/l3), `resource` |
| `cache_miss_total` | counter | same |
| `delta_sync_duration_ms` | histogram | `resource`, `customer.tenant.id` |
| `delta_failure_total` | counter | `resource`, `failure_reason` |
| `saga_step_duration_ms` | histogram | `saga_type`, `step` |
| `signalr_publish_total` | counter | `hub`, `group` |
| `auth_denial_total` | counter | `policy`, `reason` |

SLIs derived from these: graph p95 latency, cache hit ratio per resource, delta failure rate, write-path p95 (audit-ack to projection-ack).

### Traces

- Every HTTP request gets a `TraceId` propagated through Service Bus messages, Hangfire jobs, and SignalR publishes.
- Every Graph call emits a span with `customer.tenant.id`, `graph.endpoint`, `http.status_code`, `retry.count` attributes.
- Saga steps emit child spans linked by saga id.
- Sampling: 100% of error traces, 10% of success (configurable per environment).

### Audit log

Audit is **not** logging. It's a first-class, queryable, retained data class:

- Every command and Graph mutation writes an audit row in the same Postgres transaction as the projection write.
- Schema: `(MspId, CustomerTenantId, ActorPrincipal, ActorType, Command, TargetType, TargetId, Outcome, Status, Payload, TraceId, AtUtc)`.
- 7-year retention with monthly partition archival to Blob.
- Cross-MSP read access requires SuperAdmin + ticket id.

### Health endpoints

- `/health/live` — process liveness; never depends on downstream.
- `/health/ready` — readiness; checks Postgres, Redis, and the ability to acquire a token from the cached cred. Hard-fails if any are out.
- `/health/startup` — startup-only check used by the orchestrator to gate traffic until migrations and warm-cache are baseline.

---

## 12. Deployment topology

### Environments

| Environment | Purpose | Data |
| ----------- | ------- | ---- |
| **dev** | Per-engineer; Docker Compose locally | Synthetic |
| **ci** | PR validation; Testcontainers per test class | Per-run scratch |
| **staging** | Pre-prod soak; mirrors prod infra at 1/4 scale | Anonymised prod snapshot weekly |
| **prod** | Customer traffic | Real |

### Topology (Azure-first; portable to any cloud running Container Apps / EKS / GKE)

```
┌─────────────────────────────────────────────────────────────────┐
│ Azure Front Door (WAF, TLS, geo-routing, caching of static)     │
└─────────────────────────────────────────────────────────────────┘
                  │
   ┌──────────────┴──────────────┐
   │                             │
┌─────────────────┐         ┌─────────────────┐
│ Container Apps  │         │ Container Apps  │
│ Web (Blazor)    │         │ Worker (HF/SB)  │
│ — autoscale on  │         │ — autoscale on  │
│   HTTP RPS      │         │   queue depth   │
└─────────────────┘         └─────────────────┘
   │                             │
   └──────────────┬──────────────┘
                  │
   ┌──────────────┼─────────────────┬─────────────────┐
   │              │                 │                 │
┌──────────┐ ┌──────────┐    ┌──────────────┐  ┌────────────┐
│ Postgres │ │ Redis    │    │ Service Bus  │  │ Key Vault  │
│ Flex Srv │ │ Premium  │    │ Premium      │  │            │
│ + replica│ │ (HA)     │    │ (sessions)   │  │            │
└──────────┘ └──────────┘    └──────────────┘  └────────────┘
   │              │                 │
   └──────────────┴─────────────────┴────► OpenTelemetry → App Insights
```

### Single deployable shape

The codebase produces three container images from one solution:

1. **Web** — ASP.NET Core API + Blazor Web App.
2. **Worker** — Hangfire host + Service Bus consumers.
3. **Migrator** — short-lived; runs migrations against the target Postgres in CI.

The Web and Worker images can be merged into a single deployable for self-host where simpler ops outweigh independent scaling.

### Blue-green rollout

- Container Apps revisions: traffic split 100/0 → 50/50 → 0/100 over a soak window.
- DB migrations run as a CI step before the rollout (§9).
- SignalR connections drain on the old revision; new connections land on the new one.
- Hangfire honors a cooperative shutdown signal; in-flight jobs finish before the old container exits.

### Self-host

A `docker-compose.yml` produces the same topology with single-instance Postgres / Redis / Azurite (or RabbitMQ as a Service Bus stand-in). The same migrator image runs migrations; the same Web and Worker images run the app. Self-host MSPs pull tagged releases; there is no fork.

---

## 13. Failure modes and recovery

The point of cataloguing failure modes is to make them **bounded** — small blast radius, clear recovery, no surprise at 3am.

| Failure | Detection | Bounded blast radius | Recovery |
| ------- | --------- | -------------------- | -------- |
| Graph throttling for one customer tenant | 429 metric breach | That tenant's writes pause; reads serve from L3 | Polly retries with `Retry-After`; circuit breaker trips if persistent |
| Graph endpoint regional outage | 5xx surge across many tenants | All Graph traffic in that region degrades | Reads serve stale L3 with banner; writes fail fast with retry-after; SLO violation alarms; nothing local to roll back |
| Postgres primary loss | Health endpoint failure | Writes pause; reads continue from replica (read-only banner) | Failover to replica; promote; resume |
| Redis loss | L2 unreachable | L1 hit ratio increases; L3 reads grow; latency rises by ~10ms | Reconnect; warm by traffic; **no data loss** (Redis is cache + ephemeral state) |
| Service Bus loss | Outbox queue grows | Fan-out work pauses; UI commands return 202 with delayed processing | Reconnect; outbox relay drains; sagas resume |
| Hangfire scheduler crash | Job heartbeat metric | Recurring jobs delayed by N seconds | Hangfire restart on the worker; jobs resume from Redis state |
| One worker node crash | Container Apps health | That node's in-flight work is requeued | Container Apps restarts the revision; sagas resume |
| Refresh-token leak (operational secret) | Anomalous Graph 401s + access-pattern alert | One MSP impacted | Rotate via `IRefreshTokenStore.RevokeAll(mspId)`; force re-consent; audit-log forensics |
| DataProtection key compromise | Out-of-band signal | Refresh tokens become un-decryptable | Rotate to new key version; force re-consent across all MSPs; old ciphertext is dead-on-arrival |
| Cache poisoning (bad delta) | Drift between L3 and Graph spot-check | One resource type for one tenant stale | Cursor reset → full sync; metric records the failure |
| Mass Standards remediation gone wrong | User-initiated alarm or per-saga audit | Bounded by saga's compensation policy | Saga compensation steps; if compensation isn't safe, halt and alert |
| Bad deploy | Health check or error rate | Traffic stays on old revision | Container Apps revision rollback (1 click) |
| Schema migration that can't roll back | CI gate before deploy | Doesn't reach prod | Migration pipeline blocks |

### Disaster recovery

- Postgres point-in-time restore retained 35 days.
- Blob (audit archive, drift baselines) geo-redundant.
- Redis is **disposable** by design.
- Recovery objectives:
  - **RPO** (data loss tolerance): 5 min for Postgres, 0 for Blob, ∞ for Redis (we re-warm).
  - **RTO** (time to restore): 30 min for primary outage to a fresh region.

### What is explicitly not solved here

- A user-initiated "Clear Durable Queue" UI (CIPP has one). The rebuild does not have the failure mode that requires it; sagas are queryable and resumable.
- A "Forget my fork; pull upstream" workflow. The rebuild is single-source; there are no forks to reconcile.
- A "PowerShell module out-of-date" warning. There is no PowerShell module.

---

## 14. The architecture in one paragraph

A multi-tenant ASP.NET Core 10 backend administering customer M365 tenants on behalf of MSPs, using `Microsoft.Graph` v5 + `Microsoft.Identity.Web` for everything those libraries already do, with a real four-layer cache (L1 IMemoryCache → L2 Redis → L3 Postgres projections refreshed by background delta workers → bounded fallback to Graph), a typed write path that audits before Graph and updates the projection in the same Postgres transaction as the audit-status flip, multi-MSP isolation enforced in five independent layers, secrets in Key Vault and DataProtection-encrypted Postgres rows (never env vars), Hangfire + Service Bus for background work with code-defined sagas surviving deploys, SignalR for push, OpenTelemetry for observability, and Container Apps blue-green for deployment. The product's value-add is **caching, multi-tenant orchestration, the Standards engine, the UX, and the security posture** — not Graph proxying.

---

## 15. Document conventions

- Section numbers are stable; **append, do not renumber**. New material lands at the next index.
- Cross-references are by section number (e.g., "see §3" or "see §6 Polly pipeline").
- Diagrams are ASCII for source-control friendliness; an SVG export lives in `docs/architecture/` if a richer view is needed.
- This document is **non-normative for code**; `CLAUDE.md` is. If the two disagree, `CLAUDE.md` wins for code, and a `FEEDBACK.md` entry is required to reconcile.


