# REBUILD_PLAN.md — Master Strategic Plan

> The single document that, read end to end, explains: what's wrong with CIPP today, what we are building instead, why those specific choices, in what order, with what risks. Cross-references the other planning docs (`README.md`, `CLAUDE.md`, `ARCHITECTURE.md`, `PHASES.md`, `MEMORY.md`, `FEEDBACK.md`) and is the only doc that puts all of them on one page.
>
> Read this **first** if you are joining the project. The other docs are deeper on their respective subjects; this one is the map.
>
> Status: **draft** — sections land incrementally. Each section is its own commit so the plan can be reviewed and adjusted in pieces.

---

## 1. Executive summary

**What.** A clean-room C# / .NET 10 rebuild of the CIPP MSP M365 management portal, replacing CIPP's PowerShell-on-Azure-Functions stack with a typed ASP.NET Core 10 + Blazor Web App + PostgreSQL + Redis + Hangfire + Service Bus stack, distributed as a managed multi-MSP SaaS with a self-host container option.

**Why.** CIPP has structural problems that cap its scale, raise its operating cost, and slow its iteration. The four largest:

1. **The runtime cannot cache.** A JS frontend hydrating from a stateless PowerShell Function App has nowhere to hold cross-request state, so every page view either blocks on a paginate-Graph-until-done call (times out at 100+ tenants) or skips caching and re-pays the Graph cost on every interaction. The user-visible perf complaints (Issues `#1064`, `#2883`, `#75`, Discussion `#4979`) all collapse to this.
2. **The codebase is 90% Graph proxying.** CIPP-API is structurally a 700+-branch `if/else` over Microsoft Graph endpoints written in PowerShell. The PowerShell ecosystem lacked typed Graph clients, so the project hand-rolled wrappers for auth, scopes, app registrations, batch, retry, pagination — all of which `Microsoft.Graph` v5 + `Microsoft.Identity.Web` already do natively in .NET. The wrappers add maintenance cost without adding capability.
3. **The data layer punches above its weight.** Azure Table Storage holds templates that have already broken the 64 KB row limit (Issue `#1806`, open since 2023). Refresh tokens are mirrored into process environment variables, and the custom `CIPPSharp.dll` token cache shim exists to paper over the absence of `IConfidentialClientApplication` in PowerShell.
4. **The distribution model is fork-per-MSP.** Every MSP forks the repo and pulls upstream. This is the documented source of "upstream broke my fork" tickets on every major release, and it makes a coordinated security response across the install base structurally impossible.

**How.** Five pillars, each addressing one or more of the above:

1. **A real four-layer cache** (`ARCHITECTURE.md` §3): L1 `IMemoryCache` per node → L2 Redis distributed → L3 Postgres typed projections refreshed by background Graph delta workers → bounded fallback to Graph only for cold tenants. The request thread never paginates Graph.
2. **A real backend** with typed handlers, typed DTOs, typed validators, one controller registry per bounded context (~13 instead of 533 flat function files), structured logs and traces, single deployable.
3. **A real auth posture** using `Microsoft.Identity.Web` for OIDC + OBO + confidential client. Refresh tokens live in `IDataProtectionProvider`-encrypted Postgres rows accessed only via `IRefreshTokenStore` — never in environment variables. The custom token-cache shim is replaced by `IDistributedCache` with `SemaphoreSlim` coalescing.
4. **A clean-room implementation that does not re-port CIPP**. The rebuild reads Microsoft Graph contracts and CIPP feature documentation directly; no `.ps1` is translated line-for-line. The Standards engine replaces 187 PowerShell standards files with typed `IStandardHandler` registrants in three modes (Report / Remediate / Alert) with JSON-Patch drift detection.
5. **A managed multi-MSP SaaS** with strict logical isolation (five layers of cross-MSP defense — `ARCHITECTURE.md` §5), plus a self-host container image as a secondary distribution. The fork-and-deploy model is gone.

