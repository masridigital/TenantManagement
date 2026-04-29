# MEMORY.md — Session State

> Update this file at the **end of every session**. The next session reads it first to know exactly where work stopped, what's in flight, and what's next.
>
> Format: keep the most recent session at the top. Older sessions roll down. Trim to last 10 sessions.

---

## Active state

- **Phase:** Pre-Phase 0 (Planning) — **planning workspace complete**
- **Branch:** `claude/review-cipp-repos-1iNqQ`
- **Current goal:** Planning documents are landed. Next session opens Phase 0 (Foundations).
- **Next concrete task:** Begin Phase 0 per `PHASES.md` — solution skeleton, infra-as-code, CI/CD, observability — no business code. Start with the .NET 10 solution layout and the Bicep modules in parallel.

---

## Session log

### 2026-04-29 (cont.) — ARCHITECTURE / PHASES / REBUILD_PLAN landed section by section

**Goal:** Complete the remaining three planning documents that prior session deferred, with each section pushed individually so the trail is incremental and reviewable.

**Done this session:**
- Authored `ARCHITECTURE.md` end to end (15 sections, ≈ 970 lines), pushed section by section:
  - §0 Thesis (the four pillars: real cache / real backend / real auth posture / real distribution).
  - §1 System context — actors, data classes, upstream dependencies, in/out interfaces, explicit out-of-scope.
  - §2 Bounded contexts — the 13-context map replacing CIPP's ~380 unstructured Functions; cross-context rules (no shared DbContext, no entity reach-through).
  - §3 Cache hierarchy and read path — L1/L2/L3 + bounded fallback; resolution order; freshness windows; key conventions; anti-rules.
  - §4 Write path — five steps (authorize / write-ahead audit / Graph / projection-update-in-same-txn-as-audit-flip / invalidate+notify); idempotency; bulk batching; compensating-action policy.
  - §5 Multi-MSP isolation — five-layer defense (auth, MspContextAccessor, EF filters with lint-enforced bypass, ICustomerTenantAuthorizationService, Graph token scoping); platform-admin escape hatch.
  - §6 Graph integration — what the SDK already gives us, the thin IGraphTenantClient, Polly v8 pipeline composition, the pagination / batch / delta rules, anti-rules forbidding hand-rolled OAuth / pagination / batch / delta / generic graph forwarders.
  - §7 Auth and secret management — BFF posture, OBO vs client_credentials, three-tier token cache with SemaphoreSlim coalescing, encrypted IRefreshTokenStore, secret-store layout, rotation cadence.
  - §8 Background processing — Hangfire / Service Bus / code-defined sagas with rationale per choice; per-tenant warmer scheduler; per-MSP fan-out bulkhead; outbox pattern; anti-rules forbidding handler-by-name dispatch.
  - §9 Persistence — schema-per-context layout, mandatory multi-tenant columns, typed-vs-jsonb decision rule, indexing, time-partitioning, soft-delete, no-startup-migration policy, optimistic concurrency.
  - §10 SignalR; §11 Observability (logs / metrics / traces + audit as a separate first-class data class); §12 Deployment topology and blue-green; §13 Failure modes and DR (RTO/RPO targets); §14 one-paragraph summary; §15 document conventions.
- Authored `PHASES.md` end to end (≈ 1,000 lines), pushed phase by phase:
  - Header + reading guide + cross-phase rules + phase index.
  - Phase 0 Foundations → Phase 12 GA / launch, each with the five sub-sections (scope, out-of-scope, entry, exit, verification) plus a per-phase risks block.
  - Cross-cutting tracks (docs / ADRs / security review / perf / a11y / i18n / telemetry hygiene) and the explicit "what we are deliberately not doing" list.
  - Append-only update protocol.
- Authored `REBUILD_PLAN.md` end to end (≈ 415 lines), pushed section by section:
  - §1 Executive summary (what / why / how / when / who).
  - §2 Full CIPP audit (repository inventory, backend findings, frontend findings, issue / discussion references, security posture, balanced acknowledgement of what CIPP does well).
  - §3 Rebuild thesis with each architectural choice mapped explicitly to the audit finding it addresses.
  - §4 Timeline view (phase progression, "minimum credible product" line at Phase 4 exit, gates, parallel tracks).
  - §5 Strategic risk register (architectural / operational / product / people).
  - §6 Success criteria (technical SLOs / product / operational / strategic outcomes).
  - §7 Reading order for new joiners.
  - §8 Append-only update protocol.

**Decisions logged this session:**
- Section-by-section commit cadence is the project's norm for planning docs going forward (the trail must be incremental and reviewable). Encoded in each doc's update protocol.
- Phase ordering and the 13-phase index are now formally adopted as the project plan; reordering requires an ADR.
- "Minimum credible product" is defined at Phase 4 exit (Identity domain end-to-end). This is the inflection where architectural investment becomes user-visible product.
- Standards count target: ≥ 30 in Phase 6, ≥ 95 more in Phase 7, full 187+ by Phase 11.

