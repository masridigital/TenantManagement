# TenantManagement Backend - Local Testing Guide

Welcome to the new Clean Architecture rebuild of the CIPP Tenant Management platform!

To remove deployment friction, the Docker dependencies (PostgreSQL and Redis) have been completely removed for local development. The platform now seamlessly uses **SQLite** and **In-Memory Caching**.

Follow these exact steps to run and test the backend features immediately.

## 1. Start the API (Terminal 1)
The API project contains all of our MediatR logic, Minimal API endpoints, and EF Core contexts.
Open a new terminal at the root of the repository and run:
```bash
cd src/TenantManagement.Api
dotnet run
```
*Wait until you see `Now listening on: http://localhost:5000` (or similar).*

## 2. Start the Background Worker (Terminal 2)
The Worker project runs our Hangfire background jobs which simulate pulling data from Microsoft Graph and caching it locally in the SQLite database.
Open a **second** terminal at the root of the repository and run:
```bash
cd src/TenantManagement.Worker
dotnet run
```
*Wait until you see `Hangfire Server started` and `Application started`.*

## 3. Verify the Endpoints
With both services running, you can test the APIs.

Because the system implements strict multi-tenant isolation, it requires an authenticated `MspId`. For local testing, we have implemented endpoints that you can call. If you hit the health endpoint, you'll see it is alive:
- **Health Check:** `http://localhost:5000/api/health`

### Swagger / OpenAPI UI
To easily interact with the authenticated endpoints, open your browser to:
**[http://localhost:5000/swagger](http://localhost:5000/swagger)** *(Note: The port might be different depending on your launchSettings.json, check Terminal 1 output for the exact URL, e.g., `https://localhost:5001/openapi/v1.json`).*

*Currently available endpoints:*
* `GET /api/tenants`
* `GET /api/tenants/{customerTenantId}/users`

*(Note: Without a valid Bearer token from Azure AD, these endpoints will return a `401 Unauthorized`. To test these natively, you'll need to configure your `AzureAd` block in `appsettings.json` with a valid App Registration.)*

## 4. Explore the Database
Because we use SQLite, a file named `TenantManagement.db` has been automatically created in the `src/TenantManagement.Api` folder.
You can open this file using any SQLite viewer (like DBeaver, or the VSCode SQLite extension) to see the tables we created (`CustomerTenants`, `TenantUsers`, `TenantGroups`) and verify the data schema!

## What was built in this milestone?
- **Phase 0:** Clean Architecture scaffolding, Domain Base Entities with strict Global Query Filters forcing `MspId` encapsulation.
- **Phase 1:** Cache Hierarchy (L1: Memory, L2: Memory/Redis, L3: Database Projections) eliminating UI-blocking Graph calls.
- **Phase 2:** Graph Tenant SDK Client factories and Background Hangfire sync jobs for Users, Groups, and Tenants.