**When.** 13 sequential phases over ≈ 48 engineering weeks (3-engineer team), from foundations through public GA. The phase boundary is a hard gate: a phase isn't done until every exit criterion is verified. Full breakdown in `PHASES.md`.

**Who is it for.** MSPs that operate delegated-admin against customer M365 tenants today and currently use (or have evaluated) CIPP. The rebuild's value-add is **caching, multi-tenant orchestration, the Standards engine, the UX, and the security posture** — not Graph proxying, which Microsoft already ships.

---

## 2. The CIPP audit — full findings

This section is the long-form audit. The shorter version is in `README.md`'s "Why this exists." If a finding here disagrees with a header summary elsewhere, this section is the source of truth.

### 2.1 Repository inventory

Two repositories make up CIPP:

- **`KelvinTegelaar/CIPP`** — the frontend. Next.js 16 + React 19 + MUI 7. Statically exported and hosted on Azure Static Web Apps. Authentication is Azure Static Web Apps' EasyAuth, which carries identity headers to the function app. License AGPL-3.0.
- **`KelvinTegelaar/CIPP-API`** — the backend. PowerShell 7.4 on Azure Functions v4. License AGPL-3.0.

Together they form a "frontend hydrates by calling backend API; backend is a giant `if/else` over Microsoft Graph" architecture that has been the de-facto open-source MSP M365 portal since 2021.

### 2.2 Backend findings

**File and function structure**
- **533 `Invoke-*.ps1` HTTP-trigger files** under `Modules/CIPPCore/Public/` — one file per endpoint, no controller registry, no routing table, no automated docs.
- **187 standards files** — one per BPA/Standards rule, each with bespoke remediation logic. There is no shared base class or interface. The execution engine reflects on file names.
- **Durable Functions** for orchestrations with strict version matching. A deploy whose orchestration version doesn't match the in-flight state nukes the orchestration; the project has shipped a user-facing **"Clear Durable Queue"** maintenance UI to mitigate.
- **No DTO layer.** Inputs and outputs are PowerShell hashtables. Validation is per-file `if`-checks on `.Body.X` values.
- **No global error handling.** Each file has its own `try/catch` style; some swallow exceptions, some don't.
- **No typed test suite.** Pester tests exist in pockets but are not gating.

**Data layer**
- **Azure Table Storage** is the primary store. Templates, settings, BPA results, audit log, scheduler state — all in Tables.
- The 64 KB per-row limit on Tables is a known production bug (**Issue `#1806`**, open since 2023). Templates that exceed the cap fail with a low-fidelity error.
- **Blob Storage** holds binary artifacts and some larger-than-64-KB JSON payloads.
- **No relational store.** No joins. No indexes beyond `PartitionKey`/`RowKey`. No foreign-key integrity.

**Authentication**
- A custom **SAM (Secure Application Model)** wrapper handles app registration and consent. The wrapper is necessary because PowerShell did not have first-class confidential-client identity primitives.
- **Refresh tokens are mirrored into process environment variables** via the `Set-CIPPRefreshTokens` cmdlet. Any code path that reads `Env:RefreshToken` has access; the blast radius if any handler leaks env is the entire customer fleet.
- A custom **`CIPP.CIPPTokenCache` shim compiled into `CIPPSharp.dll`** provides a process-local token cache.
- Token acquisition is per-request. A burst of concurrent requests for the same `(tenant, scope)` can produce N concurrent STS round-trips.

**Performance**
- Documented **15–20 second cold start** in the project's own FAQ.
- **100+ customer tenants → ≥ 15 minute BPA refresh** (Discussion `#4979`).
- **All-tenants user list timeout** at scale (Issue `#2883`).
- Throughput is bounded by Functions per-runspace module loads; under load, runspaces are recycled and the module reloads thrash.

**Operations**
- **Self-host-by-fork** distribution: every MSP forks the repo and pulls upstream. Major releases routinely break forks (their own release notes call this out as expected).
- A central **CIPP-SAM** GitHub App handles tenant consent across the install base; one mis-step here has implications across hundreds of MSPs.
- **No graceful deploy path** for in-flight Durable Function state — handled by the "Clear Durable Queue" UI mentioned above.