**Blocked / open questions:**
- ADR-0001 (tech stack) needs to land at the start of Phase 0 with the formal sign-off; the rationale lives in `README.md` and `CLAUDE.md` but the ADR makes it canonical for future deviations.
- The active-active multi-region story is deferred to a post-GA phase; the data architecture supports it but the operational burden isn't justified at launch.

**Next session should:**
1. Read `MEMORY.md` (this file).
2. Read `FEEDBACK.md` (the three foundational directives still apply: no Graph wrapper rebuild, cache as centerpiece, auth complexity is an artifact).
3. Open Phase 0 per `PHASES.md`. Concrete first PR: solution skeleton + nullable-enabled / implicit-usings / TreatWarningsAsErrors / LangVersion latest baseline + a green `dotnet build` and `dotnet test` (with empty test projects) on CI.
4. In parallel: ADR-0001 (tech stack) lands with the team's sign-off.
5. Update this file at end of session.

**Files touched this session:**
- `ARCHITECTURE.md` (created, 15 sections)
- `PHASES.md` (created, 13 phases + cross-cutting + close)
- `REBUILD_PLAN.md` (created, 8 sections)
- `MEMORY.md` (this update)

---

### 2026-04-29 — Initial planning workspace

**Goal:** Stand up the planning documents that govern the CIPP rebuild in C#/.NET.

**Done this session:**
- Researched `KelvinTegelaar/CIPP` (frontend) and `KelvinTegelaar/CIPP-API` (backend) end-to-end via four parallel research agents.
  - Frontend: Next.js 16 + React 19 + MUI 7, static export to Azure Static Web Apps, EasyAuth, ~1,448-line toolbar files, no real TypeScript adoption, no frontend tests, mixed form libs (Formik + react-hook-form), AGPL-3.0.
  - Backend: PowerShell 7.4 on Azure Functions v4, **533 `Invoke-*.ps1` HTTP-trigger files**, **187 standards files**, Durable Functions with strict version matching, Azure Table Storage for everything, refresh tokens mirrored into env vars, custom `CIPPSharp.dll` token cache shim, 15-20s documented cold start.
  - Pain points: 100+ tenants → 15 min BPA refresh, Issue #1806 (64 KB Table row limit on templates, unresolved since 2023), Issue #2883 (All-Tenants user list timeout), Discussion #4979 (60-tenant perf complaints), $50 bug bounty for delegated-admin tooling.
  - Inventory: 13 domains, ~300 HTTP endpoints + ~80 background functions = ~380 Azure Functions, mapped to file paths under `Modules/CIPPCore/Public/`.
- Authored `README.md` — project mission, capabilities, target tech stack, repository layout intent, planning-document index.
- Authored `CLAUDE.md` — 19 sections covering tech stack, domain/application/infrastructure rules, multi-tenancy, the **"it's just a Graph wrapper" rule** (§8b — do not reinvent what `Microsoft.Graph` SDK v5 + `Microsoft.Identity.Web` already do), and **Caching is a first-class architectural concern** (§8c — three-layer cache hierarchy, read-path rule, delta-query background refresh, anti-rules).

**Decisions logged this session:**
- Tech stack confirmed: C# 13 / .NET 10 LTS, ASP.NET Core 10, Blazor Web App with MudBlazor, MediatR, FluentValidation, Polly v8, Microsoft.Graph SDK v5, Microsoft.Identity.Web, EF Core 10 + PostgreSQL, Redis, Hangfire + Service Bus, OpenTelemetry, Bicep, Testcontainers + Playwright.
- License direction: Apache 2.0 for core, separate commercial agreement for hosted SaaS. AGPL is off the table because of the MSP fork-friction it causes on CIPP.
- Distribution model: managed multi-MSP SaaS first, container image for self-host as a secondary option. The fork-and-deploy model is gone.
- Persistence direction: PostgreSQL only for relational; Blob for archives; Redis for cache/rate-limit/SignalR backplane/Hangfire; Service Bus for fan-out work queues. **No Azure Tables, no Cosmos.**

**Blocked / open questions:**
- Multi-region story (single-region first, but customers will ask): defer to Phase 11.
- Whether to keep BPA endpoints for migration only or build a forward-compatible BPA wrapper around the new Standards engine: defer to Phase 10.

**Next session should:**
1. Read `FEEDBACK.md` (it now contains the user's three architectural directives from this session — these are foundational).
2. Author `ARCHITECTURE.md`, leading with: (a) the "Graph SDK leverage" thesis, (b) the cache hierarchy and read/write paths, (c) the bounded-context map, (d) the multi-MSP isolation model.
3. Author `PHASES.md` — 12 phases, each with scope + entry criteria + exit criteria + verification.
4. Author `REBUILD_PLAN.md` — the master strategic doc. Should embed the full audit findings, the rebuild thesis, and the high-level timeline.
5. Update this file at end of session.

**Files touched this session:**
- `README.md` (created)
- `CLAUDE.md` (created)
- `MEMORY.md` (created)
- `FEEDBACK.md` (created)
