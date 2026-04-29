# PHASES.md — Implementation Roadmap

This document outlines the 12-phase development plan for the TenantManagement rebuild. Each phase has a clear scope, entry criteria, exit criteria, and verification steps to ensure systematic progression.

---

## Phase 0: Foundation
**Scope:** Set up the solution structure, infrastructure-as-code, core persistence, cache hierarchy, and authentication layer.
- **Entry Criteria:** Planning documents (ARCHITECTURE, CLAUDE, etc.) approved.
- **Exit Criteria:** `docker compose up` runs Postgres, Redis, and Azurite. API accepts OIDC token and can read/write to database with multi-MSP context.
- **Verification:** Integration tests confirm EF Core global query filters isolate `MspId` effectively.

## Phase 1: Graph Plumbing & Tenant Sync
**Scope:** Implement `IGraphTenantClient`, GDAP validations, Hangfire background workers, and L3 cache projections using Microsoft Graph Delta queries.
- **Entry Criteria:** Phase 0 complete. Base architecture is running.
- **Exit Criteria:** System can onboard an M365 tenant, establish a delta sync for users/groups, and populate the Postgres projection tables without blocking the API thread.
- **Verification:** UI displays tenant list loaded strictly from L3/L2 cache without synchronous Graph calls.

## Phase 2: Identity & Access Domain
**Scope:** Implement CRUD, bulk operations, and management for users, groups, devices, app registrations, and GDAP mappings.
- **Entry Criteria:** Graph sync infrastructure (Phase 1) is robust.
- **Exit Criteria:** All features from CIPP's Identity & Access domain are functional and cached appropriately.
- **Verification:** E2E Playwright tests successfully create a user, assign a license, and verify visibility.

## Phase 3: Endpoint Management (Intune)
**Scope:** Devices, policies, autopilot profiles, compliance status, and LAPS/BitLocker recovery.
- **Entry Criteria:** Identity & Access domain complete.
- **Exit Criteria:** Intune features achieve feature-parity with CIPP, utilizing batched Graph requests for cross-tenant policy checks.
- **Verification:** Retrieval of BitLocker keys is logged in the audit trail and properly authorized.

## Phase 4: Email / Exchange Online
**Scope:** Mailboxes, transport rules, spam/phishing filters, message trace, and quarantine management.
- **Entry Criteria:** Phase 3 complete.
- **Exit Criteria:** Exchange admin operations operate through the Graph SDK, and changes are reflected in L3 projections.
- **Verification:** A transport rule can be applied to a tenant and successfully verified via a subsequent query.

## Phase 5: Teams / SharePoint / OneDrive
**Scope:** Sites, voice routing, OneDrive provisioning, and sharing settings.
- **Entry Criteria:** Phase 4 complete.
- **Exit Criteria:** Collaboration management features are complete and responsive.
- **Verification:** Can successfully provision a SharePoint site and update its external sharing settings.

## Phase 6: Security & Audit
**Scope:** Defender state, secure score, alerts/incidents, BEC remediation, and tenant allow/block lists.
- **Entry Criteria:** Phase 5 complete.
- **Exit Criteria:** Security alerts and metrics are synchronized via background jobs and immediately visible on the dashboard.
- **Verification:** BEC check orchestration successfully identifies and (if selected) remediates compromised accounts.

## Phase 7: Standards Engine & Templates
**Scope:** Implement the `IStandardHandler` registry, Report/Remediate/Alert modes, baseline blobs, and drift detection.
- **Entry Criteria:** Core domains (Phases 2-6) are complete to provide underlying API capabilities.
- **Exit Criteria:** The Standards engine can evaluate a tenant against a defined template, detect drift via JSON patch diffs, and orchestrate remediation safely.
- **Verification:** A drift detection job runs, identifies a modified policy, logs the drift, and successfully remediates it upon approval.

## Phase 8: Reports & Analytics
**Scope:** Cross-tenant analytics, license usage, inactive accounts, MFA compliance, and app consent reports.
- **Entry Criteria:** Standards engine complete.
- **Exit Criteria:** Reports load instantly by querying the Postgres L3 projections instead of live Graph hits.
- **Verification:** Load testing the reporting dashboard confirms no synchronous Graph API limits are hit.

## Phase 9: Automation, Scheduler & PSA Integrations
**Scope:** Recurring jobs, webhooks, alert configurations, and PSA ticketing integrations (Halo, NinjaOne, etc.).
- **Entry Criteria:** Reporting and core actions are solid.
- **Exit Criteria:** Webhooks can trigger actions securely. Alerts can automatically generate tickets in connected PSAs.
- **Verification:** A simulated standards drift triggers an alert which successfully opens a mock Halo ticket.

## Phase 10: Best Practice Analyzer (BPA) Strategy
**Scope:** Implement the forward-compatible wrapper for legacy BPA workflows migrating to the new Standards engine.
- **Entry Criteria:** Standards engine and Automation (Phases 7 & 9) are fully operational.
- **Exit Criteria:** Existing CIPP users have a clear migration path for their custom BPAs into the typed Standards format.
- **Verification:** A legacy BPA JSON definition is successfully ingested and converted into an `IStandardHandler` execution path.

## Phase 11: Production Polish & Multi-Region Support
**Scope:** Final security audits, distributed tracing review, multi-region database replication strategies, and SaaS onboarding flows.
- **Entry Criteria:** All functional features complete.
- **Exit Criteria:** The system is ready for general availability, supporting cross-region deployments for data sovereignty if required by customers.
- **Verification:** E2E security scan and load test pass with flying colors.