### 2.3 Frontend findings

- **Next.js 16 + React 19 + MUI 7**, statically exported. The static export is then hosted on SWA, which limits dynamic capabilities the modern Next runtime would otherwise provide.
- **Toolbar files exceed 1,400 lines** in places. There is no consistent layout primitive. Component reuse is by copy.
- **TypeScript adoption is partial.** Some files are `.tsx`; many are `.jsx` with `any`-shaped props.
- **Forms libraries are mixed:** Formik in some places, react-hook-form in others, hand-rolled state in still more.
- **No frontend test suite.** No Jest, no Vitest, no Playwright.
- **Polling is the default real-time strategy.** No SignalR, no SSE, no WebSocket. The toll on the function app from polling at the install-base scale is non-trivial.
- **EasyAuth headers** as the auth boundary: the frontend trusts what SWA injects. Token acquisition for downstream calls happens server-side in the function app.

### 2.4 Pain points captured directly from issues / discussions

- Issue **`#1064`** — perf at scale.
- Issue **`#2883`** — all-tenants user list timeout.
- Issue **`#75`** — perf complaints (long-running).
- Discussion **`#4979`** — 60-tenant deployment perf complaints; the discussion explicitly notes "you can't really cache here because there's no real backend."
- Issue **`#1806`** — 64 KB Table row limit on templates, unresolved since 2023.

These are not edge cases — they trace to the architecture, not the code quality. A "fix" inside the current architecture is partial; a rewrite addresses the cause.

### 2.5 Security posture

- **AGPL-3.0** plus a publicly-advertised **$50 + swag bug-bounty** for a tool with delegated admin into every customer's M365.
- Refresh tokens in environment variables (above).
- No published `SECURITY.md` coordinated-disclosure process.
- Tenant-token plumbing is ad-hoc per file.
- Audit log is in Azure Tables, with the same 64 KB row limit and the same pagination characteristics.

The blast radius of any compromise is the entire delegated install base across every MSP using CIPP. The posture does not match.

### 2.6 What is good about CIPP

A balanced audit acknowledges what works:

- **Feature breadth.** CIPP covers an MSP's M365 admin surface end to end. The rebuild's feature inventory is openly informed by what CIPP ships today.
- **Community.** The Discord and contributor base have institutional knowledge about real-world MSP M365 quirks that a clean-room rebuild benefits from acknowledging.
- **Documentation tone.** CIPP's own docs are honest about limitations; the FAQ openly states the cold-start number, the perf-vs-completeness trade-off, and the fork-friction.
- **GDAP plumbing.** Despite being in PowerShell, the GDAP-relationship handling is feature-complete and a useful reference for the rebuild's clean-room implementation.

These do not change the structural verdict, but they shape **how** the rebuild is positioned: a respectful successor, not a replacement-by-disparagement.

---

## 3. The rebuild thesis

Each finding from §2 maps to a specific architectural choice. None is a tactical "fix that one thing" — they are structural, and structurally addressed.

### 3.1 The cache is the centre of gravity

**Finding addressed:** §2.4 (`#1064`, `#2883`, Discussion `#4979`).

CIPP's binary choice is: paginate-Graph-then-render (slow first paint, blocks at scale) or paginate-on-demand (fast first paint, slow steady-state, no shared cache). There is no third option for that architecture **because the architecture has no place to keep cache state across requests**.

A real .NET backend changes the equation entirely. The rebuild's read path is:

- **L1 IMemoryCache** for in-process burst dedup.
- **L2 Redis** for cluster-wide hot reads.
- **L3 Postgres typed projections** as the durable cache, refreshed by **background Graph delta workers**.
- A **bounded fallback** to one Graph page only for newly-onboarded "cold" tenants.

The request thread **never paginates Graph**. Pages render from the projection with a "data refreshed N minutes ago" footer. After the first warm cycle, every subsequent read is a database/Redis operation, not an API operation. Writes go through Graph, then update the projection in the same Postgres transaction as the audit-log status flip, then invalidate L2, then publish a SignalR refresh. (`ARCHITECTURE.md` §3, §4.)

