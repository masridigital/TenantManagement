# MEMORY.md — Session State

> Update this file at the **end of every session**. The next session reads it first to know exactly where work stopped, what's in flight, and what's next.
>
> Format: keep the most recent session at the top. Older sessions roll down. Trim to last 10 sessions.

---

## Active state

- **Phase:** Phase 2 (Identity & Access Domain)
- **Branch:** `claude/analyze-cipp-repos-UzlQB`
- **Current goal:** Execute End-to-End cache and Graph sync mechanisms locally.
- **Next concrete task:** Start the Worker and Api locally using `dotnet run` (Docker dependency removed) to verify functionality.

---

## Session log

### 2026-04-29 — Entities, Context Accessor, and EF Migrations Setup

**Goal:** Implement the multi-MSP isolation logic and test Entity Framework migrations.

**Done this session:**
- Implemented `MspContextAccessor` in `TenantManagement.Api` and wired it into `Program.cs`. This extracts the `MspId` from user claims.
- Created `BaseEntity` abstract class in `TenantManagement.Domain` that strictly enforces `MspId`, `CreatedAtUtc`, `IsDeleted`, and `RowVersion` properties on all entities.
- Created `CustomerTenant` entity as a baseline testing structure.
- Updated `ApplicationDbContext` to iterate through all entities inheriting from `BaseEntity` via reflection, injecting an EF Core Global Query Filter that guarantees `e => e.MspId == _mspContextAccessor.MspId && !e.IsDeleted`.
- Successfully generated the first EF Core Migration (`InitialCreate`) showing the complete application of our schema structure and inherited `MspId` isolation.

**Decisions logged this session:**
- Applied the global query filter programmatically in `OnModelCreating` to prevent developers from accidentally forgetting to add it per configuration.
- CIPP feature parity requires robust data isolation natively at the query level, which has now been established.

**Blocked / open questions:**
- Docker daemon was not running during execution, so the migration has not been applied to a live database yet.

**Next session should:**
1. Start Docker to bring up Postgres, Redis, and Azurite containers.
2. Apply the `InitialCreate` and `AddTenantUsers` migrations.
3. Test the Background workers (`CustomerTenantSyncJob` and `TenantUserSyncJob`) via the Hangfire dashboard.
4. Continue scaffolding Phase 2 entities (Devices, GDAP mappings, App Registrations) following the exact same pattern.

**Files touched this session:**
- `TenantManagement.Domain/Entities/TenantUser.cs`
- `TenantManagement.Domain/Entities/TenantGroup.cs`
- `TenantManagement.Application/Common/Interfaces/IApplicationDbContext.cs`
- `TenantManagement.Infrastructure/Persistence/ApplicationDbContext.cs`
- `TenantManagement.Infrastructure/Persistence/Migrations/*` (AddTenantUsers)
- `TenantManagement.Worker/Jobs/TenantUserSyncJob.cs`
- `TenantManagement.Worker/Program.cs`
- `TenantManagement.Application/Users/Queries/GetTenantUsers/*`
- `TenantManagement.Api/Endpoints/UsersEndpoints.cs`
- `TenantManagement.Api/Program.cs`
- `MEMORY.md` (updated)

### 2026-04-29 — Base API, Database, and Background Jobs Scaffolding

**Goal:** Configure the foundational services inside the ASP.NET Core API and Background Worker projects.

**Done this session:**
- Installed `Microsoft.Identity.Web` and configured JWT Authentication and base Authorization policies in `TenantManagement.Api`.
- Removed the default weather forecast code from `Program.cs` and replaced it with a structured Minimal APIs configuration (mapped `/api/health`).
- Set up `ApplicationDbContext` in `TenantManagement.Infrastructure` using `Npgsql` for PostgreSQL, including an interface `IApplicationDbContext` for the Application layer to consume.
- Scaffolded `IMspContextAccessor` in the Application layer to prepare for multi-MSP global query filters.
- Installed `Hangfire` and `StackExchange.Redis` in `TenantManagement.Worker`, and configured it to use Redis as its storage provider.
- Created extension methods `AddApplicationServices` and `AddInfrastructureServices` for clean dependency injection, registering MediatR, FluentValidation, and EF Core.

**Decisions logged this session:**
- Used `Hangfire.Redis.StackExchange` for Hangfire storage as defined by the architecture.
- Scaffolded an empty `IMspContextAccessor` which will need to be populated from claims middleware.

**Blocked / open questions:**
- None.

**Next session should:**
1. Implement the `MspContextAccessor` middleware to extract the `MspId` from the authenticated principal.
2. Create a base `Entity` abstract class in the Domain layer that includes `MspId`, `CreatedAtUtc`, and `RowVersion` properties.
3. Configure the EF Core Global Query Filter for `MspId` in `ApplicationDbContext`.
4. Run the first EF Core Migration and verify it against the local Postgres container.

**Files touched this session:**
- `TenantManagement.Api/Program.cs`
- `TenantManagement.Api/appsettings.json`
- `TenantManagement.Worker/Program.cs`
- `TenantManagement.Worker/appsettings.json`
- `TenantManagement.Infrastructure/Persistence/ApplicationDbContext.cs`
- `TenantManagement.Infrastructure/DependencyInjection.cs`
- `TenantManagement.Application/DependencyInjection.cs`
- `TenantManagement.Application/Common/Interfaces/IApplicationDbContext.cs`
- `TenantManagement.Application/Common/Interfaces/IMspContextAccessor.cs`
- `MEMORY.md` (updated)

### 2026-04-29 — Completion of Planning Documents

**Goal:** Author the remaining planning documents to complete the Pre-Phase 0 goals.

**Done this session:**
- Read and internalized `FEEDBACK.md` directives (Graph wrapper rule, cache hierarchy, auth simplicity).
- Authored `ARCHITECTURE.md` defining the caching architecture, read/write paths, bounded-context map, and multi-MSP isolation model.
- Authored `PHASES.md` defining the 12 phases of the project implementation.
- Authored `REBUILD_PLAN.md` acting as the master strategic document containing audit findings, the rebuild thesis, and timeline.
- Updated `MEMORY.md` to transition the project state to Phase 0.

**Decisions logged this session:**
- Project officially transitions from Pre-Phase 0 (Planning) to Phase 0 (Foundation). All foundational planning documents are securely in place.

**Blocked / open questions:**
- None. Ready for implementation.

**Next session should:**
1. Configure the base API scaffolding with ASP.NET Core 10 Minimal APIs and `Microsoft.Identity.Web` in `TenantManagement.Api`.
2. Configure basic Entity Framework Core DbContext in `TenantManagement.Infrastructure`.
3. Set up Redis and Hangfire in the Worker project.

**Files touched this session:**
- `ARCHITECTURE.md` (created)
- `PHASES.md` (created)
- `REBUILD_PLAN.md` (created)
- `MEMORY.md` (updated)
- `docker-compose.yml` (created)
- `TenantManagement.sln` and all related `.csproj` project files (created)

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
