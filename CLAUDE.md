# CLAUDE.md — Coding Standards & Patterns

> Read this at the start of every session. It is the project's style guide. When in doubt, prefer **explicit over clever**, **boring over novel**, and **typed over dynamic**.

---

## 1. Project rules of engagement

1. **Read `MEMORY.md` first.** It tells you what was last touched, what's in-flight, and what's next.
2. **Read `FEEDBACK.md` second.** Past corrections live there — do not repeat them.
3. **Update `MEMORY.md` at the end of every session.** Note files touched, decisions made, and the next concrete task.
4. **Append to `FEEDBACK.md` whenever a standard changes** or a mistake is corrected.
5. **Cross-reference `PHASES.md` for scope.** Do not pull work from a future phase into the current one.
6. **No CIPP source code may be copied.** The rebuild is clean-room. You may study Microsoft Graph contracts and CIPP feature documentation, but never paste PowerShell into C#.

---

## 2. Tech stack — non-negotiable

- **C# 13 / .NET 10 LTS**. No multi-targeting until we have a reason.
- **Nullable reference types: enabled** project-wide (`<Nullable>enable</Nullable>`).
- **Implicit usings: enabled**. Treat warnings as errors in CI (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- **`LangVersion: latest`**, but no preview features in `main`.
- **ASP.NET Core 10** Minimal APIs grouped by endpoint groups; MVC controllers only when model binding or filters genuinely justify them.
- **Blazor Web App** — Interactive Server by default, opt-in WebAssembly per page when the workload is heavy and offline-friendly.
- **EF Core 10** with PostgreSQL via Npgsql provider; migrations checked in; no auto-migrate on startup in production.
- **MediatR** for CQRS handler dispatch. **FluentValidation** for input validation, wired via `IPipelineBehavior`.
- **Polly v8 pipelines** — never raw `try/catch` for transient failures.
- **Microsoft.Graph SDK v5** behind our own `IGraphTenantClient` abstraction; never inject `GraphServiceClient` into application code directly.
- **OpenTelemetry** for traces/metrics/logs; never `Console.WriteLine` outside `Program.cs`.

---

## 3. Solution & project layout

- One `*.sln` at repo root.
- Project naming: `TenantManagement.<Layer>` — see `README.md` for the canonical list.
- Folder-per-bounded-context inside `Application` and `Domain` projects (e.g. `Application/Identity/Users/Commands/CreateUser`).
- One file per public type. Filename matches type name.
- File-scoped namespaces only.

---

## 4. Domain layer rules

- Domain types are **POCOs with private setters**. No EF attributes. Configuration lives in `Infrastructure/Persistence/Configurations/<Entity>Configuration.cs` using `IEntityTypeConfiguration<T>`.
- **Aggregates** are explicit. An aggregate root has a `private readonly List<TChild>` and exposes `IReadOnlyCollection<TChild>`.
- **Value objects** are `record` types with validation in the primary constructor.
- **Domain events** implement `INotification` (MediatR) and are dispatched from the aggregate via a `_domainEvents` list, drained by the `DbContext.SaveChangesAsync` interceptor.
- **No service-locator pattern**. No `IServiceProvider` injection into domain. No static singletons.
- **No `async void`** anywhere — including event handlers.

---

## 5. Application layer rules

- Every external entry point is a **MediatR command or query**:
  - Commands: `record CreateUserCommand(...) : IRequest<Result<UserId>>`.
  - Queries: `record GetUserQuery(UserId Id) : IRequest<UserDto>`.
- One handler per request type, in the same folder as the request.
- **`Result<T>`** (custom or via FluentResults) for expected failures. Throw only for *unexpected* failures.
- Validators sit beside handlers and are auto-wired by FluentValidation's pipeline behavior.
- DTOs are `record` types in the same feature folder. **Never expose domain entities across the API boundary.**
- Mapping: prefer hand-written `ToDto()` extension methods over Mapster/AutoMapper unless a feature has >10 mappings.

---

## 6. Multi-tenancy is a first-class concern

This is an MSP product managing many M365 tenants — **two layers of tenancy**:

- **MSP tenant** (the customer of TenantManagement, i.e. our paying user). Carried via the authenticated user's `MspId` claim.
- **Customer tenant** (the M365 tenant the MSP is acting on). Carried explicitly per request.

Rules:

1. **Every query and command except platform admin operations must include an MspId**, automatically attached by `MspContextAccessor` from the auth principal.
2. **EF Core query filters** apply `e => e.MspId == _mspContext.MspId` globally. No raw SQL bypasses these without an explicit `IgnoreQueryFilters()` call and a code review.
3. **Customer tenant context** is carried in `CustomerTenantContext` (scoped DI) and validated against the MSP's GDAP relationships before any Graph call.
4. **Cross-MSP access is impossible** by construction — the EF filter is a defense-in-depth layer; the auth policy is the primary gate.

---

## 7. Graph access — typed, throttled, observable

- Application code calls `IGraphTenantClient` (one instance scoped per `(MspId, CustomerTenantId)`), **not** `GraphServiceClient` directly.
- Token acquisition is centralised in `IGraphTokenProvider`. Refresh tokens are read from `IRefreshTokenStore` (encrypted at rest via `IDataProtectionProvider`); access tokens are cached in Redis with a per-key `SemaphoreSlim`-equivalent.
- **Tokens never touch environment variables.** Ever. If you see one being written there, it's a bug — file an issue.
- Every Graph call is wrapped by a Polly v8 pipeline:
  - Retry on `429` honoring `Retry-After` (capped at 60s, 5 attempts).
  - Retry on transient `5xx` with jittered exponential backoff.
  - Per-customer-tenant rate limiter (token bucket, Redis-backed) so fan-out doesn't trip Graph's per-tenant quota.
  - Bulkhead per MSP so one noisy MSP doesn't starve others.
- For multi-tenant fan-out: use the `IGraphFanOutScheduler` which respects the bulkhead and reports progress to SignalR.
- `$batch` requests must inspect each subresponse's status; **a 200 outer response with inner 429s is a failure**, not a success.

---

## 8. Standards engine rules

- One handler per standard: `class AntiPhishingPolicyStandard : IStandardHandler`.
- Each handler is decorated with `[Standard("AntiPhishingPolicy", Category = StandardCategory.Email)]` and registered automatically via assembly scanning.
- Three modes per handler: `ReportAsync`, `RemediateAsync`, `AlertAsync`. All are idempotent.
- **No remediation handler may run without a successful prior `ReportAsync` in the same orchestration** — enforced in the orchestrator, not in each handler.
- Per-standard settings are typed: `record AntiPhishingPolicySettings(bool EnableImpersonationProtection, ...)` deserialized from the template JSON.
- Drift detection diffs `ReportAsync` output against a stored baseline blob (per tenant + standard); the diff format is JSON Patch.

---

## 8b. The "it's just a Graph wrapper" rule

CIPP is, structurally, a giant `if/else` over Microsoft Graph endpoints written in PowerShell. **Most of its auth, pagination, retry, and batch logic is reinventing what `Microsoft.Graph` SDK v5 + `Microsoft.Identity.Web` already do natively.** Concrete consequences:

- **Do not hand-roll Graph pagination.** Use `PageIterator<T>` from `Microsoft.Graph` (or the SDK's typed `IAsyncEnumerable<T>` extensions). It already handles `@odata.nextLink`, throttling, and `Retry-After`.
- **Do not hand-roll OAuth flows.** `Microsoft.Identity.Web` does OBO, refresh, certificate auth, MSI, and confidential-client. The SAM "wrapper" in CIPP exists because PowerShell didn't have these primitives — we do.
- **Do not hand-roll Graph batching.** Use the SDK's `BatchRequestContent` and inspect every subresponse. The SDK already exposes inner status codes (CIPP's PowerShell helpers silently lose 429s in batches).
- **Do not hand-roll delta queries.** Use the SDK's `Delta()` extension on `users`, `groups`, `devices`, `directoryObjects`, etc. — these return a `deltaToken` we persist per-tenant per-resource and replay on the next sync.
- If we're writing more than 50 lines of Graph plumbing for a feature, **stop** and check whether the SDK already does it.

The rebuild's value-add is **not** the Graph proxying; it's caching, multi-tenant orchestration, the Standards engine, the UX, and the security posture around delegated admin. We must not waste budget rebuilding what Microsoft ships.

---

## 8c. Caching is a first-class architectural concern

CIPP cannot cache effectively because there is **no real backend**: the JS frontend hydrates by calling a PowerShell Function App, which has no shared in-process state, can only stash data in Azure Tables/Blobs (with strict size limits and no TTL semantics), and cannot reliably invalidate or pre-warm anything. The user-facing symptom is the well-documented choice between "page hit → paginate Graph until done → block render until everything's loaded" (works but timeouts at scale) or "load each page on demand with no aggressive cache" (fast first paint, slow steady-state).

**A real .NET backend changes the equation entirely. Apply the rules below.**

### Cache hierarchy

| Layer | Tech | Lifetime | Used for |
| ----- | ---- | -------- | -------- |
| **L1** | `IMemoryCache` per Web/API node | seconds–minutes | Hot read-through for repeated requests within a single instance (e.g. tenant list per signed-in user). |
| **L2** | **Redis** via `StackExchange.Redis` + `Microsoft.Extensions.Caching.StackExchangeRedis` | minutes–hours | Cross-node distributed cache: tenant lists, user lists, group lists, license SKUs, role definitions, recently fetched device records. |
| **L3** | **PostgreSQL** projection tables | hours–days, refreshed on schedule and via deltas | Durable, queryable cache of "every user across every customer tenant", "every Intune policy", "every CA policy". This is what powers cross-tenant grids without live Graph hits. |
| **Blob** | Azure Blob | indefinite | Audit log archives, standards baselines, large export artifacts. |

### Read-path rule

A page load **never** triggers a synchronous Graph paginate-until-done. The order of resolution is always:

1. **L1** (`IMemoryCache`) — return if hot.
2. **L2** (Redis) — return if present; populate L1.
3. **L3** (Postgres projection) — return if fresh; populate L1+L2.
4. **Background refresh job kicks Graph** — never the request thread. The page renders from L3 with a "data refreshed N minutes ago" footer.

If L3 is empty for a (Msp, CustomerTenant, Resource) tuple — i.e. the tenant was just onboarded — the request is allowed to fall through to a **bounded** Graph fetch (one page, server-paginated by the API) while a background warmer kicks off to populate the rest.

### Write-path rule

When the API writes through to Graph (e.g. `CreateUser`), it:
1. Issues the Graph call.
2. Updates L3 (Postgres) with the new entity in the same transaction as the audit log entry.
3. Invalidates the relevant L2 keys.
4. Pushes a SignalR message so connected clients refresh their L1.

### Background refresh

- **Per-tenant background workers** run on a Hangfire schedule (default: every 15 min for hot resources, hourly for warm, daily for cold).
- Workers use **Microsoft Graph delta queries** wherever supported (`users`, `groups`, `directoryObjects`, `devices`, `messages`, etc.). The `deltaToken` is persisted in `GraphDeltaCursors` keyed by `(MspId, CustomerTenantId, ResourceType)`.
- Initial sync is a single full-page walk; every subsequent sync is a delta pull that returns only changes.
- A failed delta cursor (token expired) triggers a fresh full sync and a metric increment — alarms fire if delta failure rate exceeds 5%.

### Cache key conventions

```
tenant:{mspId}:{customerTenantId}:users:list                  # L2 list cache
tenant:{mspId}:{customerTenantId}:users:{userId}              # L2 entity cache
tenant:{mspId}:{customerTenantId}:users:delta-cursor          # delta token (Redis only as a hot mirror; canonical in Postgres)
mspscope:{mspId}:tenants:list                                 # L2 MSP-level cache
```

### Anti-rules (do not do this)

- ❌ **No request-time pagination of Graph.** If a handler walks `nextLink` on the request thread, that is a bug.
- ❌ **No "cache for a few minutes and call Graph anyway."** Either it's served from cache, or it triggers a delta refresh that updates the cache. There is no third path.
- ❌ **No per-request token re-acquisition.** Tokens are cached in Redis with `SemaphoreSlim` token-fetch coalescing.
- ❌ **No "store the JSON blob and re-parse it on every read."** Projections are typed columns in Postgres; the Graph response is not the persistence shape.

This single rule eliminates the largest class of CIPP's user-facing performance complaints (`#1064`, `#2883`, `#75`, Discussion `#4979`).

---

## 9. Persistence rules

- **PostgreSQL only** for relational data. No Azure Table Storage, no Cosmos, no per-feature DB sprawl.
- **Blob storage** for: standards baseline snapshots, audit log archives, large export artifacts.
- **Redis** for: distributed cache, rate limiter state, SignalR backplane, Hangfire storage.
- **Service Bus** for: fan-out work queues with sessions for ordering when needed.
- Every table has `MspId`, `CreatedAtUtc`, `CreatedById`, `UpdatedAtUtc`, `UpdatedById`, `RowVersion` (concurrency token).
- Soft-delete via `IsDeleted` + global query filter; hard-delete only for ephemeral job records older than 90 days.
- **No EF migrations applied on app startup** in any non-dev environment. Migrations run as a dedicated CI step against the target database before app rollout.

---

## 10. Authorization rules

- Auth is OIDC + Microsoft.Identity.Web at the MSP level. Customer tenant access is by GDAP relationship lookup, not by claim.
- Roles: `Readonly`, `Editor`, `Admin`, `SuperAdmin` mirroring CIPP's model — but expressed as **policies**, not claims, e.g.:
  ```csharp
  options.AddPolicy("Identity.User.ReadWrite", p => p.RequireRole("Editor", "Admin", "SuperAdmin"));
  ```
- Every endpoint has an explicit `.RequireAuthorization("Policy.Name")`. **Never** rely on a default-deny without an explicit policy.
- Custom roles add per-tenant scoping; enforced in `ICustomerTenantAuthorizationService.AssertAccess(customerTenantId)`.
- Audit every authorization denial — they are signal, not noise.

---

## 11. Validation & error handling

- All inbound DTOs go through FluentValidation **before** the handler runs. Validation failures return RFC 7807 `ValidationProblemDetails`.
- Domain rule violations throw `DomainException`, mapped by middleware to `Problem` 422.
- External API failures (Graph, partner API) return `Result.Failure(...)` from the handler, mapped to `Problem` with appropriate status.
- **Do not swallow exceptions.** If you catch, you must log with structured properties and either rethrow or return a `Result.Failure` with the cause.

---

## 12. Logging & observability

- Use `ILogger<T>` with **structured logging only**:
  - Good: `_logger.LogInformation("User {UserId} created in tenant {TenantId}", userId, tenantId);`
  - Bad: `_logger.LogInformation($"User {userId} created in tenant {tenantId}");`
- Define event ids in a per-area static class (e.g. `LogEvents.Identity.UserCreated`).
- Every Graph call emits an OpenTelemetry span with `customer.tenant.id`, `graph.endpoint`, `http.status_code`, `retry.count` attributes.
- **Do not log secrets, refresh tokens, access tokens, or full request bodies of write operations.** Bodies of audit-sensitive operations may be logged with PII redaction by `IPiiRedactor`.
- Correlation: every request gets a `TraceId` propagated through Service Bus messages and Hangfire jobs.

---

## 13. Background jobs rules

- **Hangfire** for cron + ad-hoc fire-and-forget; **Service Bus** for tenant fan-out work and inter-service messaging.
- Every job is **idempotent**. If you can't make it idempotent, it's not ready for production.
- Long-running orchestrations are modelled as a saga in code, not as Durable Functions. Sagas persist state to Postgres; resumption uses the saga id.
- **No silent retries.** Every retry is logged with attempt number; persistent failures move to a dead-letter queue and raise an alert.

---

## 14. Frontend (Blazor) rules

- **One component per file**, file name matches component class name.
- Use `@code` blocks sparingly; promote logic past ~30 lines into a `partial class` code-behind.
- State: prefer **query-string-driven state** for shareable views (selected tenant, table filters); **scoped services** for per-tab state; avoid Fluxor unless a feature really needs time-travel debug.
- Tables: **server-side pagination, filtering, and sorting** — never client-side aggregation across tenants.
- Real-time: SignalR hub per domain (`/hubs/identity`, `/hubs/scheduler`); component subscribes/unsubscribes in `OnInitializedAsync`/`Dispose`.
- Forms: typed `record` model + FluentValidation validator + `<EditForm>`. No untyped dictionaries.
- Theming: every colour, spacing, and font-size goes through CSS custom properties keyed off a `ThemeConfig` record loaded per MSP.

---

## 15. Testing rules

- **Unit tests**: pure logic, no I/O. xUnit + FluentAssertions. Co-located in `tests/TenantManagement.UnitTests/<Project>/<Folder>`.
- **Integration tests**: Testcontainers spins up Postgres + Redis + Azurite per test class. Use `WebApplicationFactory<Program>` for the API, `BunitTestContext` for Blazor.
- **E2E tests**: Playwright against a running stack (`docker compose up`). Smoke-only on PRs; full suite nightly.
- **Coverage gate**: `>= 80%` line coverage on `Domain` and `Application`; lower bars elsewhere are acceptable but tracked.
- Never write a test that uses `Thread.Sleep`. Use `TaskCompletionSource` or test schedulers.

---

## 16. CI / PR rules

- Every PR runs: format check (`dotnet format --verify-no-changes`), build, unit tests, integration tests, security scan (`dotnet list package --vulnerable --include-transitive`), and OpenAPI diff.
- **Every PR updates `MEMORY.md`** with the current state.
- **Every breaking change to a coding standard updates `FEEDBACK.md`** and `CLAUDE.md`.
- Conventional commits: `feat(domain):`, `fix(api):`, `docs(architecture):`, `chore(infra):`.
- PRs >800 LOC are reviewed but the author should expect to split next time. PRs >2000 LOC must be split.

---

## 17. Security baselines

- All secrets via Key Vault references (`@Microsoft.KeyVault(...)`), never in `appsettings.*.json`.
- All Postgres connections require TLS; rejecting plaintext is enforced by Azure Database for PostgreSQL.
- All HTTP responses get `Strict-Transport-Security`, `Content-Security-Policy` (no inline scripts), `Referrer-Policy: same-origin`.
- Input that becomes HTML is sanitized (`HtmlSanitizer`) — never rendered raw.
- Every external integration (PSA, webhook receiver) verifies signatures. No unauthenticated webhooks.
- The bug-bounty target is **commercial-grade** — at minimum a published `SECURITY.md` and a coordinated-disclosure process. The CIPP $50/swag posture is a reference for what NOT to do.

---

## 18. What NOT to do (lessons from CIPP)

- ❌ Do not write a 1,400-line component file. Split aggressively.
- ❌ Do not store templates in Azure Tables (64 KB cap will bite).
- ❌ Do not mirror secrets into environment variables.
- ❌ Do not invent a custom queue-dispatcher that does `& $cmdletName @args` — never have a "run any handler by name from a queue payload" path.
- ❌ Do not poll. Use SignalR.
- ❌ Do not aggregate cross-tenant data on the client. Push pagination to the API.
- ❌ Do not strict-version-match orchestrations such that a deploy mid-flight nukes state. Sagas survive deploys; durable enums are versioned.
- ❌ Do not expose 533 endpoints with no controller registry. Group, validate, document.

---

## 19. When you change a standard

If during a session you find a rule in this file that was wrong or has been superseded:
1. Edit `CLAUDE.md` with the corrected rule.
2. Append a `FEEDBACK.md` entry: date, what changed, why.
3. Mention it in the PR description.