**Why this works structurally**: CIPP cannot do this not because no-one thought of it, but because the runtime cannot host the projection layer. The rebuild's runtime can.

### 3.2 The SDK is the abstraction; we don't re-abstract it

**Finding addressed:** §2.2 (700+-branch Graph proxying; per-file token plumbing; SAM ceremony).

Roughly 90% of CIPP-API is hand-rolled Graph plumbing: pagination via `nextLink` chasing, batch via custom helpers (which silently lose inner 429s), retry / `Retry-After` honouring per file, scope and app-registration wrangling. **All of this is solved primitives in `Microsoft.Graph` SDK v5 and `Microsoft.Identity.Web`**:

- `PageIterator<T>` — pagination + throttling + `Retry-After`.
- `BatchRequestContent` with per-subresponse status inspection.
- `Delta()` extensions on every paged resource.
- OBO, refresh, certificate, MSI, confidential-client OAuth.
- Distributed token cache with the standard `IDistributedCache` provider.

The rebuild's only owned abstraction is `IGraphTenantClient` — a per-`(MspId, CustomerTenantId)` factory that:
1. Carries tenant identity for telemetry.
2. Wraps the request adapter in a Polly v8 pipeline (bulkhead per MSP, rate limiter per customer tenant, retry, circuit breaker — `ARCHITECTURE.md` §6).
3. Forces batch subresponse inspection (so the inner-429 silent-failure bug class is impossible).
4. Audits every Graph call.

It does **not** wrap individual Graph operations. Application code calls `_client.Client.Users.PostAsync(...)` — straight SDK. (`CLAUDE.md` §8b — "the 'just a Graph wrapper' rule".)

**Why this works structurally**: every line of plumbing we don't write is a line we don't have to test, secure, or maintain. The SDK has Microsoft's full-time team behind it.

### 3.3 Auth complexity collapses when the language has the primitives

**Finding addressed:** §2.2 (SAM ceremony, env-var refresh tokens, `CIPPSharp.dll` token cache).

The CIPP "auth complexity" — scope soup, app-registration wrangling, per-tenant token plumbing, refresh-token-into-env-var, the bespoke token cache — exists because PowerShell didn't have first-class confidential-client identity primitives. .NET does. The rebuild's auth shape is:

- `Microsoft.Identity.Web` for OIDC sign-in (BFF posture: tokens never reach the browser).
- `IConfidentialClientApplication` + `ITokenAcquisition` for OBO (per user) and `client_credentials` (per worker).
- `IDistributedCache` (Redis) for token caching, with `SemaphoreSlim` coalescing so concurrent acquisitions become one STS call.
- `IRefreshTokenStore` over `IDataProtectionProvider`-encrypted Postgres rows for the rare long-lived-refresh-token cases. **Refresh tokens never appear in environment variables.** A grep step in CI fails the build if they do.
- Token rotation is a Hangfire scheduled job, not a "rerun the setup wizard" recovery story.

(`ARCHITECTURE.md` §7. `FEEDBACK.md` 2026-04-29 directive 3.)

**Why this works structurally**: the SAM wrapper is mitigation for missing language primitives. The primitives exist now. The mitigation goes away.

### 3.4 Bounded contexts replace flat function dumps

**Finding addressed:** §2.2 (533 flat `Invoke-*.ps1` files, no controller registry).

The rebuild collapses ~380 unstructured Azure Functions to **13 bounded contexts**, each with its own MediatR handlers, DTOs, validators, projections, and background jobs. (`ARCHITECTURE.md` §2.) The endpoint count drops by an order of magnitude not because we lose features but because:

- One handler replaces many one-off `Invoke-*.ps1` files (one `UsersController` group, not 22 user-related files).
- "Get a list" + "get one" + "delta since cursor" share one query handler with a parameter, not three files.
- Sub-resources are URL nesting, not new top-level endpoints.

