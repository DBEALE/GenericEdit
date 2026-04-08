# Prompt: Recreate the Dataset Platform (GenericEdit)

Build a **schema-driven dataset management platform** — a full-stack web application for creating structured datasets with versioning, role-based access control, workflow lifecycle, and an audit trail. The system is called "GenericEdit" / "Dataset Platform".

---

## Architecture Overview

Three-tier clean architecture:

```
Angular 20 Frontend (Standalone Components, Signals)
    ↓ HTTP/JSON
ASP.NET Core 10 Web API
    ↓ C# interfaces
DatasetService (Business Logic + Authorization)
    ↓ IDataRepository
BlobDataRepository (File Adapter)
    ↓
FileSystemBlobStore  OR  S3BlobStore
    ↓
Local Disk (/data/)   OR  AWS S3
```

Backend solution has three projects:
- `DatasetPlatform.Domain` — pure C# models, zero dependencies
- `DatasetPlatform.Application` — interfaces, services, DTOs; depends on Domain
- `DatasetPlatform.Api` — controllers, infrastructure (storage), DI wiring; depends on Application + Domain

---

## Tech Stack

**Backend:**
- .NET 10.0, C#, nullable reference types enabled
- ASP.NET Core Web API, `Microsoft.AspNetCore.OpenApi`
- `AWSSDK.S3` (v4.0.5.1) for optional S3 storage
- `System.Text.Json` (camelCase naming, pretty-printed storage files)
- XUnit for tests

**Frontend:**
- Angular 20 (standalone components, no NgModules)
- ag-Grid Community 35.2.0 (data grid with inline editing)
- RxJS 7.8, TypeScript 5.9 (strict mode, ES2022 target)
- SCSS
- Karma/Jasmine for tests
- Prettier (printWidth: 100, singleQuote: true)

---

## Domain Models

### `DatasetSchema`
```csharp
string Key           // unique identifier, used in file paths and API routes
string Name
string? Description
SchemaField[] HeaderFields    // summary/identity columns
SchemaField[] DetailFields    // tabular row columns
DatasetPermissions Permissions
DateTime CreatedAtUtc
DateTime UpdatedAtUtc
```

### `SchemaField`
```csharp
string Name
string Label
FieldType Type       // String=1, Number=2, Date=3, Boolean=4, Select=5, Lookup=6
bool IsKey           // used for row uniqueness checks
bool Required
int? MaxLength
decimal? MinValue
decimal? MaxValue
string[]? AllowedValues       // for Select type
string? LookupDatasetKey      // for Lookup type (references another dataset's Official headers)
```

### `DatasetInstance`
```csharp
Guid Id              // immutable GUID
string DatasetKey
string AsOfDate      // YYYY-MM-DD
DatasetState State
int Version          // auto-increments on update
Dictionary<string, string?> Header   // key-value pairs matching HeaderFields
List<Dictionary<string, string?>> Rows  // tabular data matching DetailFields
string CreatedBy
DateTime CreatedAtUtc
string LastModifiedBy
DateTime LastModifiedAtUtc
```

### `DatasetState` (enum)
```csharp
Draft = 1
PendingApproval = 2
Official = 3
```
Lifecycle is one-way: Draft → PendingApproval → Official. Only the `/signoff` endpoint can create Official state.

### `DatasetPermissions`
```csharp
HashSet<string> ReadRoles
HashSet<string> WriteRoles
HashSet<string> SignoffRoles
HashSet<string> DatasetAdminRoles
// Case-insensitive matching
// Also supports legacy readUsers/writeUsers migration properties
```

Default permissions on schema creation:
- ReadRoles: `["Reader"]`
- WriteRoles: `["Writer"]`
- SignoffRoles: `["Approver"]`
- DatasetAdminRoles: `["Admin"]`

### `UserContext`
```csharp
string UserId        // from x-user-id header, defaults to "anonymous"
string[] Roles       // from x-user-roles header, comma-separated
```

### `AuditEvent`
```csharp
string Id
DateTime OccurredAtUtc
string UserId
string Action        // SCHEMA_UPSERT | SCHEMA_DELETE | INSTANCE_CREATE | INSTANCE_UPDATE | INSTANCE_SIGNOFF | INSTANCE_DELETE
string DatasetKey
Guid? DatasetInstanceId
AuditRowChange[] RowChanges
```

### `AuditRowChange`
```csharp
string OperationType   // "Added" | "Updated" | "Removed"
Dictionary<string, string?> KeyValues       // key fields of the row
Dictionary<string, string?>? SourceValues   // before (for updates/removes)
Dictionary<string, string?>? TargetValues   // after (for adds/updates)
```

---

## File Storage Layout

```
/data/
├── schemas/
│   └── {DATASETKEY}.json
├── instances/
│   └── {DATASETKEY}/
│       ├── {instanceId}.json          ← full instance including rows
│       └── headers/
│           └── {STATE}/               ← Draft | PendingApproval | Official
│               └── {yyyy-MM-dd}.header.json   ← index partition (no rows)
└── audit/
    └── {DATASETKEY}/
        └── {timestamp}_{ACTION}_{eventId}.json
```

