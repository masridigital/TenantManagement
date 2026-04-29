# PHASES.md — Build Roadmap

> Read this **after** `README.md` (what), `ARCHITECTURE.md` (how it fits together), and `CLAUDE.md` (the standards). This is the plan: how we get from zero to feature parity, in what order, and how we know each chunk is done.
>
> Status: **draft** — phases land section by section. Each phase is its own commit so the plan can be reviewed and adjusted incrementally.

---

## 0. How to read this document

Each phase has the same five sub-sections:

1. **Scope.** What is in this phase — bullet-listed, no waffle. If it isn't listed, it isn't in this phase.
2. **Out of scope.** Things readers will assume are in this phase but aren't, and which phase owns them instead. Prevents scope creep and "but where does X go?" arguments.
3. **Entry criteria.** What must be true before the phase starts. Hard gates, not aspirations.
4. **Exit criteria.** What must be true to call the phase done. Each criterion is independently verifiable.
5. **Verification.** How we prove each exit criterion. Tests, demos, metrics, dashboards — concrete evidence.

### Rules across all phases

- **No phase ships behind a hidden feature flag** unless the flag is explicitly in scope. We don't accumulate dark code.
- **No phase imports work from a future phase.** If you find yourself touching Phase 7 code in Phase 3, stop and split the PR.
- **Every phase updates `MEMORY.md`** at the end. The next session reads it first.
- **Every phase that changes a coding standard updates `FEEDBACK.md`** and `CLAUDE.md`.
- **Coverage gates** apply to every phase touching `Domain` or `Application`: `>= 80%` line coverage on the touched code.
- **Performance budgets** apply to every phase that ships UI surface: p95 page render ≤ 500 ms over warm cache, p99 ≤ 1.5 s.

### What "done" means

A phase is done when **every** exit criterion is verified. Not "mostly done." Not "done except the tests." If a criterion is partial, the phase is still in flight and `MEMORY.md` says so. The cost of declaring a phase done while it isn't is paid by every subsequent phase that builds on it.

### Phase index

| # | Phase | Goal in one sentence | Approx. weeks |
| - | ----- | -------------------- | ------------- |
| 0 | **Foundations** | Solution skeleton, infra-as-code, CI/CD, observability, no business code | 2 |
| 1 | **Auth and tenancy** | OIDC sign-in, MSP context, customer-tenant authorization, refresh-token store | 3 |
| 2 | **Graph integration core** | `IGraphTenantClient`, Polly pipeline, batch handling, delta cursors, token cache | 3 |
| 3 | **Caching and projections** | L1/L2/L3, warmer scheduler, first projection (users), the read-path SLA | 3 |
| 4 | **Identity domain** | Users, groups, devices, sign-in logs, breach search; first end-to-end UI | 4 |
| 5 | **Tenants domain** | Onboarding saga, GDAP relationships, offboarding, alignment, all-tenants grids | 4 |
| 6 | **Standards engine** | Typed handlers, Report/Remediate/Alert modes, drift detection, templates store | 5 |
| 7 | **Endpoint Management + Exchange Online + Collaboration + Security** | Remaining customer-tenant domains | 8 |
| 8 | **Automation and integrations** | Scheduler UI, webhook receivers, PSA/RMM adapters | 4 |
| 9 | **Reports and analytics** | Domain analyser, license usage, inactive accounts, MFA, app consents | 3 |
| 10 | **BPA migration** | Legacy BPA support + one-way migration to Standards | 2 |
| 11 | **Hardening, scale, multi-region readiness** | Soak, perf, chaos, DR drills, multi-region story finalisation | 4 |
| 12 | **GA / launch** | Pricing, billing, public docs, status page, security disclosure programme | 3 |

Total: ≈ 48 engineering weeks elapsed (assumes 3-engineer team). Calendar runtime depends on team size; the **order** is non-negotiable because each phase's exit criteria are entry criteria for the next.

---

## Phase 0 — Foundations