Cross-context reads go through public DTOs; there are no cross-context FKs and no shared `DbContext`. Domain events are how contexts react to each other.

**Why this works structurally**: at 533 files the project doesn't fit in a single mental model. At 13 contexts, each context fits in one engineer's head, and a new feature lives in one context, not across the whole repo.

### 3.5 Standards as a typed registry, not 187 PowerShell files

**Finding addressed:** §2.2 (187 standards files, no shared base, reflection on file names).

Each standard becomes one C# class implementing `IStandardHandler`, decorated with `[Standard("Name", Category = ...)]`, with three modes (Report / Remediate / Alert), typed settings, and JSON-Patch drift detection against a stored baseline. (`CLAUDE.md` §8.) The orchestrator enforces "no remediate without a successful prior report in the same run." Templates live in Postgres `jsonb` (no 64 KB ceiling).

**Why this works structurally**: every standard is independently testable, the registry is enumerable, drift is a first-class concept rather than something the human has to spot in a report, and templates can be larger than 64 KB.

### 3.6 Postgres replaces Azure Tables for everything that matters

**Finding addressed:** §2.2 (64 KB Table row limit; no joins, no indexes, no FK integrity); §2.4 Issue `#1806`.

PostgreSQL via EF Core 10 + Npgsql for relational data. `jsonb` for the small set of legitimately schemaless data (per-standard settings, template payloads, drift diffs) — never for "the cached Graph response we'll re-parse on every read." Typed columns, indexes covering the queries the UI actually issues, time-partitioned audit / activity tables, soft-delete by default, and migrations as a CI step (never on app startup outside dev). (`ARCHITECTURE.md` §9.)

**Why this works structurally**: relational integrity, real indexing, and TTL-aware caching are foundations the runtime denies CIPP. Once those exist, the cache hierarchy in 3.1 is possible.

### 3.7 Multi-MSP isolation in five layers

**Finding addressed:** §2.5 (security posture mismatch); cross-MSP blast-radius ambient in CIPP.

Cross-MSP data access is impossible by construction, not by remembering to add a `where`-clause. Five independent layers, each individually sufficient: OIDC `tid → MspId` mapping; `MspContextAccessor` ambient; EF global query filters with a lint-enforced `IgnoreQueryFilters` allow-list; `ICustomerTenantAuthorizationService.AssertAccessAsync`; Graph token scoping per `(MspId, CustomerTenantId)`. (`ARCHITECTURE.md` §5.)

**Why this works structurally**: security review can't reason about correctness across an ambient hashtable threading through a function app. It can reason about a five-layer model where every layer is named, tested, and visible.

### 3.8 Managed SaaS, no fork-and-deploy

**Finding addressed:** §2.2 ("self-host-by-fork breaks on every major release"); §2.5 (coordinated security response across forks is structurally impossible).

The rebuild ships as a managed multi-MSP SaaS first; a self-host container image is a secondary distribution. Self-host upgrade is "pull a tagged image; the migrator runs migrations; restart" — not "rebase against upstream and resolve conflicts." There are no per-MSP forks to coordinate a security update across. (`PHASES.md` Phase 12.)

**Why this works structurally**: a single deployable surface is the only way to make a coordinated CVE response across the install base feasible. The fork model can never get there.

### 3.9 SignalR replaces polling

**Finding addressed:** §2.3 (polling toll on the function app at scale).

Per-domain SignalR hubs with Redis-backed backplane; the UI joins MSP- and tenant-scoped groups; writes publish progress and invalidations; reads reconcile on the next L2/L3 fetch (which is now correct because the write path invalidated cache before publishing). SignalR is best-effort low-latency; correctness is in the cache and audit, not in the broadcast. (`ARCHITECTURE.md` §10.)

**Why this works structurally**: pushing changes is far cheaper at scale than polling for them, both for compute and for client experience.

### 3.10 Audit is a first-class data class, not a log line

**Finding addressed:** §2.5 (audit log in Azure Tables with the same 64 KB / pagination caveats); operational forensics gap.

