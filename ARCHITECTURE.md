# ARCHITECTURE.md — System Design

This document outlines the high-level architecture of the TenantManagement application, a clean-room rebuild of CIPP in C#/.NET.

---

## 1. The "Graph SDK Leverage" Thesis

CIPP was heavily built around reinventing Microsoft Graph proxying in PowerShell. The core thesis of this rebuild is to leverage the official `Microsoft.Graph` SDK v5 and `Microsoft.Identity.Web` to handle the heavy lifting of API interaction. 

- **Typed Models:** We use the natively provided typed entity models for every Graph resource.
- **Pagination & Throttling:** The rebuild uses `PageIterator<T>` for seamless pagination, throttling, and `Retry-After` handling.
- **Batching:** `BatchRequestContent` is used for batched operations, allowing precise inspection of per-subresponse statuses (eliminating silent inner 429 losses).
- **Delta Queries:** We heavily utilize `Delta()` extensions on supported resources (`users`, `groups`, `devices`, etc.) to minimize full-sync data transfers.
- **Auth Primitives:** We rely on `Microsoft.Identity.Web` for OBO, refresh, certificate, MSI, and confidential-client flows instead of hand-rolling SAM logic.

Our value-add is multi-tenant orchestration, the Standards engine, caching, and the UI—not Graph proxying.

---

## 2. Cache Hierarchy and Read/Write Paths

A significant architectural shift from CIPP is the introduction of a robust, real backend capable of stateful caching. This eliminates the "paginate-Graph-on-page-load" bottleneck.

### Cache Hierarchy

1. **L1: In-Memory Cache** (`IMemoryCache`): Hot read-through cache per API/Web node, lasting seconds to minutes. Used for repeated requests within a single instance.
2. **L2: Distributed Cache** (Redis): Cluster-wide cache lasting minutes to hours. Used for tenant lists, group lists, role definitions, and entity caching.
3. **L3: Postgres Projections**: Durable, queryable cache of cross-tenant data. Refreshed via scheduled background jobs using Graph delta queries.
4. **Blob Storage**: Used for standards baseline snapshots, audit archives, and large export artifacts.

### Read Path

A page load **never** triggers a synchronous Graph paginate-until-done operation. 
1. Check **L1** (return if hit).
2. Check **L2** (return if hit, populate L1).
3. Check **L3** (return if fresh, populate L1+L2).
4. **Background Refresher**: If L3 is missing (e.g., a new tenant), allow a bounded one-page Graph fetch and trigger a background worker to populate L3.

### Write Path

When mutating data:
1. Issue the Graph call via `IGraphTenantClient`.
2. Update the **L3** (Postgres) projection in the same transaction as the audit log.
3. Invalidate relevant **L2** keys.
4. Push a SignalR message to connected clients to refresh **L1** and UI state.

---

## 3. Bounded-Context Map

The system is organized around 13 core bounded contexts, mirroring the feature set:

1. **Identity & Access:** Users, groups, devices, app registrations, GDAP, risky users.
2. **Endpoint Management:** Intune devices, policies, autopilot, compliance, scripts.
3. **Email / Exchange:** Mailboxes, transport rules, connectors, spam/phishing filters.
4. **Collaboration:** Teams, SharePoint, OneDrive provisioning.
5. **Security:** Alerts, Defender state, secure score, audit logs.
6. **Tenants:** Onboarding, offboarding, alignment, domain health.
7. **Standards Engine:** `IStandardHandler` registry, per-tenant settings, drift detection.
8. **Templates:** Configurations for CA, Intune, transport rules, etc.
9. **Reports & Analytics:** License usage, MFA status, app consents.
10. **Automation / Scheduler:** Recurring jobs, PSA integrations, webhooks.
11. **Best Practice Analyzer (BPA):** Legacy support and migration path to Standards.
12. **Admin / Settings:** Platform configuration, GDAP role mapping, branding.
13. **Observability:** Audit trails, activity feeds, telemetry.

---

## 4. Multi-MSP Isolation Model

TenantManagement operates as a managed multi-MSP SaaS. Isolation is critical and enforced structurally.

### Two Layers of Tenancy

1. **MSP Tenant:** The paying customer. Identified by `MspId`.
2. **Customer Tenant:** The M365 tenant being managed.

### Isolation Guarantees

- **Global Query Filters:** Every EF Core entity has an `MspId`. `e => e.MspId == _mspContext.MspId` is applied globally.
- **Context Injection:** `MspContextAccessor` guarantees the `MspId` is automatically attached based on the authenticated principal.
- **Customer Tenant Validation:** `CustomerTenantContext` validates the requested M365 tenant against the MSP's active GDAP relationships before any Graph API call is made.
- **Auth Policies:** Explicit `.RequireAuthorization("Policy.Name")` protects every API endpoint. Cross-MSP access is structurally impossible.
