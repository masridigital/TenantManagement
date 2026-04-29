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