Audit log is a typed, queryable, retained data class in Postgres with 7-year retention and monthly partition archival to Blob. Every command and every Graph mutation writes an audit row in the same Postgres transaction as the projection update. Schema is typed (`actor`, `command`, `target`, `outcome`, `status`, `payload`, `traceId`); cross-MSP read access requires SuperAdmin + ticket id. (`ARCHITECTURE.md` §11.)

**Why this works structurally**: audit-as-log is unsearchable at scale. Audit-as-data is the substrate for forensics, compliance, and customer-facing transparency.

---

## 4. Timeline view across phases

The full phase detail (scope / out-of-scope / entry / exit / verification / risks per phase) lives in `PHASES.md`. This section is the at-a-glance roll-up and shows how the phases reinforce one another.

### 4.1 Phase progression

```
Phase 0   Foundations              ░░░░░░░░░░░░░░░░░░░░  empty deployable, green CI
Phase 1   Auth & tenancy           ▓▓▓░░░░░░░░░░░░░░░░░  signed-in users, MSP context, refresh-token store
Phase 2   Graph integration core   ▓▓▓░░░░░░░░░░░░░░░░░  IGraphTenantClient, Polly, batch, delta cursors
Phase 3   Caching & projections    ▓▓▓░░░░░░░░░░░░░░░░░  L1/L2/L3 + warmer; users as the reference
Phase 4   Identity domain          ▓▓▓▓░░░░░░░░░░░░░░░░  first end-to-end UI surface
Phase 5   Tenants domain           ▓▓▓▓░░░░░░░░░░░░░░░░  onboarding saga, GDAP, all-tenants grids
Phase 6   Standards engine         ▓▓▓▓▓░░░░░░░░░░░░░░░  typed registry, three modes, drift, templates
Phase 7   Endpoint Mgmt + ExO +    ▓▓▓▓▓▓▓▓░░░░░░░░░░░░  largest phase: four customer-tenant domains
          Collab + Security
Phase 8   Automation & integrations ▓▓▓▓░░░░░░░░░░░░░░░  scheduler, webhooks, PSA/RMM adapters
Phase 9   Reports & analytics      ▓▓▓░░░░░░░░░░░░░░░░░  cross-tenant rollups, scheduled exports
Phase 10  BPA migration            ▓▓░░░░░░░░░░░░░░░░░░  legacy compatibility + one-way migration
Phase 11  Hardening & multi-region ▓▓▓▓░░░░░░░░░░░░░░░░  soak, chaos, DR drills, active-passive
Phase 12  GA / launch              ▓▓▓░░░░░░░░░░░░░░░░░  pricing, public docs, security disclosure
```

**Engineering weeks (3-engineer team):** approximately **48 weeks** elapsed in total. Calendar duration depends on team size; the **order is non-negotiable** because each phase's exit criteria are entry criteria for the next.

### 4.2 The "minimum credible product" line

If we drew a line for the first phase at which the product could be put in front of a friendly design partner, it would be **after Phase 4** (Identity domain shipped end-to-end). At that point a partner can sign in, manage users / groups / devices on real tenants, and feel real performance. This is the inflection point where the architectural investments of Phases 0–3 pay back as a visible product, and where feedback loops with real users start.

The phases between Phase 4 and Phase 12 are all about expanding feature surface and hardening — the architectural shape is set by Phase 3.

### 4.3 What gates the timeline

The timeline is gated, in order, by:

1. **Entra ID app registration consent process.** Phase 1 cannot complete until multi-tenant consent works end-to-end. Mitigation: this is started in parallel with Phase 0.
2. **A representative test customer tenant with realistic fleet.** Phases 3+ need ≥ 1,000 users / ≥ 200 devices etc. for the perf claims to be meaningful. Mitigation: provisioning tooling lands in Phase 0.
3. **Partner Center sandbox availability.** Phase 5 onboarding saga needs a working invite flow against a sandbox. Mitigation: provision early in Phase 2.
4. **Pen-test scheduling.** Phase 11 includes an external pen-test; the calendar slot is booked at the start of Phase 9 to avoid being the long pole at the end.
5. **Stripe / billing integration.** Phase 12 needs a production Stripe account and legal review of pricing terms. Mitigation: legal kicks off in Phase 10.

