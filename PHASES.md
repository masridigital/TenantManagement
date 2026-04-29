# PHASES.md — Build Roadmap

> Read this **after** `README.md` (what), `ARCHITECTURE.md` (how it fits together), and `CLAUDE.md` (the standards). This is the plan: how we get from zero to feature parity, in what order, and how we know each chunk is done.
>
> Status: **draft** — phases land section by section. Each phase is its own commit so the plan can be reviewed and adjusted incrementally.

---

## 0. How to read this document

Each phase has the same five sub-sections:

1. **Scope.** What is in this phase — bullet-listed, no waffle. If it isn't listed, it isn't in this phase.
2. **Out of scope.** Things readers will assume are in this phase but aren't, and which phase owns them instead. Prevents scope creep and "but where does X go?" arguments.
3. **Entry criteria.** What must be true before the phase starts. Hard gates, not aspirations.
4. **Exit criteria.** What must be true to call the phase done. Each criterion is independently verifiable.
5. **Verification.** How we prove each exit criterion. Tests, demos, metrics, dashboards — concrete evidence.

### Rules across all phases

- **No phase ships behind a hidden feature flag** unless the flag is explicitly in scope. We don't accumulate dark code.
- **No phase imports work from a future phase.** If you find yourself touching Phase 7 code in Phase 3, stop and split the PR.
- **Every phase updates `MEMORY.md`** at the end. The next session reads it first.
- **Every phase that changes a coding standard updates `FEEDBACK.md`** and `CLAUDE.md`.
- **Coverage gates** apply to every phase touching `Domain` or `Application`: `>= 80%` line coverage on the touched code.
- **Performance budgets** apply to every phase that ships UI surface: p95 page render ≤ 500 ms over warm cache, p99 ≤ 1.5 s.

### What "done" means

A phase is done when **every** exit criterion is verified. Not "mostly done." Not "done except the tests." If a criterion is partial, the phase is still in flight and `MEMORY.md` says so. The cost of declaring a phase done while it isn't is paid by every subsequent phase that builds on it.

### Phase index

| # | Phase | Goal in one sentence | Approx. weeks |
| - | ----- | -------------------- | ------------- |
| 0 | **Foundations** | Solution skeleton, infra-as-code, CI/CD, observability, no business code | 2 |
| 1 | **Auth and tenancy** | OIDC sign-in, MSP context, customer-tenant authorization, refresh-token store | 3 |
| 2 | **Graph integration core** | `IGraphTenantClient`, Polly pipeline, batch handling, delta cursors, token cache | 3 |
| 3 | **Caching and projections** | L1/L2/L3, warmer scheduler, first projection (users), the read-path SLA | 3 |
| 4 | **Identity domain** | Users, groups, devices, sign-in logs, breach search; first end-to-end UI | 4 |
| 5 | **Tenants domain** | Onboarding saga, GDAP relationships, offboarding, alignment, all-tenants grids | 4 |
| 6 | **Standards engine** | Typed handlers, Report/Remediate/Alert modes, drift detection, templates store | 5 |
| 7 | **Endpoint Management + Exchange Online + Collaboration + Security** | Remaining customer-tenant domains | 8 |
| 8 | **Automation and integrations** | Scheduler UI, webhook receivers, PSA/RMM adapters | 4 |
| 9 | **Reports and analytics** | Domain analyser, license usage, inactive accounts, MFA, app consents | 3 |
| 10 | **BPA migration** | Legacy BPA support + one-way migration to Standards | 2 |
| 11 | **Hardening, scale, multi-region readiness** | Soak, perf, chaos, DR drills, multi-region story finalisation | 4 |
| 12 | **GA / launch** | Pricing, billing, public docs, status page, security disclosure programme | 3 |

Total: ≈ 48 engineering weeks elapsed (assumes 3-engineer team). Calendar runtime depends on team size; the **order** is non-negotiable because each phase's exit criteria are entry criteria for the next.

---

## Phase 0 — Foundations

**Goal:** A green CI on a deployable skeleton, with no business features, that we can put traffic on. Every subsequent phase relies on this being boring and reliable.

### Scope

- Solution layout matching `README.md` repository layout: `TenantManagement.{Domain,Application,Infrastructure,Graph,Standards,Api,Web,Worker,Migrations}` + tests projects.
- Coding-standard scaffolding: nullable enabled, implicit usings, `LangVersion latest`, `TreatWarningsAsErrors true`, `dotnet format` configured.
- Single `appsettings.{Development,Staging,Production}.json` with Key Vault references for every secret.
- ASP.NET Core 10 host with health endpoints (`/health/live`, `/health/ready`, `/health/startup`) returning the status framework only.
- EF Core 10 + Npgsql provider with an empty `AppDbContext` and a CI step that creates and tears down a Postgres database via Testcontainers.
- Redis client wired (`StackExchange.Redis`) with a smoke check at startup; no usage yet.
- Hangfire host wired with Redis storage; one heartbeat job to prove the dashboard works.
- Service Bus client wired with one health-check queue.
- OpenTelemetry SDK wired: traces, metrics, logs exported to App Insights (Azure) or OTLP collector (self-host); 100% sampling in dev/ci.
- Bicep modules for: Container Apps env, Postgres Flexible Server, Redis, Service Bus, Key Vault, Front Door, App Insights. Modules deploy to `dev`.
- GitHub Actions workflows: `pr.yml` (format check, build, unit tests, integration tests, security scan, OpenAPI diff), `deploy-dev.yml` (build container image, run migrator, deploy Web + Worker), `deploy-staging.yml` (manual approval gate).
- Container images: `Web`, `Worker`, `Migrator` build cleanly, all <300 MB compressed.
- One CODEOWNERS file. One pull-request template that points at `MEMORY.md`.
- `docs/architecture/`, `docs/ADRs/` directories created with a placeholder ADR template.

### Out of scope

- Authentication of any kind (Phase 1).
- Any reference to Microsoft Graph (Phase 2).
- Any business entity (Phase 4+).
- Multi-region (Phase 11).
- Self-host packaging (`docker-compose.yml` exists but is dev-only; the GA self-host artifact is Phase 12).

### Entry criteria

- Repository created, branch protections configured.
- Azure subscriptions for `dev` and `staging` available with a service principal for GitHub Actions deploys.
- The team has read `README.md`, `CLAUDE.md`, `ARCHITECTURE.md`.

### Exit criteria

1. `git clone && dotnet build` produces a green build with zero warnings.
2. `dotnet test` runs all (empty) test projects green.
3. `pr.yml` runs end-to-end on a trivial PR and is green.
4. `deploy-dev.yml` deploys Web + Worker to a Container Apps revision; both containers reach `/health/ready` = 200.
5. The Hangfire heartbeat job runs in `dev` and is visible in the dashboard.
6. App Insights shows traces and metrics from `dev` for synthetic requests.
7. A Bicep `what-if` against staging shows zero drift.
8. A new engineer can go from clean checkout to a running local stack (`docker compose up`) in < 15 min.
9. `MEMORY.md` updated; no `FEEDBACK.md` entries pending.

### Verification

- Live demo of points 1–6 from a clean checkout, on a recording for the project log.
- Onboarding-time stopwatch from a teammate not involved in scaffolding (point 8).
- A signed-off ADR-0001 ("Why .NET 10, Postgres, Redis, Service Bus, Hangfire, Container Apps") in `docs/ADRs/`.

### Risks

- **Container Apps cold-start regressions.** Mitigation: minimum-replica = 1 in dev/staging; production sets it from a measured warm-pool size in Phase 11.
- **Bicep drift.** Mitigation: every PR runs `bicep what-if` against staging; manual rejects on drift.
- **CI minutes blowout.** Mitigation: Testcontainers reuse + single shared Postgres image cache; budget 8 min per PR end-to-end.

---

## Phase 1 — Auth and tenancy

**Goal:** A user can sign in to the Blazor app with their MSP's Entra ID, the app resolves their `MspId`, and every endpoint enforces a typed authorization policy. Customer-tenant access is gated by `ICustomerTenantAuthorizationService`. Refresh tokens have a home that is **not** an environment variable.

### Scope

