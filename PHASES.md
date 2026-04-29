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