### 4.4 Parallel tracks

Some work threads run continuously alongside the phase work:

- **Documentation.** Each phase updates `docs/` for its surface; a phase isn't done if its docs are missing.
- **ADRs.** Non-trivial decisions get an ADR. Numbering is chronological.
- **Security review.** Each phase has a checkpoint at exit; the `/security-review` skill runs before merge.
- **Performance budgets.** Phases touching UI verify the budget; drift is investigated, not normalised.
- **Accessibility.** WCAG 2.2 AA across every shipping page; verified with axe-core in CI.
- **Internationalisation.** Strings extracted into resources from day one; English first.
- **Telemetry hygiene.** No metric without a dashboard, no log line without an event id, no alert without a runbook.

(See `PHASES.md` "Cross-cutting tracks".)

---

## 5. Risks and how we manage them

A risk register at the strategic level — phase-level risks live in `PHASES.md` per phase. Each strategic risk has a category, the failure mode, and the structural mitigation.

### 5.1 Architectural risks

| Risk | Failure mode | Mitigation |
| ---- | ------------ | ---------- |
| The cache hierarchy is over-engineered for our actual load | We pay complexity cost without a return | Phase 3 ships **users only** as the reference; subsequent resources copy the pattern only if they meet the same bar. |
| Standards engine doesn't reach the breadth CIPP has | MSPs can't migrate, churn back to CIPP | Phase 6 ships ≥ 30 standards; Phase 7 brings ≥ 95 more alongside their domains; Phase 11 closes the gap to 187 total. |
| Real-time push (SignalR) loses messages in flight | UI shows stale state until next page nav | SignalR is best-effort by design; the cache + audit are the correctness layer; UI grids reconcile on next read |
| Multi-region adds operational burden we can't carry | On-call drowns | Active-passive only at GA; active-active deferred until measured load justifies it |

### 5.2 Operational risks

| Risk | Failure mode | Mitigation |
| ---- | ------------ | ---------- |
| Graph quota changes (Microsoft tightens limits) | Fan-out work degrades fleet-wide | Per-MSP bulkhead + per-tenant rate limiter; degrade gracefully to L3 reads with banners; SLO dashboards alarm before user impact |
| Postgres failover takes longer than RTO | Customer-visible outage | Failover drilled in Phase 11; runbook in `docs/operations/runbooks/`; automated promotion script |
| Refresh-token leak | Cross-customer compromise | Tokens DataProtection-encrypted in Postgres, never in env; rotation by Hangfire job; revoke-all-by-MSP path drilled in Phase 11 |
| Bad deploy reaches prod | Downtime / data corruption | Migrations are a CI step before deploy; Container Apps revision rollback in 1 click; canary + soak before traffic shift |

### 5.3 Product risks

| Risk | Failure mode | Mitigation |
| ---- | ------------ | ---------- |
| MSPs don't migrate from CIPP because their workflows are entrenched | Slow customer ramp | Phase 10 ships a forward-compatible BPA surface and one-way migration tooling; the rebuild is positioned as a respectful successor, not a replacement-by-disparagement |
| Pricing miscalibration | First-month churn | Design-partner cohort in Phase 12 is the canary; pricing is reviewable post-launch; trial flow with no card |
| Feature parity gaps surfaced post-launch | Customer-visible regressions | Shadow-CIPP demos at each phase exit (P4 onward); the rebuild's feature inventory tracks CIPP's surface explicitly in `README.md` |

### 5.4 People risks