- Multi-tenant Entra app registration (terraform/Bicep + first-time consent flow documented).
- `Microsoft.Identity.Web` wired into both the API and the Blazor Web App (BFF posture; tokens never reach the browser).
- OIDC sign-in: cookie auth on the web side, JWT bearer on the API side, OBO exchange for inter-component calls.
- `MspContextMiddleware` resolving `tid → MspId` via `MspDirectoryLookup` (a Postgres table populated at MSP onboarding).
- `MspContextAccessor` (scoped DI service) populated once per request; injected into `AppDbContext`, MediatR pipeline, Hangfire job activator.
- Authorization policies for the four-role model (`Readonly`, `Editor`, `Admin`, `SuperAdmin`) plus per-feature policies (`Identity.User.ReadWrite`, etc.).
- `ICustomerTenantAuthorizationService.AssertAccessAsync(mspId, customerTenantId, ct)` with the GDAP relationship lookup. **No actual GDAP call yet** — Phase 2 plugs that in. For now, the relationship store is read from a manually-seeded table; the service interface is final.
- `IRefreshTokenStore` over `IDataProtectionProvider`-encrypted Postgres rows. DataProtection key in Key Vault. `RefreshTokens` table created with the audited access pattern.
- `MspDirectoryLookup` cache: L2 (Redis) keyed on `tid`, 1 h TTL; backed by Postgres truth.
- Audit-log table created (`observability.audit_log`) and the auth pipeline writes denial rows with `actor`, `policy`, `reason`, `traceId`. (Full command-audit lands Phase 2; this is just the auth side.)
- EF Core global query filter on every entity in `MspContextAccessor`-aware tables (zero entities yet — the convention and lint rule are what land).
- Lint rule (Roslyn analyzer) failing the build on `IgnoreQueryFilters` outside an allow-list.
- Sign-in / sign-out / consent UI wired into the Blazor shell.
- Smoke endpoint `GET /api/me` returning the authenticated principal + resolved `MspId` + roles + the assertion-result for a sample customer-tenant id.

### Out of scope

- Any Graph call (Phase 2).
- The actual GDAP relationship sync from Partner Center (Phase 5).
- The MSP-onboarding-flow UI (Phase 5; for Phase 1 we seed MSPs via SuperAdmin API or SQL).
- PIM / JIT activation (Phase 4).
- Custom roles with per-tenant scoping (Phase 5; the policies for this exist but the data is fixed).

### Entry criteria

- Phase 0 exit criteria all green.
- Multi-tenant app registration created in our platform Entra tenant.
- A test MSP tenant with at least two users (one Editor-equivalent, one Admin-equivalent) provisioned.

### Exit criteria

1. A user from the test MSP tenant can sign in to the Blazor app and see `/api/me` resolved with the correct `MspId` and roles.
2. A user from a non-onboarded tenant is denied at the `MspDirectoryLookup` step and lands on the "tenant not provisioned" page.
3. `ICustomerTenantAuthorizationService` returns `Granted` for the seeded relationship and `Denied` for everything else; both outcomes audit-log a row.
4. `IRefreshTokenStore` round-trips a token through encrypt → store → retrieve → decrypt with the production DataProtection chain.
5. The `IgnoreQueryFilters` lint rule fires on a deliberately bad PR (proven in CI).
6. All endpoints (currently just `/api/me`, `/health/*`, `/api/_diagnostics/*`) have an explicit `.RequireAuthorization(...)` or are listed in an `AllowAnonymous` allow-list reviewed at PR time.
7. Refresh-token-in-env-var grep returns zero hits across the repo (CI step).
8. Cookie posture verified: no Graph token in the browser; auth cookie is `HttpOnly`, `Secure`, `SameSite=Lax`, encrypted server-side.
9. Coverage `>= 80%` on the new `Identity` (platform side) and `PlatformAdmin` modules touched in this phase.
10. `MEMORY.md` updated, `FEEDBACK.md` reviewed for any standard changes.

### Verification

- Two recorded sign-in demos: one happy-path, one denied.
- Penetration smoke: a user from MSP-A receives a token; `/api/me?customerTenantId={mspBSeededTenant}` returns 403 with audit log row.
- DataProtection rotation rehearsal: rotate the master key in Key Vault, restart, prove existing rows are still decryptable and new rows use the new key.
- A purposeful PR that adds `IgnoreQueryFilters` outside the allow-list fails CI; reverting it goes green.

### Risks

- **Cookie-vs-bearer confusion** when the Blazor server calls the API. Mitigation: components call MediatR via DI in-process; `/api/...` is for external integrators and the rare WebAssembly island.
- **DataProtection key handling.** Mitigation: keys live in Key Vault, never on disk; ADR-0002 documents the rotation contract.
- **OIDC consent UX.** First sign-in for an MSP requires admin consent. Mitigation: a dedicated "first-time setup" page that walks the admin through, with an explicit error path for "user is not a tenant admin."

---

## Phase 2 — Graph integration core

**Goal:** A typed `IGraphTenantClient` that any application code can inject, call any Graph endpoint through, and trust to be throttled, retried, batched, audited, and tenant-scoped — without that code knowing how any of those work. The integration leans on `Microsoft.Graph` v5 and `Microsoft.Identity.Web` for everything those libraries do; we own the factory and the resilience pipeline, **not** the SDK.

### Scope

- `IGraphTenantClientFactory` constructing per-`(MspId, CustomerTenantId)` instances of `IGraphTenantClient`.
- `IGraphTokenProvider` with:
  - OBO path (`AcquireTokenOnBehalfOfAsync`) used when `MspContextAccessor` carries a user principal.
  - Client-credentials path (cert from Key Vault) used in worker contexts (no user).
  - Distributed token cache (Redis-backed `IDistributedCache`) keyed by `(MspId, CustomerTenantId, Scope, [UserObjectId])`.
  - `SemaphoreSlim` coalescing on cache miss so N concurrent acquisitions become 1 STS call.
- Polly v8 pipeline registered on the SDK's `HttpClient` (`DelegatingHandler`):
  - Bulkhead per `MspId` (default 64 concurrent in-flight).
  - Per-`(MspId, CustomerTenantId)` token-bucket rate limiter (Redis-backed).
  - Retry on 429 honouring `Retry-After` (cap 60 s, max 5 attempts).
  - Retry on transient 5xx with jittered exponential backoff.
  - Per-attempt 30 s timeout.
  - Circuit breaker per `(MspId, CustomerTenantId)`.
- `IGraphTenantClient.SendBatchAsync(BatchRequestContent, ct)` that succeeds **only if every subresponse is 2xx**; un-acked subrequests requeue into a follow-up batch with their inner `Retry-After` honoured.
- `GraphAuditingHandler` middleware recording every Graph call (endpoint, method, status, retry count, latency) into `observability.audit_log` with `actor` resolved from the calling context.
- `GraphDeltaCursors` table created with `(MspId, CustomerTenantId, ResourceType, DeltaLink, LastSyncedAt)` and a typed `IGraphDeltaCursorStore` over it.
- A reference call site exercising the SDK: `Worker.Diagnostics.PingTenantAsync(mspId, customerTenantId)` reads `organization` and writes a single audit row. Used in the readiness probe and in dev for end-to-end verification.
- GDAP relationship synchronisation against Partner Center, populating `MspCustomerTenantRelationship` (the table seeded in Phase 1). This is what plugs the real data into `ICustomerTenantAuthorizationService`.
- Adding `customer.tenant.id`, `graph.endpoint`, `http.status_code`, `retry.count` to OpenTelemetry spans on every Graph call.

### Out of scope

