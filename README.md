# TenantManagement

A ground-up, modern C#/.NET rebuild of the CIPP (CyberDrain Improved Partner Portal) concept: a multi-tenant Microsoft 365 management portal for Managed Service Providers (MSPs).

> **Status:** Pre-implementation. This repository contains the analysis of the existing CIPP (`KelvinTegelaar/CIPP` + `KelvinTegelaar/CIPP-API`) product, the rationale for rebuilding, and the architectural and phased plan for the replacement.

---

## Why this exists

CIPP is the de-facto open-source MSP M365 portal, but it ships with structural problems that cap its scale, raise its operating cost, and slow its iteration:

- **PowerShell-on-Azure-Functions backend** — cold starts of 15-20 seconds are documented in the project's own FAQ; throughput is bounded by per-runspace module loads.
- **533 individual `Invoke-*.ps1` HTTP-trigger files** with no controller registry, no static typing, no DTO validation, and per-file error handling.
- **Azure Table Storage for everything**, including templates that exceed the 64 KB row limit (open since 2023, unresolved).
- **187 standards** are each their own PowerShell file with bespoke remediation logic — no compile-time guarantees that a standard parses or runs.
- **Durable Functions with strict version matching** that fails hard on mid-deploy state, requiring a user-facing "Clear Durable Queue" maintenance UI.
- **Self-host-by-fork** distribution model where every MSP forks the repo and pulls upstream — a documented source of breakage on every major release.
- **Refresh tokens mirrored into process environment variables** — broad blast radius if any code path leaks env.
- **No real test suite** on the frontend; partial PowerShell tests on the backend.
- **AGPL-3.0** plus a **$50 bug-bounty** program for a tool with delegated admin into every customer's M365 — a posture that does not match the blast radius.

TenantManagement is a clean-room rebuild that keeps the feature surface (~300 endpoints, 13 domains, the standards/drift/scheduler model) and replaces the runtime, data layer, distribution model, and security posture.

---

## Core capabilities (target feature parity)

Organised into 13 domains, mapping 1:1 to the CIPP feature set:

1. **Identity & Access** — users (CRUD/bulk/MFA), groups, devices, app registrations, GDAP/JIT, risky users, sign-in logs, breach search.
2. **Endpoint Management (Intune)** — devices, policies, autopilot, applications, assignment filters, compliance, BitLocker/LAPS recovery, scripts.
3. **Email / Exchange Online** — mailboxes, permissions, rules, transport rules, connectors, anti-spam/phishing/malware filters, safe links/attachments, quarantine, message trace, mailbox restore.
4. **Teams / SharePoint / OneDrive** — sites, voice, activity, OneDrive provisioning, sharing settings.
5. **Security** — alerts/incidents, Defender state/TVM, secure score, BEC check & remediate, tenant allow/block, audit log search, named locations.
6. **Tenants** — onboarding (GDAP invites, role mapping, SAM bootstrap), offboarding, alignment/drift, all-tenant BPA / compliance / domain health.
7. **Standards engine** — typed `IStandardHandler` registry, per-tenant settings, Report / Remediate / Alert modes, drift detection.
8. **Templates** — CA, Intune, transport rule, group, app, BPA, standards, spam/connection/safe-links, JIT.
9. **Reports & Analytics** — domain analyser, license usage, inactive accounts, MFA, app consents, OAuth apps.
10. **Automation / Scheduler** — recurring jobs, webhooks, alert configurations, PSA integrations (Halo, NinjaOne, Pax8, Hudu, IT Glue, Datto).
11. **Best Practice Analyzer (BPA)** — legacy support + migration off to Standards.
12. **Admin / Settings** — backend health, users & roles, partner webhooks, branding, integrations, app permissions, GDAP role mapping, custom data.
13. **Logging / Observability** — activity feed, audit trail, scheduled item history, distributed tracing.

---

## Tech stack