| Risk | Failure mode | Mitigation |
| ---- | ------------ | ---------- |
| Single-engineer dependency on cache / Graph / Standards subsystems | Bus factor of one | Pair-rotation per phase; ADRs document non-obvious choices; runbooks for every operational surface |
| Burnout on a 48-week build | Quality drop, missed exits | Phases are sized so each one ships something tangible; cross-cutting tracks prevent perpetual debt; a phase can extend rather than compress |
| External pen-test reveals a structural issue | Late-stage rework | Internal pen-test sweeps at every phase exit; the external test in Phase 11 should not be the first time a security perspective sees the system |

---

## 6. Success criteria

What does it look like when this works?

### 6.1 Technical SLIs / SLOs at GA

| Metric | Target |
| ------ | ------ |
| Read-path p95 latency over warm cache | ≤ 250 ms |
| Read-path p99 latency over warm cache | ≤ 600 ms |
| UI page render p95 (warm) | ≤ 500 ms |
| Write-path p95 (audit-ack to projection-ack) | ≤ 800 ms |
| Graph throttle rate | ≤ 0.5% of calls |
| Delta sync failure rate | ≤ 5% (alarm threshold) |
| L2 cache hit ratio (lists) | ≥ 90% over warm population |
| Auth denial false-positive rate | ≤ 0.1% |
| Soak: sustained 24h at GA load | Zero SLO violations |

### 6.2 Product outcomes

- A friendly design-partner MSP onboards a 100-tenant customer fleet in < 1 day end-to-end.
- The all-tenants user list page renders in ≤ 800 ms p95 for that 100-tenant MSP — i.e., the headline CIPP perf complaint is gone.
- An MSP can run a Standards template across all customer tenants and see per-tenant outcomes streaming in via SignalR within seconds.
- The deprecation of CIPP BPA is non-disruptive: existing PSA / Hudu links continue to resolve in the rebuild's compatibility surface; standards run produces results equivalent to CIPP BPA on the same tenant.
- A security researcher submits a report through the bug-bounty programme and receives a commercial-grade reward within the documented disclosure timeline.

### 6.3 Operational outcomes

- Region failover is exercised quarterly with a clean recovery.
- A coordinated security response (e.g., a rotated cert, a flagged advisory) reaches the entire install base via a tagged image release within 24h.
- The audit log answers "did a human or a robot do this?" for every customer-tenant mutation.
- Operators have a single Hangfire dashboard plus a single OTel-fed observability surface and never need to ssh into a container.

### 6.4 Strategic outcomes

- The product is positioned as the credible commercial-grade evolution of the CIPP concept.
- The Standards engine accumulates MSP-contributed handlers in a structured way (clean-room Pull Requests against a typed contract, not orphaned `.ps1` files).
- The platform is a viable substrate for adjacent products (e.g., a customer-tenant compliance score, a per-MSP marketplace of certified Standards templates) without architectural rework.

---

## 7. Reading order for new joiners

For someone joining the project for the first time, read in this order:

1. **`README.md`** — what the project is and the target tech stack.
2. **`REBUILD_PLAN.md`** (this file) — the strategic map across all the others.
3. **`ARCHITECTURE.md`** — how the pieces fit together at runtime.
4. **`PHASES.md`** — the phased build plan.
5. **`CLAUDE.md`** — the coding standards (and the rules that govern AI-assisted work specifically).
6. **`MEMORY.md`** — what was last touched, what's next.
7. **`FEEDBACK.md`** — past corrections; do not repeat them.

If a session is starting and someone has read only one document, read **`MEMORY.md`** first to catch up on state, then this document for context.

---

## 8. Update protocol

This document is **append-only** within a phase: new findings become new sub-sections, old findings stand. If a strategic position changes, the old position is preserved (struck-through or marked "superseded YYYY-MM-DD") and the new position is added below, with an ADR recording the change. The doc's purpose is to be the canonical strategic narrative — losing the trail of how we got here makes the document useless.

If the audit findings in §2 are revised (e.g., CIPP fixes one of them), the original finding stays with a "**Resolved upstream YYYY-MM-DD**" marker and a note. The rebuild's value-add is structural; one upstream fix doesn't invalidate the rebuild's premise, but the trail must be honest.