Header partition files contain an array of `DatasetInstance` objects with rows omitted, plus a SHA-256 hash of the partition content. On read, the hash is verified; on mismatch the partition is rebuilt from individual instance files.

The `BlobDataRepository` uses `SemaphoreSlim(1)` to serialize all writes (thread-safety without distributed locking).

---

## API Endpoints

### Schemas
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/schemas` | List all schemas the caller can read |
| PUT | `/api/schemas/{datasetKey}` | Create or replace a schema |
| DELETE | `/api/schemas/{datasetKey}` | Delete schema and all its instances |

### Instances
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/datasets/{datasetKey}/headers` | List instance headers (no rows) |
| GET | `/api/datasets/{datasetKey}/instances` | List full instances |
| GET | `/api/datasets/{datasetKey}/instances/{instanceId}` | Fetch single instance |
| GET | `/api/datasets/{datasetKey}/instances/latest` | Latest for date+state |
| POST | `/api/datasets/{datasetKey}/instances` | Create instance |
| PUT | `/api/datasets/{datasetKey}/instances/{instanceId}` | Update instance |
| POST | `/api/datasets/{datasetKey}/instances/{instanceId}/signoff` | Promote to Official |
| DELETE | `/api/datasets/{datasetKey}/instances/{instanceId}` | Delete instance |

