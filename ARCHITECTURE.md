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
