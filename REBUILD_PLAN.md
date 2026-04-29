# REBUILD_PLAN.md — Master Strategic Document

This document outlines the strategic rationale, audit findings, and the execution thesis for the TenantManagement rebuild, replacing the legacy CIPP ecosystem.

---

## 1. Executive Summary

CIPP has established itself as a critical open-source tool for Managed Service Providers (MSPs) to manage Microsoft 365 environments. However, its architectural foundation—a PowerShell-based Azure Function backend paired with a stateless React frontend—has reached its structural limits. 

The TenantManagement project is a ground-up, clean-room rebuild in C# / .NET 10. It retains the functional surface area of CIPP while replacing the runtime, data layer, security posture, and distribution model to support enterprise scale, high performance, and rigorous security.

---

## 2. Audit Findings of Legacy Architecture

Our deep-dive research into the existing `KelvinTegelaar/CIPP` and `KelvinTegelaar/CIPP-API` repositories revealed several critical pain points capping its scale and stability:

### 2.1 Backend (CIPP-API)
- **Runtime Bottlenecks:** PowerShell 7.4 on Azure Functions v4 introduces documented cold starts of 15-20 seconds. Throughput is severely bounded by per-runspace module loads.
- **Structural Fragmentation:** The API consists of **533 individual `Invoke-*.ps1` HTTP-trigger files** and **187 standard files** with no unified controller registry, static typing, or DTO validation.
- **Persistence Limitations:** Relying exclusively on Azure Table Storage has broken features; notably, templates exceeding the 64 KB row limit fail to save (an issue open since 2023).
- **Auth & Security:** The "Secure Application Model" (SAM) implementation mirrors sensitive refresh tokens into process environment variables. The project uses a custom `.dll` shim for token caching, bypassing standard identity primitives.
- **State Management:** Durable Functions with strict version matching fail hard on mid-flight deployments, requiring a manual "Clear Durable Queue" maintenance UI.

### 2.2 Frontend (CIPP)
- **Tech Debt:** Built on Next.js 16 + React 19 + MUI 7 using static export to Azure Static Web Apps. The codebase suffers from massive files (e.g., ~1,448-line toolbar files), inconsistent form libraries (Formik + react-hook-form), and a lack of real TypeScript adoption.
- **Testing:** Zero frontend test coverage.
- **Performance:** Because the backend lacks a caching layer, the JS frontend must hydrate by paginating Graph until done. At scale (100+ tenants), this leads to severe timeouts and user complaints (e.g., a 15-minute BPA refresh).

### 2.3 Operational & Distribution Model
- **Distribution Model:** The "self-host-by-fork" requirement, where every MSP forks the repo and pulls from upstream, is a constant source of breakage during major releases.
- **License Posture:** The project uses AGPL-3.0 and offers a mere $50 bug bounty, a posture deeply misaligned with a tool possessing delegated administrative access to thousands of downstream M365 tenants.

---

## 3. The Rebuild Thesis

To resolve these systemic issues, the rebuild adopts the following architectural tenets:

### 3.1 Leverage the Graph SDK
CIPP spent massive effort reinventing Graph proxying (pagination, batching, retry logic, OAuth flows). TenantManagement will rely natively on **Microsoft.Graph SDK v5** and **Microsoft.Identity.Web**. If a feature requires more than 50 lines of custom Graph plumbing, we are doing it wrong.

### 3.2 Caching as a First-Class Concern
We are transitioning from a stateless "proxy-to-Graph" model to a heavily cached read-path:
- **L1:** In-memory cache per node.
- **L2:** Redis distributed cache.
- **L3:** PostgreSQL projection tables.
Reads are served instantly from cache. Background Hangfire workers utilize Microsoft Graph **Delta Queries** to populate and refresh the L3 projections asynchronously.

### 3.3 Strict Multi-Tenancy & Persistence
We are adopting **PostgreSQL** via EF Core 10 as the unified relational store, abandoning Azure Tables. Every query is scoped by an `MspId` via EF Core Global Query Filters, ensuring bulletproof cross-MSP isolation by construction.

### 3.4 Modernized Distribution & License
The application will be distributed primarily as a **managed multi-MSP SaaS**, eliminating the fork-and-deploy nightmare. A self-host container image will remain for customers with strict data sovereignty needs. The core will transition to a permissive license (likely Apache 2.0), decoupled from the AGPL friction.

---

## 4. High-Level Timeline

Execution is managed via a strict 12-phase pipeline (see `PHASES.md` for detailed scopes):

- **Phase 0:** Foundation & Architecture definition.
- **Phase 1-6:** Domain implementations (Identity, Endpoint, Exchange, Collaboration, Security) with Graph Delta syncs.
- **Phase 7-10:** Orchestration (Standards Engine, Analytics, PSA Automation, BPA Strategy).
- **Phase 11:** Production Polish, Load Testing, & Multi-Region Support.

The project operates cleanly out of the `TenantManagement.*` project structure, enforcing C# 13, MediatR, and FluentValidation standards across the board.