### Audit & Lookups
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/audit` | List audit events (optional `?datasetKey=`) |
| GET | `/api/lookups/{lookupDatasetKey}/values` | Get valid values for a Lookup field |

### Query Parameters (headers/instances endpoints)
- `minAsOfDate=YYYY-MM-DD`, `maxAsOfDate=YYYY-MM-DD`
- `state=Draft|PendingApproval|Official`
- `headerCriteria={JSON}` — substring filter on header key-value pairs
- `includeInternalInfo=true` — include performance/diagnostic stats in response

### Request Headers (trust-based auth for dev)
- `x-user-id: {userId}` — defaults to `"anonymous"`
- `x-user-roles: role1,role2,...` — comma-separated role names

### Response Shapes
```json
{ "items": [...], "internalInfo": null }       // for list endpoints
{ "item": {...}, "internalInfo": null }         // for single-item endpoints
```

---

## Business Logic (DatasetService)

### Authorization (`DatasetAuthorizer`)
- Global `DatasetAdmin` role bypasses all per-dataset checks
- Role hierarchy per dataset: DatasetAdmin ⊃ Signoff ⊃ Write ⊃ Read
- Methods: `CanRead`, `CanWrite`, `CanSignoff`, `IsDatasetAdmin`
- Checked in service layer before any data access

### Validation
- Field type enforcement (String, Number, Date, Boolean)
- Required field checks
- `MaxLength` for strings
- `MinValue`/`MaxValue` for numbers
- `AllowedValues` enforcement for Select fields
- Cross-dataset lookup validation (fetches Official headers of lookup dataset)
- Duplicate key detection (rows with identical key field values)

### Versioning
- Version auto-increments on every update
- Optional `ExpectedVersion` in update/signoff requests → 409 Conflict if stale
- Optional `ResetVersion` flag to restart version counter at 1

### Lifecycle
- `POST /instances` → always creates Draft
- `PUT /instances/{id}` → updates Header/Rows/State (but not to Official)
- `POST /instances/{id}/signoff` → only path to Official; validates signoff permission
- State can only move forward (Draft→PendingApproval→Official), never backward

### Error Handling
`DatasetServiceException` with `ErrorCode` enum values:
- `NotFound`, `Unauthorized`, `Conflict`, `ValidationError`, `SchemaNotFound`, `InstanceNotFound`

HTTP mapping: 400 (ValidationError), 403 (Unauthorized), 404 (NotFound/SchemaNotFound/InstanceNotFound), 409 (Conflict)

---

## Frontend Structure

### Root Component (`app.ts`)
Single large standalone component (~1500 lines) managing all state via Angular Signals:

**State signals include:**
- `selectedDatasetKey`, `selectedInstance`, `schemas`
- `instances`, `headers`, `auditEvents`
- `currentUserId`, `currentUserRoles` (user simulation)
- `activeTab` (Editor / Schema Builder / Audit)
- `isDarkTheme`
- `headerDateFilter` (Last Month / 3M / 6M / 1Y)
- `headerCriteria` (filter map)
- Loading/error states per operation

### Editor Tab (main interface)
- Left panel: searchable dataset selector
- Right panel:
  - AsOfDate picker + State selector (hybrid: freetext + suggestions datalist)
  - Header editor: key-value pairs matching schema's HeaderFields
  - Detail grid: ag-Grid with inline editing, one row per dictionary entry
  - Signoff button (shows audit comparison before confirming)
  - Version display + conflict handling

### Schema Builder Tab
- JSON editor pane (collapsible, shows raw schema JSON)
- Form-based field editor: add/remove/configure HeaderFields and DetailFields
- Permissions editor: comma-separated role lists per permission level
- Save = PUT `/api/schemas/{key}`

### Audit Explorer Tab
- List of audit events (filterable by dataset)
- Detail view per event:
  - Added rows grid (target values)
  - Updated rows grid (source → target with delta highlighting)
  - Removed rows grid (source values)

### Query Page (`query-page/`)
- Separate route component
- OpenAPI-explorer-style interface for testing all endpoints

### Custom ag-Grid Cell Editors
- `StateHybridCellEditor` — text input + `<datalist>` for state suggestions
- `LargeListCellEditor` — expanded multiselect for permitted/lookup values

### API Service (`dataset-api.service.ts`)
Injectable Angular service wrapping all HTTP calls using `HttpClient`.

### User Simulation
Frontend includes role presets (Reader / Writer / Approver / Admin) to simulate different users for testing, setting `x-user-id` and `x-user-roles` headers on all requests.

---

## Configuration

### `appsettings.json`
```json
{
  "Storage": {
    "Provider": "LocalFile",
    "BasePath": "../../../data",
    "S3": {
      "BucketName": "",
      "Region": "us-east-1",
      "Prefix": "dataset-platform",
      "ServiceUrl": "",
      "ForcePathStyle": false
    }
  },
  "ApiDebug": {
    "Enabled": false,
    "LogFilePath": "apiDiag.log",
    "MaxLogRows": 1000,
    "Verbosity": "Full",
    "LogSearchEfficiencyStats": false
  }
}
```

When `Provider = "S3"`, the `BlobDataRepository` delegates to `S3BlobStore` instead of `FileSystemBlobStore`.

### `ApiDiagnosticsMiddleware`
Optional request/response logger. Logs to a rotating file. Tracks search efficiency stats (how many partition files were read vs skipped). Enabled via `ApiDebug.Enabled = true`.

### Dev Scripts
- `start-dev.ps1` — starts API (`dotnet run`) and frontend (`ng serve --port 4300`) together
- `stop-dev.ps1` — kills the processes

---

## Testing

### Backend Tests (`DatasetPlatform.Api.Tests`)
- `DatasetServiceTests.cs` — unit tests for all service methods (create, update, signoff, delete, validation, authorization)
- `FileDataRepositoryTests.cs` — integration tests for file storage round-trips
- `HeadersDiagnosticsLogTests.cs` — tests for header partition hash/rebuild logic
- `LookupControllerTests.cs` — tests for lookup endpoint

### Frontend Tests
- `app.spec.ts` — component-level tests
- Karma/Jasmine configuration in `karma.conf.js`

---

## Build Instructions

### Backend
```bash
# From repo root
dotnet build DatasetPlatform.slnx
dotnet test DatasetPlatform.slnx
dotnet run --project backend/src/DatasetPlatform.Api
```

### Frontend
```bash
cd frontend
npm install
ng serve --port 4300   # dev
ng build               # production
```

### Data directory
Create `/data/schemas`, `/data/instances`, `/data/audit` at the path specified in `appsettings.json` `Storage.BasePath`.

---

## Key Design Decisions to Preserve

1. **Header partitioning for performance** — never load row data when only listing headers. Partition files per (state, asOfDate) with SHA-256 hash for stale detection and auto-rebuild.

2. **Single semaphore for writes** — `SemaphoreSlim(1,1)` in `BlobDataRepository` ensures no concurrent write corruption on local filesystem. S3 uses conditional writes.

3. **Trust-based headers in dev** — `x-user-id` and `x-user-roles` headers are read directly. For production, replace `RequestUserContextAccessor` with JWT/OIDC middleware extracting the same `UserContext` from claims.

4. **Signoff as a separate endpoint** — prevents any code path from setting Official state except the explicit signoff flow, which also enforces the `SignoffRoles` permission check.

5. **Angular Signals over RxJS** — all UI state is `signal()`/`computed()`. Only `HttpClient` (Observable-based) uses RxJS, via `.subscribe()` or `toSignal()`.

6. **One large root component** — intentional; the entire editor lives in `app.ts` to keep all state co-located. Only the Query Page is a separate component.

7. **Storage abstraction** — `IBlobStore` (get/put/delete/list blobs) is the lowest level. `BlobDataRepository` sits above it and understands the data model. Swap storage backend by changing `IBlobStore` implementation.

---

## Production Hardening (Not Yet Implemented)

- Replace header-based auth with OIDC/JWT (Azure AD or similar)
- Add HTTPS enforcement
- Add rate limiting
- Replace `SemaphoreSlim` with distributed lock (Redis/DynamoDB) for multi-instance deployment
- Add soft-delete / recycle bin for instances
- Add schema versioning (track schema changes over time)
- Add field-level encryption for sensitive data
