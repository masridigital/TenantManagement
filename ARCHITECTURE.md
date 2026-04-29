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
