# Dataset Platform Baseline

This workspace contains a production-oriented baseline for a schema-driven dataset platform:

- Backend repository service/API: ASP.NET Core (.NET 10)
- Frontend UI: Angular 20 (standalone)
- Unit tests for backend and frontend
- File-based storage abstraction designed to be swapped for other storage providers (S3/PostgreSQL)

## Solution Layout

- `backend/src/DatasetPlatform.Domain`: Domain models (schema, fields, instances, permissions, audit)
- `backend/src/DatasetPlatform.Application`: Business rules, authorization, validation, versioning, audit orchestration
- `backend/src/DatasetPlatform.Api`: HTTP API + file repository adapter + user context headers
- `backend/tests/DatasetPlatform.Api.Tests`: Unit tests for core domain behavior
- `frontend`: Angular UI for role-based dataset operations and schema maintenance

## Role Model

Headers used for role/user context in API requests:

- `x-user-id`: user identifier
- `x-user-roles`: comma-separated roles (for example: `Reader,Writer,Approver,Admin,DatasetAdmin`)

Role names are schema-configurable principals. Capability buckets remain Read/Write/Signoff/DatasetAdmin.

Supported capabilities:

- Read: view dataset schemas and dataset instances where read is permitted
- Write: create/edit dataset instances (non-official states)
- Signoff: promote dataset instance to official state
- DatasetAdmin: manage schemas, permissions, and view audit logs

## Key Backend Behaviors

- Schema-defined header and detail field metadata
- Validation on create/edit based on schema field types and constraints
- Official state can only be created through signoff
- Versioned dataset instances by dataset + asOfDate + state
- Latest version retrieval by dataset + asOfDate + state
- Audit event recorded for schema and dataset mutations
- Pluggable repository interface (`IDataRepository`) with initial file-based implementation

## API Endpoints

- `GET /api/schemas`
- `PUT /api/schemas/{datasetKey}`
- `DELETE /api/schemas/{datasetKey}`
- `GET /api/datasets/{datasetKey}/headers?minAsOfDate=YYYY-MM-DD&maxAsOfDate=YYYY-MM-DD&state=Draft|PendingApproval|Official&headerCriteria={...}&includeInternalInfo=true|false`
- `GET /api/datasets/{datasetKey}/instances?minAsOfDate=YYYY-MM-DD&maxAsOfDate=YYYY-MM-DD&state=Draft|PendingApproval|Official&headerCriteria={...}&includeInternalInfo=true|false` (exact-date behavior: use the same value for `minAsOfDate` and `maxAsOfDate`)
- `GET /api/datasets/{datasetKey}/instances/{instanceId}`
- `GET /api/datasets/{datasetKey}/instances/latest?asOfDate=YYYY-MM-DD&state=Draft|PendingApproval|Official`
- `POST /api/datasets/{datasetKey}/instances`
- `PUT /api/datasets/{datasetKey}/instances/{instanceId}`
- `POST /api/datasets/{datasetKey}/instances/{instanceId}/signoff`
- `GET /api/audit`

## API Diagnostics Logging

Global API request/response logging can be enabled through configuration:

- Section: `ApiDebug`
- `Enabled`: `true|false` (default: `false`)
- `LogFilePath`: path to the log file (default: `apiDiag.log`)

When enabled, all `/api/*` requests and responses are appended to the configured text file.
If `LogFilePath` is relative, it is resolved from `backend/src/DatasetPlatform.Api`.

## Run Instructions

### Backend

```powershell
cd backend/src/DatasetPlatform.Api
dotnet run
```

Default API URL is shown in launch output (typically `https://localhost:7278`).
With `start-dev.ps1`, the API runs at `http://localhost:5201`.

### Frontend

```powershell
cd frontend
npm install
npm start
```

Open the Angular URL shown in console (typically `http://localhost:4200`).
With `start-dev.ps1`, the UI runs at `http://localhost:4300`.

## Frontend Notes

- New schema defaults:
	- Read roles: `Reader`
	- Write roles: `Writer`
	- Signoff roles: `Approver`
	- Dataset admin roles: `Admin`
- Default simulated user context:
	- User: `Admin`
	- Roles: `Reader,Writer,Approver,Admin,DatasetAdmin`
- Clipboard import accepts both CSV and Excel-style tab-delimited data.

## Test Instructions

### Backend tests

```powershell
dotnet test DatasetPlatform.slnx
```

### Frontend tests

```powershell
cd frontend
npm run test -- --watch=false --browsers=ChromeHeadless
```

## Storage Swapping Strategy

Current implementation uses `BlobDataRepository` under `backend/src/DatasetPlatform.Api/Infrastructure`.

To swap to S3 or PostgreSQL:

1. Implement `IDataRepository` in a new class (for example `S3DataRepository` or `PostgresDataRepository`).
2. Register it in dependency injection in API startup (`Program.cs`).
3. Keep `DatasetService` unchanged so business logic remains centralized and storage-agnostic.

## Production Hardening Next Steps

- Integrate enterprise authentication/authorization (OIDC/Azure AD/Entra ID)
- Replace header-based identity with claims-based identity middleware
- Add optimistic concurrency control and idempotency keys
- Add structured logging and metrics (OpenTelemetry)
- Add integration tests for controllers and persistence adapters
- Add CI pipeline gates for unit tests, coverage, and security scanning
