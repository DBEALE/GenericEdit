# Dataset Platform — Developer Guide

Welcome to the Dataset Platform codebase. This guide is written for junior team members who are new to the project. It explains what the system does, how it is structured, and how to navigate the code confidently.

---

## Table of Contents

1. [What does this system do?](#1-what-does-this-system-do)
2. [High-level architecture](#2-high-level-architecture)
3. [Project structure](#3-project-structure)
4. [Key concepts](#4-key-concepts)
5. [Data flow — walking through a request](#5-data-flow--walking-through-a-request)
6. [Authorization model](#6-authorization-model)
7. [Storage and file layout](#7-storage-and-file-layout)
8. [API reference](#8-api-reference)
9. [Configuration](#9-configuration)
10. [Running locally](#10-running-locally)
11. [Running tests](#11-running-tests)
12. [Frontend overview](#12-frontend-overview)
13. [Glossary](#13-glossary)

---

## 1. What does this system do?

The Dataset Platform is a **schema-driven dataset management system**. Think of it like a structured spreadsheet manager where:

- **Schemas** define _what_ a dataset looks like (columns, types, validations, who can access it).
- **Instances** are individual snapshots of data — each instance has a date, a lifecycle state, and the actual data rows.
- **Audit events** record every change ever made, so you can always see who changed what and when.

### Real-world analogy

Imagine a financial firm that publishes daily market reference rates. Each day's rates are a "dataset instance". A junior analyst creates a draft, a senior analyst reviews it and promotes it to "pending approval", then a head of desk signs it off as "official". Everyone downstream can then query the official rate for a specific date.

---

## 2. High-level architecture

```
┌─────────────────────────────┐
│        Angular UI            │  frontend/src/
│  (ag-Grid, Angular Signals)  │
└──────────────┬──────────────┘
               │ HTTP / JSON
┌──────────────▼──────────────┐
│      ASP.NET Core API        │  DatasetPlatform.Api
│   Controllers + Middleware   │
└──────────────┬──────────────┘
               │ C# interfaces
┌──────────────▼──────────────┐
│     DatasetService           │  DatasetPlatform.Application
│  (business logic + auth)     │
└──────────────┬──────────────┘
               │ IDataRepository
┌──────────────▼──────────────┐
│   BlobDataRepository         │  DatasetPlatform.Api / Infrastructure
│  (file or S3 storage)        │
└──────────────┬──────────────┘
               │
    ┌──────────┴──────────┐
    │                     │
┌───▼────┐         ┌──────▼──────┐
│  Local  │         │   AWS S3    │
│  Files  │         │   Bucket    │
└────────┘         └─────────────┘
```

### Clean Architecture layers

The backend follows a layered (Clean Architecture) approach. Each layer only depends on the layer below it:

| Layer | Project | Responsibility |
|---|---|---|
| **Domain** | `DatasetPlatform.Domain` | Plain C# models. No logic, no dependencies. |
| **Application** | `DatasetPlatform.Application` | Business logic, authorization, validation. Depends only on Domain. |
| **API** | `DatasetPlatform.Api` | HTTP controllers, middleware, storage implementations. Depends on Application. |

> **Why does this matter?** If you want to swap the database from local files to PostgreSQL, you only change the `DatasetPlatform.Api` infrastructure layer — the business logic in `DatasetService` stays completely untouched.

---

## 3. Project structure

```
CopitotEditor/
├── backend/
│   ├── src/
│   │   ├── DatasetPlatform.Domain/        # Pure models — no framework dependencies
│   │   │   └── Models/
│   │   │       ├── DatasetSchema.cs        # Schema definition
│   │   │       ├── DatasetInstance.cs      # A snapshot of data
│   │   │       ├── DatasetState.cs         # Enum: Draft → PendingApproval → Official
│   │   │       ├── SchemaField.cs          # A single column definition
│   │   │       ├── FieldType.cs            # Enum: String, Number, Date, Boolean, Select, Lookup
│   │   │       ├── DatasetPermissions.cs   # Role-based access control config
│   │   │       ├── UserContext.cs          # Who is making the request
│   │   │       └── AuditEvent.cs           # Immutable log entry
│   │   │
│   │   ├── DatasetPlatform.Application/   # Business logic
│   │   │   ├── Abstractions/
│   │   │   │   ├── IDatasetService.cs      # What the API can ask the service to do
│   │   │   │   └── IDataRepository.cs      # What the service can ask storage to do
│   │   │   ├── Services/
│   │   │   │   ├── DatasetService.cs       # The main business logic class (~500 lines)
│   │   │   │   ├── DatasetAuthorizer.cs    # Permission checks (pure static methods)
│   │   │   │   └── DatasetServiceException.cs # Business-rule violation exception
│   │   │   └── Dtos/
│   │   │       ├── DatasetRequests.cs      # Incoming request shapes (Create, Update, Signoff)
│   │   │       └── DatasetResponses.cs     # Outgoing response shapes
│   │   │
│   │   └── DatasetPlatform.Api/           # Web API + storage implementations
│   │       ├── Controllers/
│   │       │   ├── SchemasController.cs          # GET/PUT/DELETE /api/schemas
│   │       │   ├── DatasetInstancesController.cs  # Full CRUD + signoff
│   │       │   ├── AuditController.cs             # GET /api/audit
│   │       │   └── LookupController.cs            # GET /api/lookups/{key}/values
│   │       ├── Infrastructure/
│   │       │   ├── IBlobStore.cs                  # Storage abstraction interface
│   │       │   ├── FileSystemBlobStore.cs         # Local filesystem implementation
│   │       │   ├── S3BlobStore.cs                 # AWS S3 implementation
│   │       │   ├── BlobDataRepository.cs          # Translates domain calls → blob operations
│   │       │   ├── DatasetHeaderPartitioning.cs   # Partitioning logic for fast queries
│   │       │   ├── RequestUserContextAccessor.cs  # Reads user identity from HTTP headers
│   │       │   ├── ApiDiagnosticsMiddleware.cs    # Request/response logging to file
│   │       │   ├── ApiDiagnosticsOptions.cs       # Config model for diagnostics
│   │       │   ├── ApiRequestIoStats.cs           # I/O counters for diagnostics
│   │       │   └── StorageOptions.cs              # Config models for storage provider
│   │       ├── appsettings.json                   # Production config
│   │       ├── appsettings.Development.json       # Dev overrides
│   │       └── Program.cs                         # App startup + DI wiring
│   │
│   └── tests/
│       └── DatasetPlatform.Api.Tests/     # XUnit test suite
│
├── frontend/
│   └── src/app/
│       ├── app.ts                          # Root component (grid + editor)
│       ├── app.html                        # Template
│       ├── dataset-api.service.ts          # HTTP client for the backend
│       ├── models.ts                       # TypeScript interfaces matching backend models
│       └── query-page/                     # OpenAPI explorer component
│
├── data/                                   # Local storage (gitignored in prod)
│   ├── schemas/                            # Schema JSON files
│   ├── instances/                          # Instance JSON files
│   └── audit/                             # Audit event JSON files
│
├── DEVELOPER_GUIDE.md                      # This file
├── SUGGESTED_IMPROVEMENTS.md              # Recommended next steps
├── start-dev.ps1                           # Starts both servers
└── stop-dev.ps1                            # Stops both servers
```

---

## 4. Key concepts

### Dataset Schema

A schema is the blueprint for a dataset. It defines:

- **Key** — a URL-safe identifier (e.g. `market-rates`). Always lowercase.
- **Name** — a human-readable display name.
- **HeaderFields** — columns that appear in every instance's header (summary/identity info).
- **DetailFields** — columns for each row of tabular data.
- **Permissions** — who can read, write, sign off, or administer the schema.

### Dataset Instance

An instance is one snapshot of data for a specific date. It has:

- **AsOfDate** — the business date this data applies to (e.g. `2024-12-01`).
- **State** — where it is in the approval lifecycle.
- **Version** — a counter that increments on every update.
- **Header** — the summary key-value pairs (e.g. `{"region": "EMEA", "currency": "USD"}`).
- **Rows** — the tabular detail data.

### Lifecycle states

```
  [Draft] ──────────► [PendingApproval] ──────► [Official]
     ▲                        ▲                      ▲
  Created by              Created by          Only via /signoff
  write users            write users          by signoff users
```

> **Important:** You can never set a state to `Official` via a create or update request. You must call the dedicated `/signoff` endpoint. This is enforced in `DatasetService.CreateInstanceAsync` and `UpdateInstanceAsync`.

### Header partitioning (how queries stay fast)

Rather than reading every instance file on every query, the system maintains **header index files**. Each index file covers one (state, asOfDate) combination and stores only the header fields (not the row data). This means:

- A query filtered by date and state only reads a small subset of files.
- Full detail rows are only loaded when explicitly requested.
- If an index file is found to be stale (its stored hash doesn't match the instance), it is automatically rebuilt.

### Audit trail

Every operation (create, update, delete, signoff, schema changes) appends an immutable `AuditEvent` to storage. Audit events are never modified. They record who did what and when, with a plain-text description of the change.

---

## 5. Data flow — walking through a request

Let's trace what happens when a user calls `POST /api/datasets/market-rates/instances`:

```
1. HTTP Request arrives
   → ApiDiagnosticsMiddleware intercepts (logs request, wraps response)

2. DatasetInstancesController.Create() is called
   → Validates route key matches body key
   → Calls userContextAccessor.GetCurrent()
     → Reads "x-user-id" and "x-user-roles" headers
     → Returns a UserContext object

3. datasetService.CreateInstanceAsync(request, user) is called
   → Normalises the dataset key to lowercase
   → Calls repository.GetSchemaAsync() to load the schema
   → Calls DatasetAuthorizer.CanWrite(schema, user) — throws if not authorised
   → Rejects if state == Official (must use signoff endpoint)
   → Calls ValidateBySchemaAsync() — checks field types, required fields, allowed values
   → Calls repository.GetInstancesAsync() to check for duplicate headers
   → Assigns a new GUID, calculates the next version number
   → Calls repository.SaveInstanceAsync() to persist the new instance
   → Calls AddAuditAsync() to record the creation event

4. The created DatasetInstance is returned as JSON
   → ApiDiagnosticsMiddleware logs the response

5. HTTP 200 OK with the instance JSON is sent to the client
```

---

## 6. Authorization model

Authorization is **role-based** and defined per schema. The schema's `Permissions` object contains four sets of principal names (user IDs or role names):

| Permission set | What it grants |
|---|---|
| `ReadRoles` | View instances, headers, audit logs |
| `WriteRoles` | Create, update, delete Draft/PendingApproval instances (also implies read) |
| `SignoffRoles` | Promote instances to Official (also implies read) |
| `DatasetAdminRoles` | Modify the schema itself (also implies all other access) |

There is also a **global** `DatasetAdmin` role (the string `"DatasetAdmin"`) — any user holding this role has full access to every dataset and every operation, regardless of individual schema permissions.

### How identity reaches the server

The user's identity is passed via HTTP headers (simulated — no real authentication currently):

- `x-user-id: alice` — the user's identifier
- `x-user-roles: DatasetAdmin,Analyst` — comma-separated roles

> **Security warning:** These headers are not cryptographically verified. Anyone can forge them. Before going to production, replace `RequestUserContextAccessor` with proper JWT/OIDC validation.

### Following an authorization check in code

```
Controller
  └─ userContextAccessor.GetCurrent()     → builds UserContext from headers
Controller
  └─ datasetService.SomeMethod(user)
       └─ GetSchemaOrThrowAsync()         → loads schema (throws 400 if not found)
       └─ DatasetAuthorizer.CanWrite()    → checks user against schema.Permissions
       └─ EnsureAuthorized(bool, msg)     → throws DatasetServiceException if false
Controller
  └─ catches DatasetServiceException      → returns HTTP 400 with the message
```

---

## 7. Storage and file layout

### File structure (LocalFile provider)

```
data/
├── schemas/
│   ├── market-rates.json          # Schema definition for the "market-rates" dataset
│   └── fund-nav.json
│
├── instances/
│   ├── market-rates/
│   │   ├── {guid-without-dashes}.json   # Full instance (header + rows)
│   │   └── headers/
│   │       ├── OFFICIAL/
│   │       │   └── 2024-12-01.header.json   # Header index for Official / 2024-12-01
│   │       └── DRAFT/
│   │           └── 2024-12-01.header.json
│   └── fund-nav/
│       └── ...
│
└── audit/
    ├── market-rates/
    │   └── 20241201T120000000Z_INSTANCE_CREATE_{guid}.json
    └── fund-nav/
        └── ...
```

### Switching to S3

Change `appsettings.json`:

```json
"Storage": {
  "Provider": "S3",
  "S3": {
    "BucketName": "my-bucket",
    "Region": "eu-west-1",
    "Prefix": "dataset-platform"
  }
}
```

The same `BlobDataRepository` and all business logic works unchanged — only the `IBlobStore` implementation swaps.

### Adding a new storage backend (e.g. PostgreSQL)

1. Create a class that implements `IDataRepository` (in `DatasetPlatform.Api/Infrastructure/`).
2. Register it in `Program.cs` under a new provider name.
3. The `DatasetService` class needs zero changes.

---

## 8. API reference

All endpoints are prefixed with `/api`. All request/response bodies are JSON.

### Schemas

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/schemas` | List all schemas accessible to the current user |
| `PUT` | `/api/schemas/{datasetKey}` | Create or replace a schema |
| `DELETE` | `/api/schemas/{datasetKey}` | Delete schema and all its instances |

### Instances

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/datasets/{key}/instances` | List instances (with optional filters) |
| `GET` | `/api/datasets/{key}/headers` | List header summaries only (faster) |
| `GET` | `/api/datasets/{key}/instances/{id}` | Get a single instance by GUID |
| `GET` | `/api/datasets/{key}/instances/latest` | Get the most recent instance for a date + state |
| `POST` | `/api/datasets/{key}/instances` | Create a new instance |
| `PUT` | `/api/datasets/{key}/instances/{id}` | Update an existing instance |
| `DELETE` | `/api/datasets/{key}/instances/{id}` | Delete an instance |
| `POST` | `/api/datasets/{key}/instances/{id}/signoff` | Promote instance to Official state |

### Common query parameters (GET instances/headers)

| Parameter | Type | Description |
|---|---|---|
| `minAsOfDate` | `yyyy-MM-dd` | Exclude instances before this date |
| `maxAsOfDate` | `yyyy-MM-dd` | Exclude instances after this date |
| `state` | string | Filter by state: `Draft`, `PendingApproval`, or `Official` |
| `headerCriteria` | JSON string | Substring filter on header fields, e.g. `{"region":"EMEA"}` |
| `includeInternalInfo` | bool | Attach storage diagnostic stats to the response |

### Audit & lookups

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/audit?datasetKey={key}` | Get audit events (optionally filtered to a dataset) |
| `GET` | `/api/lookups/{key}/values` | Get allowed values for a Lookup-type field |

---

## 9. Configuration

### `appsettings.json`

```json
{
  "Storage": {
    "Provider": "LocalFile",        // "LocalFile" or "S3"
    "BasePath": "../../../data",    // Root folder for LocalFile provider
    "S3": {
      "BucketName": "",
      "Region": "us-east-1",
      "Prefix": "dataset-platform",
      "ServiceUrl": "",             // Set this for LocalStack / MinIO
      "ForcePathStyle": false
    }
  },
  "ApiDebug": {
    "Enabled": true,                // Enable request/response logging
    "LogFilePath": "apiDiag.log",   // Where to write logs
    "Verbosity": "Compact",         // "Compact" (headers only) or "Full" (includes bodies)
    "LogSearchEfficiencyStats": true // Include storage I/O counters in each log entry
  }
}
```

### Frontend base URL

The frontend defaults to `http://localhost:5201/api`. To change it, set the global variable before Angular bootstraps:

```html
<script>
  globalThis.__datasetApiBaseUrl = 'https://your-api-host/api';
</script>
```

---

## 10. Running locally

### Prerequisites

- .NET 10 SDK
- Node.js 20+ and npm

### Start both servers

```powershell
./start-dev.ps1
```

This launches:
- Backend: `http://localhost:5201` (or as configured)
- Frontend: `http://localhost:4200`

Or start them manually:

```bash
# Backend
cd backend/src/DatasetPlatform.Api
dotnet run

# Frontend (separate terminal)
cd frontend
npm install
npm start
```

### Seed test data

```powershell
./seed-historical-data.ps1
```

### Stop servers

```powershell
./stop-dev.ps1
```

---

## 11. Running tests

```bash
cd backend/tests/DatasetPlatform.Api.Tests
dotnet test
```

### What is tested

| Test file | What it covers |
|---|---|
| `DatasetServiceTests.cs` | Instance creation validation, state transition rules, signoff workflow |
| `LookupControllerTests.cs` | Lookup endpoint integration |
| `FileDataRepositoryTests.cs` | Persistence — save, load, delete instances |
| `HeadersDiagnosticsLogTests.cs` | Diagnostics middleware output |

### Test utilities

- `InMemoryRepository` — an in-memory `IDataRepository` implementation for fast unit tests (no disk I/O).
- User factory helpers create `UserContext` objects with preset roles.

---

## 12. Frontend overview

The frontend is an **Angular 20** single-page application using **standalone components** (no `NgModule`).

### State management

Angular **Signals** are used for reactive state — no Redux or NgRx. Key signals in `app.ts`:

| Signal | What it holds |
|---|---|
| `selectedSchema` | The dataset the user has chosen from the list |
| `instances` | The loaded instances for the selected schema |
| `editingInstance` | The instance currently open in the editor |
| `currentUser` | Simulated user identity (for demo purposes) |

### UI components

- **ag-Grid** — renders the instance list and the row editor grid. Configured with inline cell editing.
- **Dataset catalogue** — left panel showing all accessible schemas.
- **Header editor** — form for filling in header fields.
- **Detail grid** — ag-Grid for editing tabular row data.
- **Query page** (`query-page/`) — a separate tab for exploring the API via the OpenAPI spec.

### HTTP client (`dataset-api.service.ts`)

All API calls go through `DatasetApiService`. It uses Angular's `HttpClient` and is typed against the models in `models.ts`.

---

## 13. Glossary

| Term | Meaning |
|---|---|
| **Schema** | The definition of a dataset's structure and permissions |
| **Instance** | One versioned snapshot of data for a specific date |
| **Header** | The summary/identity key-value pairs of an instance (e.g. region, currency) |
| **Detail rows** | The tabular data rows of an instance |
| **AsOfDate** | The business date a snapshot is "as of" (not the creation date) |
| **State** | Lifecycle status: Draft → PendingApproval → Official |
| **Signoff** | The act of promoting a PendingApproval/Draft instance to Official |
| **Version** | Integer counter that increments on each update to the same instance |
| **Header index / partition** | A lightweight file containing only header fields, used to speed up filtered queries |
| **DatasetAdmin** | The global super-admin role name; grants full access to everything |
| **Blob store** | The underlying file/object storage abstraction (LocalFile or S3) |
| **Audit event** | An immutable log record of any create/update/delete/signoff operation |
| **Lookup field** | A field whose allowed values are drawn from another dataset at runtime |
| **Principal** | A user ID or role name that can be granted a permission |
