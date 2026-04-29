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