- Any L1/L2/L3 caching of Graph **responses** (Phase 3 — this phase is the SDK + token + pipeline layer; data caching is the next layer up).
- Any end-user UI consuming Graph (Phase 4+).
- The Exchange / legacy-REST shims (Phase 7 — they'll plug in via the same Polly pipeline).
- Standards run logic (Phase 6).
- The "all customer tenants" cross-tenant grids (Phase 5).

### Entry criteria

- Phase 1 exit criteria all green.
- A test customer M365 tenant with a GDAP relationship to the test MSP.
- The platform's confidential-client cert provisioned in Key Vault and consented by the test MSP.

### Exit criteria

1. `Worker.Diagnostics.PingTenantAsync` succeeds against the test customer tenant from a worker pod, in a Hangfire-triggered job, and writes one audit row.
2. The same call from the API context (under an MSP user's OBO token) also succeeds and audits with `actor = <user>`.
3. A deliberately throttled tenant (forced 429 via a test stub) shows: retries up to 5, `Retry-After` honoured, telemetry tags set, no exception leaking past the Polly pipeline within budget.
4. A batch of 10 mixed sub-requests with 2 forced 429s shows: outer 200, inner 429s captured, the two failing sub-requests requeued, eventual full success, audit row reflects the actual outcome.
5. The token cache is verified: 100 concurrent calls for the same `(MspId, CustomerTenantId)` produce exactly **one** STS call (metric `auth_token_fetch_total`).
6. `GraphDeltaCursors` round-trips a delta link for the `users` resource against the test tenant: first call full, second call returns empty changeset.
7. `MspCustomerTenantRelationship` is populated by a Hangfire job from Partner Center; `ICustomerTenantAuthorizationService` now denies for relationships not present and grants for those that are; both outcomes audit.
8. The `IGraphTenantClient` factory **refuses** to construct an instance for a `(MspId, CustomerTenantId)` not in `MspCustomerTenantRelationship`; this is the last line of defense before Graph.
9. Coverage `>= 80%` on `TenantManagement.Graph` and the touched parts of `Infrastructure`.
10. `MEMORY.md` updated.

### Verification

- Recorded chaos run: introduce 30 s of forced 429s mid-call from a stub, observe retries and final success in the audit log.
- Token cache stampede test: 1,000 parallel `Worker.Diagnostics.PingTenantAsync` calls; assert exactly one STS round-trip.
- Cross-tenant safety test: a fabricated request to construct `IGraphTenantClient(mspId=A, customerTenantId=Bs)` (B's tenant under A's MSP context, where the relationship doesn't exist) throws `CustomerTenantAccessDenied` before any token is acquired.
- ADR-0003 records the choice to put the resilience pipeline on the SDK's `HttpClient` rather than wrapping individual SDK methods, with the rationale that the SDK is the abstraction and we don't re-abstract.

### Risks

- **Partner Center API quirks** (lower throughput, eventual consistency on relationship state). Mitigation: synchronisation is a Hangfire recurring job, not a request-time check; freshness window 15 min; relationships in flux are explicitly tracked.
- **Token cache cold-start** under deploy. Mitigation: cache key version segment (§3 of `ARCHITECTURE.md`) so stale entries from previous deploys don't poison; warm-up calls on a small set of tenants in the readiness probe.
- **Polly pipeline mis-tuning.** Mitigation: rate-limit + bulkhead defaults are conservative; tune in Phase 11 from real telemetry; never tune by gut feel in dev.

---

## Phase 3 — Caching and projections

**Goal:** Prove the four-layer read path on **one resource** (users) end-to-end. Once this works, every subsequent domain just plugs in. This is the phase that lifts the rebuild's core value off the slide deck and into running code: a request thread that **never** paginates Graph.

### Scope

- L1: `IMemoryCache` registered with size-limit and a per-entry size estimator; default 5 min TTL; key prefix `l1:`.
- L2: Redis distributed cache with `IDistributedCache` adapter; `IGraphCacheKey` factory enforcing the key conventions from `ARCHITECTURE.md` §3 including the `schema:v{rev}` segment.
- L2 stampede protection: `SemaphoreSlim` per cache key for read-through population.
- L3: `identity.users_projection` typed Postgres table with the projected user shape (object id, UPN, display name, mail, account enabled, manager id, license assignments by SKU id, last sign-in, source delta cursor id) plus the standard multi-tenant columns (`MspId`, `CustomerTenantId`, audit columns, `RowVersion`).
- L3: `users_sync_state` row per `(MspId, CustomerTenantId)` carrying `last_full_sync_at`, `last_delta_sync_at`, `delta_cursor_state`, `last_error`.
- The warmer job:
  - `WarmUserListJob(MspId, CustomerTenantId)` fire-and-forget Hangfire job.
  - First run: full page-iterated read using `PageIterator<User>`, project rows, set the cursor.
  - Subsequent runs: delta read using `users.Delta()`, upsert/delete projection rows, update cursor.
  - Idempotent: re-running is a no-op on unchanged data.
- The orchestrator:
  - Recurring Hangfire job `WarmMspUsersJob(MspId)` every 15 min that enumerates active customer tenants and enqueues per-tenant warm jobs subject to the per-MSP bulkhead.
- The resolver:
  - `IUserListResolver.GetAsync(mspId, customerTenantId, query, ct)` walks L1 → L2 → L3 → cold-fallback (one bounded Graph page + warmer kick) per `ARCHITECTURE.md` §3 step list.
  - Returns the data with `last_synced_at` so the UI can render the "data refreshed N minutes ago" footer.
- Write-path invalidation hooks: `IInvalidationPort.InvalidateUserList(mspId, customerTenantId)` and `InvalidateUser(mspId, customerTenantId, userObjectId)` callable from any future write handler.
- Metrics: `cache_hit_total{layer,resource}`, `cache_miss_total`, `delta_sync_duration_ms`, `delta_failure_total`, `warmer_queue_depth`.
- A purpose-built **load test** scenario: 50 simulated MSPs × 20 customer tenants each × a steady "list users" request load, asserting the read path stays under the latency budget without a single request thread paginating Graph.
- A diagnostics endpoint `GET /api/_diagnostics/cache/{mspId}/{customerTenantId}/users` showing the L1/L2/L3 hit-or-miss decision and the staleness for the last N requests, gated to SuperAdmin.

### Out of scope

- Any other resource type (groups, devices, etc.) — those land in Phase 4 and 7. **Users** is the reference implementation; subsequent resources copy the pattern.
- The user-facing UI (Phase 4 — this phase ships only the API and the warmer).
- Write paths (Phase 4 — this phase ships only reads).
- Projections for resources that don't support delta (Phase 7).

### Entry criteria

- Phase 2 exit criteria all green.
- A test customer tenant with > 1,000 user records (so pagination and delta are exercised) and a script to mutate users on it for delta verification.

### Exit criteria

1. **Read path SLO**: under the load-test scenario above, p95 of `GET /api/identity/users?customerTenantId={id}` is ≤ 250 ms and p99 ≤ 600 ms over warm cache. Zero requests trigger a `nextLink` walk on the request thread (verified by absence of `cache_miss_total{layer="l3"}` plus `graph_request_total{caller="resolver"}` events for non-cold tenants).
2. **Cold-tenant first read** (newly seeded `(MspId, CustomerTenantId)` with empty L3) returns ≤ 1 s, populates one Graph page synchronously, and enqueues the warmer; the second read from L3 within 10 s returns the full set.
3. **Stale-but-not-cold**: forcing `last_synced_at` to 1 hour past the freshness window returns the stale data immediately, kicks the warmer, and the next read returns fresh data within the warmer's budget.
4. **Delta correctness**: mutate 10 users on the test tenant, wait for the warmer cycle, observe the projection has applied exactly those changes (no stragglers, no extras).
5. **Cursor reset**: forcibly invalidate the delta cursor, confirm the warmer falls back to full sync, the metric `delta_failure_total{reason="cursor_expired"}` increments, and the projection is reconciled.
6. **Stampede protection**: 1,000 parallel cache misses for the same key produce exactly one underlying L3/Graph fetch.
7. **Schema rev invalidation**: bumping the schema rev (a deploy-time constant) effectively expires all L1/L2 entries; verified by hit-rate dropping and immediately recovering as warm runs.
8. **No request-time pagination test**: a CI integration test asserts `IGraphTenantClient` records zero Graph calls during 1,000 read requests against a warm tenant.
9. Coverage `>= 80%` on the new resolver, warmer, and projection code.
10. `MEMORY.md` updated; ADR-0004 records the cache hierarchy and freshness defaults.

### Verification

- The load test in point 1, run in CI nightly with results posted to the project log.
- A recorded session showing the delta correctness check (point 4) end-to-end.
- A negative test PR removing the `SemaphoreSlim` coalescing fails the stampede assertion.
- A negative test PR that adds a `nextLink` walk on the request thread fails the assertion in point 8.

### Risks

- **Delta semantics edge cases.** Users that are deleted-then-recreated, or where Graph returns a row with only the `id` and the change reason, can confuse the projection. Mitigation: `IUserDeltaApplier` has an explicit unit-test matrix for every `@removed` shape Graph emits, and a fall-through to full-sync if a row is unparseable.
- **Projection drift.** A bug in the upsert can let Graph and projection diverge silently. Mitigation: a low-frequency reconciler job that does a small random-sample full read against Graph and asserts equivalence; alarm if drift > 0.5%.
- **Hot-key Redis pressure.** A few very large MSPs can put 80% of read traffic on a few keys. Mitigation: L1 absorbs the hot read; L2 keys carry compressed payloads; the per-MSP bulkhead caps ingest.

---

## Phase 4 — Identity domain (first end-to-end UI)

**Goal:** First real user-facing surface. An MSP user signs in, picks a customer tenant, sees a server-paginated grid of users with sub-second renders from cache, and can do CRUD + bulk operations that round-trip Graph through the full write path with audit, projection update, and SignalR push. By the end of this phase, the app is a credible drop-in for CIPP's user management — but faster, typed, and correctly multi-tenant.

### Scope

#### Read surface

- Users grid with server-side pagination, filtering, sorting, column selection, persisted view per MSP user.
- User detail page: profile, manager, group memberships, license assignments, sign-in logs (lazy-loaded), risky-user state.
- Groups grid + detail (members, owners, dynamic membership rule view).
- Devices (Entra-registered) grid + detail.
- Sign-in logs query page: filter by date / user / status / IP / location; results streamed via cursor pagination from Graph (this is one of the few **on-demand** queries that doesn't go through L3 — sign-in logs are queryable, not projected).
- Risky users grid + detail; "Dismiss" command.
- Breach search (HIBP integration) — two surfaces: per-account check, per-tenant scan with results table.
- All grids respect the four-role policy model from Phase 1 (`Identity.User.Read`, `Identity.User.ReadWrite`, etc.).
- All grids show the "data refreshed N minutes ago" footer with a "refresh now" affordance that enqueues a warmer kick (rate-limited per-tenant per-resource to once per 30 s).

#### Projections (additions to Phase 3's pattern)

- `identity.groups_projection` + `identity.group_members_projection` (typed join table) + delta cursor.
- `identity.devices_projection` + delta cursor.
- All driven by the same warmer / resolver pattern; no new infrastructure.

#### Write surface

- Create user (single + bulk CSV).
- Edit user (typed field set, with FluentValidation; multi-step UI but single transaction).
- Disable / enable user, reset password, revoke sessions.
- Edit aliases.
- Set user photo.
- Per-user MFA reset / set auth method.
- Add to / remove from group; bulk membership change with batch + SignalR progress.
- Assign / unassign licenses (single + bulk).
- Hide from GAL.
- Restore deleted user.
- Dismiss risky user.
- Create temporary access pass (TAP).
- Every write goes through the five-step write path from `ARCHITECTURE.md` §4 (authorize → write-ahead audit → Graph → projection update + audit ack in same txn → invalidate + SignalR notify).

#### UI infrastructure

- Blazor Web App shell: top-bar tenant picker (with type-ahead, recent tenants), left-nav by domain, breadcrumbs.
- MudBlazor table component wrapper with the standard "skeleton on first paint + data on warm" pattern.
- SignalR client wired into the shell; toast + grid-row updates for write events.
- A `/customer-tenant/{id}/identity/...` URL scheme that survives refresh and is shareable within an MSP.
- An "in-flight operations" tray showing bulk progress.

### Out of scope

- App registrations / SPN management (Phase 5 — those are tenant-administration concepts more than identity).
- GDAP role mapping UI (Phase 5).
- Just-in-time admin elevation (Phase 5).
- Custom-role authoring with per-tenant scopes (Phase 5).
- Conditional Access policies (Phase 7 under Security).
- Intune-managed devices (Phase 7).

### Entry criteria

- Phase 3 exit criteria all green.
- The test customer tenant has a representative user / group / device fleet (≥ 500 users, ≥ 50 groups, ≥ 200 devices) so grids and bulks are realistic.
- Design tokens and the MudBlazor theme finalised so this phase doesn't bleed into per-page styling churn.

### Exit criteria

1. An MSP Editor signs in, navigates to the test customer tenant, sees the users grid render in ≤ 500 ms p95 over warm cache, paginates / filters / sorts without re-rendering the grid frame.
2. Creating a user succeeds end-to-end: Graph call observed, audit row written, projection row appears, SignalR pushes the new row to the open grid, and the grid updates without a page refresh.
3. Bulk-add 200 users via CSV: batches of 20, audit row per bulk, per-row outcome streamed via SignalR, partial failures surfaced inline, no Graph throttle errors leak past the Polly pipeline.
4. Disable user → projection updated → grid badge updates live.
5. Sign-in logs page returns 50 results in ≤ 1.5 s p95 (this is the on-demand path, not L3).
6. A user from MSP-A cannot URL-tamper to view MSP-B's grid (verified by pen-test sweep of the Identity routes).
7. Coverage `>= 80%` on `Application/Identity/*` and `Domain/Identity/*`.
8. bUnit tests cover every grid component's render-from-loading and render-from-data paths.
9. Playwright smoke tests cover: sign-in → list users → edit user → bulk add → bulk license assign → revoke sessions.
10. Performance budgets met across the Identity surface: p95 page render ≤ 500 ms (warm), p99 ≤ 1.5 s.
11. `MEMORY.md` updated.

### Verification

- A 30-minute "shadow CIPP" demo where the same MSP-day workflow is performed in CIPP and in the rebuild, side-by-side, with timings.
- A pen-test sweep of all identity routes against horizontal access (MSP-A → MSP-B) and vertical access (Readonly → Editor commands).
- Nightly Playwright run against `staging` with traces uploaded.

### Risks

- **Bulk operations are the highest blast-radius surface.** Mitigation: bulk-add and bulk-license assign require explicit confirm + ticket-id-or-justification field captured into audit.
- **CSV ingestion shape variability.** Mitigation: schema validation up front; preview UI shows what will happen before submit.
- **Sign-in log queries against tenants with very high signal volume time out.** Mitigation: query is bounded by date range with sane defaults; cursor-based; surface the cursor in the URL so users can resume.

---

## Phase 5 — Tenants domain

**Goal:** Onboard / offboard customer tenants as a first-class flow, keep the tenant catalogue accurate, and surface the cross-tenant grids (alignment, drift, BPA, compliance, domain health) that are the most-used part of CIPP. This is the phase that makes the platform **multi-customer-tenant in earnest** rather than a single-tenant proof-of-concept.

### Scope

#### Onboarding saga

- A code-defined saga (`TenantOnboardingSaga`) persisted to `graph.sagas`, with steps:
  1. Validate inputs (target tenant id, GDAP invite role set, MSP user initiating).
  2. Create GDAP invite via Partner Center with the requested role set.
  3. Wait for invite acceptance (saga sleeps; resumed by a webhook receiver or a 1-min poll).
  4. Map invited GDAP roles to our internal roles per the MSP's role-mapping table.
  5. SAM bootstrap: register the platform's confidential-client app in the customer tenant if not already trusted; verify cert-based auth round-trip.
  6. Initial warmer kick across the standard resource set (tenants → users → groups → devices → CA policies).
  7. Run baseline Standards in **Report-only** mode; persist baseline.
  8. Notify MSP via UI + email; mark customer tenant `Active`.
- Failure paths at each step have explicit compensation:
  - GDAP invite stuck → notify, surface re-send affordance, do **not** auto-retry (avoid invite spam).
  - Cert bootstrap failure → flag as `Onboarding-Blocked`, link to remediation playbook.
  - Initial warm failure → tenant stays `Onboarding-Warming` and the warmer retries; UI surfaces the partial state.

#### Offboarding saga

- Reverse flow: revoke GDAP, delete projection rows for that customer tenant under the MSP, archive audit log shards, retain the audit log itself per retention policy, mark `Offboarded` (soft-deleted with retention).
- Hard-delete is a separate flow gated by SuperAdmin + a ticket id, with a 30-day cooling-off period.

#### Tenant catalogue + GDAP relationship sync

- `tenants.customer_tenants` — one row per customer tenant the MSP has any relationship with.
- `tenants.gdap_relationships` — refreshed from Partner Center every 15 min via Hangfire.
- `tenants.gdap_role_mappings` — MSP-defined map from Partner Center GDAP roles to our internal four-role policy plus per-feature scopes.
- A customer-tenant detail page summarising: status, GDAP roles, last warmer run per resource, drift score (Phase 6 plugs in), open incidents (Phase 7 plugs in).

#### Cross-tenant grids

- All-tenants list (the home page after sign-in).
- All-tenants alignment: per-tenant pass/fail per Standard (Phase 6 fills in the data; Phase 5 ships the grid frame).
- All-tenants drift: per-tenant deviation count and last-seen drift event (Phase 6 fills in).
- All-tenants BPA: per-tenant pass/fail per BPA rule (Phase 10 fills in via the BPA → Standards bridge).
- All-tenants compliance: per-tenant secure score, MFA coverage, conditional-access posture summary (Phase 7 fills in).
- All-tenants domain health: per-tenant DNS / DKIM / SPF / DMARC / MX state.
- Each grid: server-side pagination, MSP-scoped, indexed for sort/filter on the columns that matter.

#### Custom roles (per-tenant scoping)

- The "custom role" concept introduced in Phase 1 (interface only) is now functional: an MSP can author a custom role with a per-customer-tenant scope ("Editor on tenants A, B, C; Readonly on D"), persisted in `platform_admin.custom_roles`, and the policy evaluator checks both the policy claim and the per-tenant scope.

#### JIT admin elevation

- An MSP Admin can request just-in-time elevation to a higher-privileged GDAP role for a bounded window (default 1 h, max 8 h) with a justification captured into audit.
- Elevation actually re-issues a Graph token under the elevated GDAP role; expiry of the window auto-revokes.

#### Tenant-level settings

- Per-MSP defaults that apply across all customer tenants (alert recipients, notification channels, refresh windows).
- Per-customer-tenant overrides where they make sense (refresh window for a specific tenant; standards-applicable list).

### Out of scope

- The Standards engine itself (Phase 6) — Phase 5 ships the grid frames and the slots; the data lands in Phase 6.
- Endpoint Management / Exchange / Collaboration / Security domains (Phase 7).
- BPA grid data (Phase 10).
- PSA integration of onboarding events (Phase 8).

### Entry criteria

- Phase 4 exit criteria all green.
- A second test customer tenant available so multi-tenant grids actually have ≥ 2 rows.
- Partner Center sandbox accepting GDAP invites end-to-end.

### Exit criteria

1. Onboarding a fresh customer tenant from scratch via the UI completes in ≤ 10 min wall-clock (most of which is human GDAP-acceptance time, not our processing).
2. The onboarding saga survives a worker restart mid-flight without losing state; saga row is queryable; resume happens automatically.
3. All-tenants list page renders in ≤ 800 ms p95 over warm cache for an MSP with 200 customer tenants (synthetic load for verification).
4. Offboarding a customer tenant completes, projection rows are gone, audit log persists, tenant is `Offboarded` and not visible to default queries (without breaking historical audit reads).
5. Custom role with per-tenant scope: a user assigned to it cannot view tenants outside the scope (verified by the standard pen-test sweep).
6. JIT elevation: a user requests, gets the elevated token, performs an action, the action audits with `actor.elevation = jit-{requestId}`, the elevation expires automatically.
7. GDAP relationship sync runs on schedule and reflects an out-of-band relationship change (manual delete in Partner Center) in ≤ 20 min.
8. Coverage `>= 80%` on `Application/Tenants/*` and `Domain/Tenants/*`.
9. The home dashboard ships with the all-tenants grids visible and clickable through to per-tenant detail.
10. `MEMORY.md` updated.

### Verification

- An end-to-end onboarding demo recorded against a fresh sandbox tenant.
- A worker-kill chaos test mid-saga, restart, verify resumption.
- Pen-test sweep extended to custom-role per-tenant scoping.
- Performance test: 200-tenant grid render with realistic per-tenant payloads.

### Risks

- **Partner Center latency / inconsistency.** Mitigation: saga waits with backoff; surface the wait state explicitly to the user instead of pretending to be in-flight.
- **Custom-role policy combinatorics.** Mitigation: the policy evaluator is a small explicit decision tree, not an expression engine; tested with a matrix of claim × scope × resource access cases.
- **JIT elevation as a privilege-escalation path.** Mitigation: every elevation requires a justification, is rate-limited per MSP user, and triggers an immediate audit notification to the MSP's SuperAdmin.

---

## Phase 6 — Standards engine

**Goal:** A typed, registry-driven Standards engine that replaces CIPP's 187 bespoke PowerShell standard files with type-checked C# handlers, where every standard is independently testable, every handler implements three modes (Report / Remediate / Alert), drift is detected via a JSON-Patch diff against a stored baseline, and templates live in Postgres `jsonb` (not 64 KB-capped Azure Tables). This phase is the single biggest functional differentiator between the rebuild and CIPP.

### Scope

#### Engine

- `IStandardHandler` interface with `ReportAsync(StandardContext)`, `RemediateAsync(StandardContext)`, `AlertAsync(StandardContext)`.
- `[Standard("AntiPhishingPolicy", Category = StandardCategory.Email)]` attribute + assembly-scanning registration into `IStandardRegistry`.
- `StandardContext`: `(MspId, CustomerTenantId, IGraphTenantClient, settings: T, mode, traceId, ct)`.
- Per-standard typed settings: `record AntiPhishingPolicySettings(bool EnableImpersonationProtection, ...)` deserialised from the template payload.
- Mode invariant: `RemediateAsync` cannot run unless a `ReportAsync` for the same `(MspId, CustomerTenantId, StandardId, runId)` is on file with `outcome != "skipped-due-to-error"`. Enforced in the orchestrator, not in each handler.
- Idempotency: Remediate observes the post-Report state and applies only the necessary delta; re-running is a no-op.

#### Drift

- After `ReportAsync` runs, the typed result is canonicalised and diffed against `standards.drift_baselines` (per `(MspId, CustomerTenantId, StandardId)`).
- Diff format: JSON Patch (RFC 6902).
- Drift events surface in:
  - The all-tenants drift grid (Phase 5 frame).
  - The per-tenant standards page.
  - SignalR (`/hubs/standards`) so open dashboards update live.
- Baselines are blob-stored when large; the Postgres row carries metadata + blob ref.

#### Templates

- `templates.standards_templates` typed table with `(MspId, TemplateId, StandardId, Settings jsonb, Version, IsDeleted, RowVersion)`.
- A template is a named bundle of per-standard settings. MSPs apply a template to a tenant or a tenant group.
- Versioning: editing a template creates a new version; old versions stay so audit history can resolve "what version was applied when".
- Import path: a CIPP standards template export can be parsed and converted to the new shape (best-effort; conversion notes shown in the UI).
- Other template kinds in scope this phase: CA templates, group templates, JIT admin templates. Intune / spam / connection / safe-links templates roll into Phase 7 with their respective domains.

#### Orchestration

- `RunStandardsCommand(MspId, ScopeFilter, Mode, TemplateId)` — fans out via Service Bus to per-tenant `RunStandardOnTenantCommand` subject to the per-MSP bulkhead.
- Saga-tracked for any run > 100 tenants (i.e., an MSP-wide standards run is a saga).
- Progress published via SignalR per-MSP and per-tenant groups.
- A run row in `standards.standard_runs` aggregates per-tenant outcomes; an audit row records the initiating user.

#### UI

- All-tenants alignment grid (data plug-in to the Phase 5 frame).
- Per-tenant Standards page: list of applicable standards, each with mode (Report / Remediate / Alert), last result, drift status, "run now" affordance.
- Per-standard detail page: typed settings editor (a generated form bound to the settings record), per-tenant override view, run history.
- Template editor: list / create / edit / version-diff / apply-to-scope.
- A "what would this do?" preview that runs the standard in **dry-run** mode (Report against current state, simulate Remediate without writing) and shows the planned diff.

#### Migration tooling

- A script in `TenantManagement.Migrations` that reads a directory of CIPP `Invoke-CIPPStandard*.ps1` files, parses metadata (name, category, default settings), and produces a stub C# handler skeleton with TODOs for the actual Graph calls. **Does not execute PowerShell.** It's a code-generation aid for clean-room reimplementation.

### Out of scope

- Domain-specific standards whose content lives in Phase 7 (e.g., Defender posture, Intune compliance specifics) — those handlers can be authored in Phase 7. Phase 6 ships the engine plus a representative ≥ 30 standards covering the most-used CIPP categories (Identity, Email, Tenant, AAD).
- BPA migration into Standards (Phase 10).
- Scheduler UI (Phase 8) — for now, runs are kicked off ad-hoc or on a default per-MSP cron.

### Entry criteria

- Phase 5 exit criteria all green.
- A representative customer tenant with a "messy" baseline (some non-default settings, some legacy policies) so drift detection has signal.

### Exit criteria

1. ≥ 30 standards implemented in C# across Identity / Email / Tenant / AAD categories, each with all three modes.
2. Each handler has unit tests covering: report against a stub Graph state, remediate against a delta, idempotency on re-run.
3. Mode invariant verified: a remediate-without-report attempt fails fast with a typed error (proven by a negative test).
4. Drift baselines round-trip a JSON Patch correctly; a deliberately-introduced setting change is detected and surfaced in the drift grid in ≤ 1 warmer cycle.
5. Run a template against 50 tenants, observe per-MSP bulkhead caps the concurrency, every per-tenant outcome lands in the run row, no Graph throttle errors leak past the pipeline.
6. Mid-run worker restart resumes the saga; outcomes are not duplicated; idempotency keys hold.
7. Template import from a real CIPP standards export produces a structurally valid new template (with conversion notes for any settings the new shape doesn't yet support).
8. The dry-run preview produces a diff identical to the actual remediate would produce, against an unchanged tenant.
9. Coverage `>= 80%` on `Application/Standards/*`, `Domain/Standards/*`, and the per-handler standards code.
10. `MEMORY.md` updated; ADR-0005 records the engine design and the JSON-Patch baseline format.

### Verification

- A side-by-side recorded comparison of running 5 representative standards in CIPP and in the rebuild against the same tenant; outcomes equivalent, runtime in the rebuild ≤ 1/3 of CIPP's.
- A drift-detection demo: change a setting out-of-band, observe the next run flag it, the all-tenants grid update, the SignalR push.
- A negative test PR that authors a handler missing one of the three modes fails the registry-validation step at startup (or a CI test).

### Risks

- **Per-handler scope creep.** A handler that "just one little time" reads or writes outside its declared scope is a recipe for drift between Report and Remediate. Mitigation: each handler declares its read/write scope in metadata; the engine validates at runtime; CI test fails on undeclared calls.
- **Template-shape evolution.** A schema change to a settings record breaks existing templates. Mitigation: versioned settings records with explicit migration functions; the template version pins the schema version; no in-place edits to a published settings shape.
- **Drift noise.** Microsoft pushes settings shapes around (new properties, defaults change). Mitigation: a per-property "ignore if unset" flag in the standard's metadata; baselines store both the recorded shape and the schema version.

---

## Phase 7 — Endpoint Management + Exchange Online + Collaboration + Security

**Goal:** Bring the four large customer-tenant domains to feature parity with CIPP. This is the longest phase by raw line count but the lowest architectural risk: every domain is the same pattern (resolver + projection + warmer + write handlers) with domain-specific Graph calls and standards. Splitting these four into separate sub-phases is reasonable; sequencing them inside one phase keeps the cross-domain features (e.g., per-tenant compliance dashboard) coherent.

### Scope

#### EndpointManagement (Intune)

- Projections: `endpoint_management.managed_devices`, `endpoint_management.intune_policies`, `endpoint_management.applications`, `endpoint_management.assignment_filters`, `endpoint_management.compliance_policies`. Delta-driven where supported; full-refresh-on-schedule otherwise.
- Read surface: managed devices grid, device detail, policies grid, applications grid, assignment filters grid, compliance policies grid, autopilot enrolment view.
- Write surface: deploy / edit / delete policies, deploy / remove apps (Office, Win32, store, Choco repository), assign / unassign apps and policies, autopilot config CRUD, autopilot device CRUD, intune script CRUD, reusable settings CRUD, compliance policy CRUD, defender deployment.
- Sensitivity-flagged operations: BitLocker recovery key read, LAPS local admin password read. Both require step-up auth (re-MFA) at the action moment, JIT-allowable, audited as `category = "secret-recovery"`, surfaced in a separate audit lane.
- Standards specific to this domain: device compliance baseline, autopilot baseline, BitLocker / LAPS posture, etc. (≥ 25 standards).

#### ExchangeOnline

- Projections: `exchange_online.mailboxes`, `exchange_online.transport_rules`, `exchange_online.connectors`, `exchange_online.anti_spam_filters`, `exchange_online.anti_phishing_filters`, `exchange_online.malware_filters`, `exchange_online.safe_links`, `exchange_online.safe_attachments`, `exchange_online.quarantine_policies`. Mailbox metadata only; **no message bodies stored**.
- Read surface: mailboxes grid, mailbox detail (CAS, mobile devices, permissions, rules, OOO, calendar permissions, contact permissions), transport rules grid, connectors grid, the four filter grids, safe-links / safe-attachments grids, quarantine, message trace, mailbox restore tracker.
- Write surface: mailbox conversion (regular ↔ shared ↔ room ↔ equipment), permissions edit, rules edit, OOO edit, transport rule add / edit / remove, connector add / edit / remove, filter policies add / edit / remove, safe-links / safe-attachments add / edit / remove, quarantine policy add / edit / remove, mailbox restore initiate, retention hold / litigation hold set, archive enable, auto-expanding archive enable, calendar processing set, mailbox quota / locale / email-size set.
- The Exchange shim (`TenantManagement.Graph.ExchangeShim`) hosts the in-process PowerShell runspace pool for the operations Graph still doesn't cover, with strict timeouts and the same Polly + audit pipeline.
- Standards specific: anti-spam baseline, anti-phishing baseline, malware baseline, safe-links baseline, transport rule hardening, etc. (≥ 30 standards).

#### Collaboration (Teams / SharePoint / OneDrive)

- Projections: `collaboration.sites`, `collaboration.teams`, `collaboration.teams_voice` (feature-flagged), `collaboration.teams_activity`. SharePoint admin URLs cached.
- Read surface: sites grid, site detail (members, sharing, quota), Teams grid, Teams voice (LIS locations, voice numbers), OneDrive provisioning view, sharing settings.
- Write surface: site add (single + bulk), site delete, sharepoint settings edit, sharepoint member edit, sharepoint permissions edit, Teams add, group → Team conversion, Teams voice number assign / remove, OneDrive provision, OneDrive shortcut deploy.
- Standards specific: external sharing posture, Teams meeting policy baseline, etc. (≥ 15 standards).

#### Security

- Projections: `security.alerts`, `security.incidents`, `security.defender_state`, `security.defender_tvm`, `security.secure_score_history`, `security.named_locations`, `security.tenant_allow_block_list`, `security.audit_log_searches`. Alert and incident streams are **near-real-time** (delta + push from Graph subscriptions where available).
- Read surface: alerts grid, alert detail, incidents grid, incident detail, Defender TVM grid, secure score over time, named locations grid, allow / block list grid, audit log search results.
- Write surface: alert state change (set), incident state change (set), CA policy add / edit / remove, CA exclusions / service-exclusions, named location add / edit / remove, allow / block list add / remove, audit log search initiate (saga), BEC check + remediate (a sensitive multi-step flow with explicit confirmations).
- Real-time: Graph change-notification subscriptions for alerts and incidents where supported; otherwise delta on a fast cadence.
- Standards specific: CA baseline, Defender baseline, secure-score targets, audit-log baseline, etc. (≥ 25 standards).
- The full Standards inventory across all four domains in this phase reaches ≥ 95 additional standards on top of Phase 6's ≥ 30, getting to ≥ 125 total — well past CIPP's currently-functioning subset and on track for the 187 by Phase 11.

### Out of scope

- BPA migration (Phase 10).
- PSA / RMM integrations (Phase 8).
- Reports / analytics that aggregate across all four domains (Phase 9).
- Multi-region performance tuning (Phase 11).

### Entry criteria

- Phase 6 exit criteria all green.
- Test customer tenants representative of each domain's surface (a tenant with substantial Intune fleet; one with non-trivial Exchange policy state; one with active Defender alerts; one with broad SharePoint sharing).

### Exit criteria

1. Each of the four domains ships full read + write parity for the CIPP feature set documented in `README.md` (the 13-domain inventory).
2. Per-domain projections meet the read-path SLO from Phase 3.
3. Sensitive operations (BitLocker / LAPS recovery, mailbox restore initiation, BEC remediate) require step-up MFA at the action moment, audit to a separate lane, and are reviewable in the SuperAdmin "sensitive actions" report.
4. The Exchange shim runspace pool sustains the bulkhead under sustained load without process-level memory regressions over a 24h soak.
5. Real-time security alerts surface in the UI within 60 s p95 of Graph emission.
6. ≥ 95 additional standards land alongside their domains; total ≥ 125; all three modes; all idempotent.
7. Cross-domain tests: a Standards run that touches all four domains simultaneously respects the per-MSP bulkhead, completes within budget, and produces a single aggregated run row.
8. Coverage `>= 80%` on each of the four domains' application + domain code.
9. Playwright smoke covers the most-used workflow per domain (deploy a CA policy, edit a transport rule, add a SharePoint site, dismiss an alert).
10. `MEMORY.md` updated; per-domain ADRs (0006–0009) recording any non-obvious choices.

### Verification

- Per-domain shadow-CIPP demo (4 separate recordings) showing parity and timing.
- The 24h soak with the Exchange shim is run on staging; memory + handle counts before and after are within 5%.
- A pen-test sweep against the BitLocker / LAPS / BEC sensitive flows asserting step-up enforcement.
- A randomised property test for each domain: 1,000 random read requests across warm tenants, zero request-thread Graph paginations.

### Risks

- **Phase volume.** This is the largest phase. Mitigation: track the four domains as parallel work streams against the same shared infrastructure; a domain's failure to ship doesn't block the others; weekly review checkpoint per domain.
- **PowerShell shim regressions.** Long-running runspace pools are a known memory-leak surface. Mitigation: runspaces recycle every N invocations or M minutes; a leak detector job alarms on RSS growth past a budget.
- **Real-time alert volume.** Some tenants emit thousands of alerts/day. Mitigation: stream into a dedicated `security.alerts` partition; SignalR publish is debounced per alert id; the UI grid uses virtualised rendering.

---

## Phase 8 — Automation and integrations

**Goal:** Make the platform an active participant in an MSP's tooling ecosystem rather than just a console. Scheduled jobs the MSP can author and modify, webhook receivers that bridge external events into alerts and audit, and outbound integrations with the PSAs / RMMs / billing systems that MSPs already run on. CIPP has all of this; the rebuild does it with typed adapters, signed payloads, and per-integration bulkheads.

### Scope

#### Scheduler UI

- A scheduled-items grid: list, create, edit, run-now, pause, delete.
- Item types: standards run, BPA run, report generation, custom Graph batch (typed parameters, never an arbitrary URL forwarder), webhook fanout, alert configuration evaluation.
- Cron-style schedule with timezone awareness; "run on the first Tuesday of the month at 09:00 in the MSP's timezone".
- Run history per item, with outcome, duration, and any per-tenant breakdown (for fan-out items).
- Pause/resume preserves cron alignment.
- Per-item idempotency keys: a missed run is **not** retroactively executed; the next aligned tick runs.

#### Alert configurations

- An alert configuration is `(MspId, Trigger, Filter, Action[])`.
- Triggers: standards drift, security alert, sign-in anomaly, license usage threshold, scheduled job failure, GDAP relationship change, integration health.
- Filter: tenant scope + condition expression (typed; not free-text PowerShell).
- Actions: SignalR-only, email, Teams webhook, Slack webhook, PSA ticket creation, custom HTTP webhook with signed payload.
- All actions are queued via Service Bus; fire-and-forget but auditable.

#### Webhook receivers (`hooks.{domain}/v1/{provider}`)

- Per-provider HMAC signature verification (the secret rotates; both old and new accepted during a 7-day window).
- Replay protection: nonce + timestamp window (5 min); duplicate nonces rejected.
- Provider-specific payload shape parsed into a typed event; unknown shapes audit and 400.
- Standard providers wired: Microsoft (Defender, M365 audit log routing), Halo, NinjaOne, Pax8, Hudu, IT Glue, Datto, Microsoft Service Health.
- A "generic" receiver for MSP-defined webhooks accepts a JSON body, validates against an MSP-provided JSON Schema, and emits an internal event.

#### Outbound integrations

- Per-integration adapters in `TenantManagement.Integrations.<Provider>` projects, each with a typed `IIntegrationAdapter`.
- Standard adapters in scope this phase:
  - **Halo** — ticket create / update / link.
  - **NinjaOne** — alert create, organisation sync.
  - **Pax8** — license sync, billing usage report.
  - **Hudu** — page sync (per-tenant documentation export).
  - **IT Glue** — flexible-asset sync.
  - **Datto** — alert ingest (incoming) + ticket sync (outgoing).
- All adapters share a per-integration bulkhead so a slow PSA cannot stall the rest of the platform.
- All adapters store credentials in `automation.integration_credentials`, encrypted via DataProtection (no plaintext in `appsettings.*.json`, no env vars).
- Health surface: an integrations dashboard showing per-MSP per-integration last-success / last-failure / queue depth.

#### Email & Teams

- Outbound email via Graph `sendMail` from a platform mailbox (delegated, signed, DKIM-aligned at the platform domain).
- Teams / Slack webhooks: signed, replayable, with per-MSP rate caps.
- The default recipient list per alert configuration is configurable per MSP.

### Out of scope

- A "run any cmdlet name from a queue payload" generic dispatcher (CIPP's `& $cmdletName @args` pattern). **Forbidden.** Every scheduled item is one of a typed set.
- Reports / analytics surface (Phase 9).
- BPA legacy bridge (Phase 10).

### Entry criteria

- Phase 7 exit criteria all green.
- Test sandbox accounts available for Halo, NinjaOne, Pax8, Hudu, IT Glue, Datto.
- Microsoft Service Health webhook endpoint registered.

### Exit criteria

1. An MSP authors a scheduled item that runs a Standards template across all tenants every Sunday at 02:00 in their timezone; the next run executes correctly; missed runs (paused over a window) do not retroactively execute.
2. An alert configuration triggered by drift on Standard X creates a Halo ticket within 60 s p95.
3. A Datto inbound webhook arrives, is verified, parsed, deduplicated, and surfaces in the activity feed.
4. Each integration's credentials cycle through rotation without MSP intervention; a rotation event audits.
5. A purposefully misbehaving integration (slow, returning 5xx) trips its bulkhead within budget; the rest of the platform remains healthy; the integrations dashboard surfaces the state.
6. Webhook signature verification rejects payloads with bad signatures, expired nonces, or replayed nonces; each rejection audits.
7. Per-integration coverage `>= 70%` (lower than core because adapters are heavily mocked at the boundary; integration tests use VCR-style cassettes).
8. The "generic" MSP-defined webhook validates against the MSP's JSON Schema; non-conforming payloads return 422 with a typed problem-details response.
9. `MEMORY.md` updated; ADR-0010 records the integration-adapter contract and the bulkhead-per-integration rule.

### Verification

- An end-to-end recorded scenario: drift detected → alert configuration triggered → Halo ticket opened → Hudu page updated → Slack message sent → activity feed shows the chain.
- A chaos test that takes Halo offline for 30 minutes; observe other integrations are unaffected; observe Halo work queues drain on recovery.
- Replay-attack test against each provider's webhook receiver; all rejected.

### Risks

- **Adapter shape churn.** PSA APIs evolve. Mitigation: each adapter has a typed surface; version pin; integration tests run nightly against sandbox accounts; adapter version mismatches fail fast at startup with a clear "PSA X needs adapter v2.x" message.
- **Webhook spam.** A misconfigured external system can flood our receivers. Mitigation: per-MSP per-provider rate cap with shed-load behaviour; audited 429 responses.
- **Misuse of "generic" webhooks.** A loose schema is an injection vector. Mitigation: schema is enforced; payload is parsed into a typed envelope; downstream handlers receive only the typed shape, never a raw blob.

---

## Phase 9 — Reports and analytics

**Goal:** Cross-tenant rollups and read-side projections that answer the questions an MSP owner asks at the end of the month: "where is our license waste? who hasn't signed in? whose MFA is weak? what apps did users consent to that they shouldn't have? which domains are misconfigured?" These are **read-side** projections that never write to Graph, and which run against the L3 store + Graph on-demand for stuff that isn't projected.

### Scope

#### Reports surface

- **Domain analyser** — per-tenant DNS / DKIM / SPF / DMARC / MX / MTA-STS / TLS-RPT state, with per-record explanation, mailbox-domain mismatch detection, and trend arrows over 90 days.
- **License usage** — per-MSP / per-tenant SKU consumption vs. assigned, idle license detection, license cost mapping (with Pax8 sync from Phase 8 for true-up).
- **Inactive accounts** — users with no sign-in in N days (configurable thresholds for licensed / unlicensed / shared mailboxes), guests dormant, accounts with passwords-never-expires.
- **MFA report** — per-user MFA enrolment, methods registered, "registered but never used", per-tenant rollup.
- **App consents** — per-tenant OAuth grant inventory, risk score (publisher / scope / user-or-admin consent), trend over time.
- **OAuth apps** — apps approved or pending, scope-by-scope risk surface.
- **Standards alignment** rollup — already shipped in Phase 6; this phase adds historical trend ("how did our alignment evolve over the last 6 months?").
- **Drift** rollup — last-90-day drift events per tenant per category.
- **Sign-in trends** — sign-in volume by tenant / location / risk level. (Doesn't store all sign-ins; aggregates daily into a `reports.signin_daily_aggregate` table.)
- **Audit trends** — per-MSP audit-event volume by command type, week-over-week deltas.

#### Read replica use

- All long-running report queries hit the Postgres read replica (`Reports` context only) so they never perturb OLTP.
- A "snapshot" feature: an MSP can run a report and the result is materialised into `reports.snapshots` for later compare or share, with a TTL.

#### Export

- Every report exports to CSV and to a paginated PDF for client-facing delivery.
- Exports are generated server-side (off-thread Hangfire job), stored in Blob, and served via signed URLs that expire in 24 h.
- PDF templating uses a typed model + handlebars; no untrusted templating.

#### Scheduled reports

- An MSP can schedule any report to run on a cron and deliver to an email distribution list, a Teams webhook, or a PSA ticket.
- Scheduled report runs go through Phase 8's scheduler and Phase 8's outbound integrations.

### Out of scope

- BPA migration into Standards (Phase 10).
- Per-MSP billing analytics for the platform itself (Phase 12).
- A "build your own report" SQL surface — explicitly **not** in scope; the predefined reports plus scheduled exports cover the use cases CIPP customers actually use.

### Entry criteria

- Phase 8 exit criteria all green.
- Read replica configured and running with measurable replication lag (< 1 s p95 in staging).
- Pax8 sync (or equivalent) populating license cost mappings for the test MSPs.

### Exit criteria

1. Each report renders for a 200-tenant MSP in ≤ 3 s p95 from the read replica.
2. Domain analyser flags a deliberately-misconfigured DKIM record on the test tenant within one warmer cycle and shows a remediation hint.
3. License usage matches Pax8 truth ± 1 license per tenant (Pax8 has occasional eventual consistency).
4. Inactive-accounts thresholds are persisted per-MSP and survive a deploy.
5. MFA report's "registered but never used" classification is verified against a stub state matrix.
6. App-consents risk score follows a documented rubric (`docs/reports/app-consent-risk.md`); changes to the rubric require a `FEEDBACK.md` entry.
7. CSV and PDF exports for every report; signed URLs expire correctly.
8. Scheduled report delivery via the three channels (email, Teams, PSA) succeeds end-to-end.
9. Coverage `>= 80%` on `Application/Reports/*`.
10. `MEMORY.md` updated.

### Verification

- A "month-end" recorded run by a stand-in MSP user generating all reports and one PDF export; total wall-clock ≤ 30 min for a 200-tenant MSP.
- A delivery test: a scheduled report is delivered to an inbox + a Teams channel + a PSA ticket within budget.
- The replication-lag dashboard during peak load stays under 1 s p95.

### Risks

- **Replica lag** under heavy ingest. Mitigation: lag SLO is a release gate; long-running queries that find lag > 5 s fall back to the primary with a warning footer.
- **PDF generation memory.** Some reports for large MSPs are big. Mitigation: streaming PDF generation; Hangfire job has a memory budget; over-budget jobs split the report into per-tenant pages.
- **App-consent risk subjectivity.** The score is a heuristic and can be wrong. Mitigation: the rubric is published; the score is editable by the MSP per app; overrides are versioned.

---

## Phase 10 — BPA migration

**Goal:** Honour the install base. CIPP's Best Practice Analyzer is one of its most-used surfaces. The rebuild's Standards engine is a strict superset (typed handlers, drift detection, three modes), but BPA-rule-by-name is what existing CIPP users have muscle memory for and what their reports already reference. This phase ships a forward-compatible BPA wrapper around the Standards engine, plus a one-way migration so MSPs can move CIPP BPA results into the new model without losing history.

### Scope

#### BPA compatibility surface

- A `Bpa` bounded context that exposes endpoints and a UI shape compatible with CIPP's BPA pages (so screenshots, Hudu pages, and PSA tickets that link to BPA URLs continue to make sense).
- Internally, every "BPA rule" maps 1:1 to a Standards handler in **Report-only** mode. There are no separate BPA execution paths.
- The BPA grid (per-MSP) and BPA detail pages are read-throughs onto the Standards alignment grid with BPA-specific column ordering and naming.
- "Run BPA" is a Standards run with the BPA template scope.
- BPA report exports preserve CIPP's column shape so existing PSA workflows that parse them keep working for at least the rebuild's first GA year.

#### BPA template import

- A migration tool reads CIPP BPA templates (JSON exported from CIPP) and:
  - Maps each BPA rule to a corresponding Standards handler. Rules without a 1:1 map are surfaced as "unmapped — needs manual review" with the proposed candidate handlers.
  - Preserves naming, severity classification, and any tenant-scope overrides.
  - Writes the resulting Standards template into `templates.standards_templates` with a `Source = "BPA-Migrated"` tag.
- Conversion is **dry-run by default**; the MSP reviews the proposed mapping before committing.

#### Historical BPA data import

- A one-shot migration utility reads a CIPP `BpaResults` table export (from Azure Tables) and projects it into `bpa.historical_runs` for visibility and continuity.
- Historical runs are read-only; new BPA runs go through Standards.

#### Deprecation plan

- The BPA endpoints are documented as a **compatibility surface**, not a maintained product. The deprecation timeline is published in `README.md`: the surface stays for at least 12 months past GA, then enters a 6-month sunset.
- Standards is the forward direction. Every BPA UI page links to its Standards equivalent.

### Out of scope

- Building net-new BPA-only logic. Anything new lands as a Standards handler.
- Migrating CIPP's "templates" that aren't BPA — those belong to Phase 6 (Standards) or Phase 7 (per-domain) imports.
- A two-way sync between BPA and Standards. The flow is one-way: BPA → Standards.

### Entry criteria

- Phase 9 exit criteria all green.
- A real CIPP BPA template export (with breadth of rule coverage) and a real `BpaResults` Table export from a participating MSP (anonymised) for verification.

### Exit criteria

1. BPA endpoints + UI surface render and produce results indistinguishable from a Standards run with the equivalent template scope.
2. The migration tool maps ≥ 90% of CIPP's stock BPA rules automatically; the remainder are surfaced clearly for manual review.
3. Importing a CIPP BPA template into the Standards engine produces a template that, when run, yields the same per-tenant pass/fail outcomes as CIPP's BPA on the same tenants (verified on the participating MSP's data).
4. Historical BPA results are importable and readable in the rebuild's BPA grid.
5. Every BPA UI page has a "this is moving to Standards" banner with a link.
6. Coverage `>= 80%` on `Application/Bpa/*` and the migration tool.
7. `MEMORY.md` updated; ADR-0011 records the BPA-as-compatibility-surface decision and the deprecation timeline.

### Verification

- Side-by-side comparison: run CIPP BPA + rebuild BPA on the same anonymised tenant; assert per-rule outcomes match.
- A migration dry-run against the participating MSP's exported template; manual review of the < 10% unmapped surface.
- The deprecation banner is reviewed for tone (not punitive; informative).

### Risks

- **Mapping accuracy.** A wrong BPA → Standards mapping silently changes outcomes. Mitigation: dry-run is mandatory; a "compare last CIPP BPA outcome to first rebuild BPA outcome" report runs as part of the migration handoff.
- **Hudu / PSA URL stability.** External pages link to BPA URLs. Mitigation: URL shape is preserved; deprecation is announced 12 months ahead.
- **Sunset hostility.** MSPs feel forced to migrate. Mitigation: Standards is genuinely better, and the migration tool does most of the work; the deprecation period is generous.

---
