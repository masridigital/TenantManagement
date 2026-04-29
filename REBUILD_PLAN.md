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
