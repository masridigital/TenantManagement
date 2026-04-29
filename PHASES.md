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
