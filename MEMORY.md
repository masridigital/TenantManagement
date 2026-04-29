# MEMORY.md — Session State

> Update this file at the **end of every session**. The next session reads it first to know exactly where work stopped, what's in flight, and what's next.
>
> Format: keep the most recent session at the top. Older sessions roll down. Trim to last 10 sessions.

---

## Active state

- **Phase:** Pre-Phase 0 (Planning)
- **Branch:** `claude/analyze-cipp-repos-UzlQB`
- **Current goal:** Land the seven planning documents (README, CLAUDE, ARCHITECTURE, PHASES, MEMORY, FEEDBACK, REBUILD_PLAN) so Phase 0 can begin from a complete brief.
- **Next concrete task:** Author `ARCHITECTURE.md`, then `PHASES.md`, then `REBUILD_PLAN.md`.

---

## Session log

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