| Layer                     | Choice                                                                |
| ------------------------- | --------------------------------------------------------------------- |
| Language                  | **C# 13** on **.NET 10 LTS**                                          |
| Backend                   | **ASP.NET Core 10** (Minimal API endpoint groups + Controllers where MVC fits better) |
| Frontend                  | **Blazor Web App (.NET 10)** — Interactive Server with prerender, optional WebAssembly islands |
| Frontend UI kit           | **MudBlazor** for the data-heavy admin surface                        |
| Auth                      | **Microsoft.Identity.Web** (OIDC + on-behalf-of for Graph), BFF posture — tokens never leave the server |
| Microsoft Graph           | **Microsoft.Graph SDK v5** + custom typed `IGraphTenantClient` factory with per-tenant rate limiting |
| Validation                | **FluentValidation**                                                  |
| Mediation                 | **MediatR** (CQRS handlers)                                           |
| Real-time                 | **SignalR** (replaces frontend polling)                               |
| Persistence               | **PostgreSQL 17** + **EF Core 10** (primary store: tenants, templates, jobs, audit, drift baselines) |
| Cache & rate limiting     | **Redis 7** via `StackExchange.Redis` + `RedisRateLimiting`           |
| Background jobs           | **Hangfire** (with Redis storage) for cron + ad-hoc, **Azure Service Bus** for fan-out work queues |
| Secret store              | **Azure Key Vault** + ASP.NET Core **DataProtection** for at-rest encryption of customer refresh tokens (never written to env) |
| Resilience                | **Polly v8** pipelines (retry + circuit breaker + timeout + bulkhead) |
| Observability             | **OpenTelemetry** → Application Insights / Grafana Cloud (logs, traces, metrics) |
| Infrastructure-as-Code    | **Bicep** modules (App Service / Container Apps, Postgres Flexible Server, Service Bus, Key Vault, Front Door) |
| CI/CD                     | **GitHub Actions** with environment-gated promotion (dev → staging → prod), Container Apps blue/green |
| Tests                     | **xUnit**, **FluentAssertions**, **bUnit** (Blazor), **Playwright** (E2E), **Testcontainers** (integration) |
| Codegen / API contracts   | **NSwag** generates a typed TS client (for any third-party integrators) and the server-side OpenAPI document |

Distribution model: **single managed multi-MSP SaaS** with strict logical isolation per MSP, plus a **self-host container image** for MSPs that require sovereignty. The fork-and-deploy model is gone.

---

## Repository layout

This repository is currently the **planning workspace** for the rebuild. Once Phase 0 begins, the layout will be:

```
src/
  TenantManagement.Domain/             # Aggregates, entities, value objects, domain events
  TenantManagement.Application/        # MediatR handlers, validators, DTOs, ports (interfaces)
  TenantManagement.Infrastructure/     # EF Core, Redis, Service Bus, Key Vault adapters
  TenantManagement.Graph/              # IGraphTenantClient factory, throttling, batch
  TenantManagement.Standards/          # Standards engine + per-standard handlers
  TenantManagement.Api/                # ASP.NET Core 10 Web API (endpoint groups)
  TenantManagement.Web/                # Blazor Web App (UI)
  TenantManagement.Worker/             # Hangfire host + Service Bus consumers
  TenantManagement.Migrations/         # CIPP-import and BPA-to-Standards migration tools
tests/
  TenantManagement.UnitTests/
  TenantManagement.IntegrationTests/   # Testcontainers (Postgres, Redis, Azurite)
  TenantManagement.E2E/                # Playwright
infra/
  bicep/
docs/
  ADRs/                                # Architecture Decision Records
```

---

## Planning documents

The rebuild is governed by these living documents in repo root:

| File                  | Purpose                                                                  |
| --------------------- | ------------------------------------------------------------------------ |
| `README.md`           | This file — what the project is.                                         |
| `CLAUDE.md`           | Coding standards, patterns, and conventions for any AI assistant working on the codebase. |
| `ARCHITECTURE.md`     | System diagram, data flow, bounded contexts, integration points.         |
| `PHASES.md`           | Roadmap: every phase, scope, exit criteria, verification.                |
| `MEMORY.md`           | Session state tracker — where work stopped, what's next.                 |
| `FEEDBACK.md`         | Log of corrections and lessons learnt; consulted at the start of every session. |
| `REBUILD_PLAN.md`     | The master strategic document: full audit findings + the rebuild thesis. |

---

## Getting started

Until Phase 0 lands, "getting started" means: read `REBUILD_PLAN.md` end-to-end, then `ARCHITECTURE.md`, then `PHASES.md`, then `CLAUDE.md`. Once Phase 0 ships, this section will be replaced with `dotnet restore` + `docker compose up` instructions.

## License

To be decided at Phase 0 — likely **Apache 2.0** (permissive, contributor-friendly) for the core, with a separate **commercial agreement** for the hosted SaaS. AGPL is explicitly off the table given the MSP fork-friction it has caused on CIPP.

## Acknowledgements

The feature surface is informed by [KelvinTegelaar/CIPP](https://github.com/KelvinTegelaar/CIPP) and [KelvinTegelaar/CIPP-API](https://github.com/KelvinTegelaar/CIPP-API) (AGPL-3.0). No CIPP source code is reused; the rebuild is clean-room, working from the public feature documentation and Microsoft Graph API contracts directly.