**Goal:** A green CI on a deployable skeleton, with no business features, that we can put traffic on. Every subsequent phase relies on this being boring and reliable.

### Scope

- Solution layout matching `README.md` repository layout: `TenantManagement.{Domain,Application,Infrastructure,Graph,Standards,Api,Web,Worker,Migrations}` + tests projects.
- Coding-standard scaffolding: nullable enabled, implicit usings, `LangVersion latest`, `TreatWarningsAsErrors true`, `dotnet format` configured.
- Single `appsettings.{Development,Staging,Production}.json` with Key Vault references for every secret.
- ASP.NET Core 10 host with health endpoints (`/health/live`, `/health/ready`, `/health/startup`) returning the status framework only.
- EF Core 10 + Npgsql provider with an empty `AppDbContext` and a CI step that creates and tears down a Postgres database via Testcontainers.
- Redis client wired (`StackExchange.Redis`) with a smoke check at startup; no usage yet.
- Hangfire host wired with Redis storage; one heartbeat job to prove the dashboard works.
- Service Bus client wired with one health-check queue.
- OpenTelemetry SDK wired: traces, metrics, logs exported to App Insights (Azure) or OTLP collector (self-host); 100% sampling in dev/ci.
- Bicep modules for: Container Apps env, Postgres Flexible Server, Redis, Service Bus, Key Vault, Front Door, App Insights. Modules deploy to `dev`.
- GitHub Actions workflows: `pr.yml` (format check, build, unit tests, integration tests, security scan, OpenAPI diff), `deploy-dev.yml` (build container image, run migrator, deploy Web + Worker), `deploy-staging.yml` (manual approval gate).
- Container images: `Web`, `Worker`, `Migrator` build cleanly, all <300 MB compressed.
- One CODEOWNERS file. One pull-request template that points at `MEMORY.md`.
- `docs/architecture/`, `docs/ADRs/` directories created with a placeholder ADR template.

### Out of scope

- Authentication of any kind (Phase 1).
- Any reference to Microsoft Graph (Phase 2).
- Any business entity (Phase 4+).
- Multi-region (Phase 11).
- Self-host packaging (`docker-compose.yml` exists but is dev-only; the GA self-host artifact is Phase 12).

### Entry criteria

- Repository created, branch protections configured.
- Azure subscriptions for `dev` and `staging` available with a service principal for GitHub Actions deploys.
- The team has read `README.md`, `CLAUDE.md`, `ARCHITECTURE.md`.

### Exit criteria

1. `git clone && dotnet build` produces a green build with zero warnings.
2. `dotnet test` runs all (empty) test projects green.
3. `pr.yml` runs end-to-end on a trivial PR and is green.
4. `deploy-dev.yml` deploys Web + Worker to a Container Apps revision; both containers reach `/health/ready` = 200.
5. The Hangfire heartbeat job runs in `dev` and is visible in the dashboard.
6. App Insights shows traces and metrics from `dev` for synthetic requests.
7. A Bicep `what-if` against staging shows zero drift.
8. A new engineer can go from clean checkout to a running local stack (`docker compose up`) in < 15 min.
9. `MEMORY.md` updated; no `FEEDBACK.md` entries pending.

### Verification

- Live demo of points 1–6 from a clean checkout, on a recording for the project log.
- Onboarding-time stopwatch from a teammate not involved in scaffolding (point 8).
- A signed-off ADR-0001 ("Why .NET 10, Postgres, Redis, Service Bus, Hangfire, Container Apps") in `docs/ADRs/`.

### Risks

- **Container Apps cold-start regressions.** Mitigation: minimum-replica = 1 in dev/staging; production sets it from a measured warm-pool size in Phase 11.
- **Bicep drift.** Mitigation: every PR runs `bicep what-if` against staging; manual rejects on drift.
- **CI minutes blowout.** Mitigation: Testcontainers reuse + single shared Postgres image cache; budget 8 min per PR end-to-end.

---
