# Suggested Improvements & Refactors

This document lists recommended changes to improve the Dataset Platform's security, reliability, maintainability, and observability. Items are grouped by priority.

---

## Priority 1 — Must fix before production

### 1.1 Replace header-based authentication with real identity verification

**Problem:** User identity is read from `x-user-id` and `x-user-roles` HTTP headers with no cryptographic verification. Any client can supply arbitrary values, effectively impersonating any user or claiming any role.

**Files:** [RequestUserContextAccessor.cs](backend/src/DatasetPlatform.Api/Infrastructure/RequestUserContextAccessor.cs)

**What to do:**
- Integrate an OIDC provider (Azure AD / Entra ID, Auth0, Okta, etc.).
- Add `builder.Services.AddAuthentication().AddJwtBearer(...)` in [Program.cs](backend/src/DatasetPlatform.Api/Program.cs).
- Replace `RequestUserContextAccessor` with one that reads the validated `ClaimsPrincipal` from `HttpContext.User`.
- Add `[Authorize]` attributes to controllers, or a global authorization policy.

---

### 1.2 Restrict CORS policy

**Problem:** The API currently allows requests from any origin (`AllowAnyOrigin()`). This is dangerous in production because it allows cross-site requests from any website.

**File:** [Program.cs](backend/src/DatasetPlatform.Api/Program.cs:54-57)

**What to do:**
```csharp
// Replace AllowAnyOrigin() with explicit allow-list:
policy.WithOrigins("https://your-frontend-domain.com")
      .AllowAnyHeader()
      .AllowAnyMethod();
```

---

### 1.3 Add concurrency control (optimistic locking)

**Problem:** If two users simultaneously update the same instance, the second write silently overwrites the first (last-write-wins). There is no mechanism to detect this conflict.

**Files:** [BlobDataRepository.cs](backend/src/DatasetPlatform.Api/Infrastructure/BlobDataRepository.cs), [DatasetService.cs](backend/src/DatasetPlatform.Application/Services/DatasetService.cs), [DatasetRequests.cs](backend/src/DatasetPlatform.Application/Dtos/DatasetRequests.cs)

**What to do:**
- Add an `ExpectedVersion` field to `UpdateDatasetInstanceRequest`.
- In `DatasetService.UpdateInstanceAsync`, compare `request.ExpectedVersion` against `existing.Version` and throw if they differ.
- Return `HTTP 409 Conflict` from the controller (currently all service errors return 400).
- The frontend should pass the `version` it last fetched when submitting an update.

---

### 1.4 Multi-instance deployment: replace the in-process semaphore

**Problem:** `BlobDataRepository` uses a `SemaphoreSlim(1,1)` to serialise writes. This only works within a single server process. Running two API instances simultaneously will cause race conditions and potential data corruption.

**File:** [BlobDataRepository.cs](backend/src/DatasetPlatform.Api/Infrastructure/BlobDataRepository.cs:19)

**What to do (short term):** Ensure only one API instance runs at a time (sticky sessions / single replica).

**What to do (long term):** Replace the file-based storage with a database that has native transaction and locking support (PostgreSQL is a natural fit). Implementing `IDataRepository` against a database removes this problem entirely.

---

## Priority 2 — Important for quality and reliability

### 2.1 Split DatasetService into focused services

**Problem:** [DatasetService.cs](backend/src/DatasetPlatform.Application/Services/DatasetService.cs) is ~500 lines and handles schemas, instances, headers, audit, lookups, validation, and filtering. It is hard to navigate and increasingly risky to modify.

**What to do:** Extract the following classes, each implementing a focused interface:

| New class | Responsibility |
|---|---|
| `SchemaService` | Schema CRUD + validation |
| `InstanceQueryService` | All read operations (instances, headers, latest) |
| `InstanceMutationService` | Create, update, delete, signoff |
| `SchemaValidator` | Field validation logic (`ValidateBySchemaAsync`, `EnsureNoDuplicateFieldNames`, etc.) |
| `AuditService` | Audit event reads and writes |

The controller-facing `IDatasetService` interface can remain as a facade that delegates to these smaller services, keeping the controllers unchanged.

---

### 2.2 Return HTTP 404 for "not found" errors instead of HTTP 400

**Problem:** When an instance or schema is not found, the service throws `DatasetServiceException` and controllers return HTTP 400 ("Bad Request"). HTTP 404 ("Not Found") is the correct status code for this situation, and clients (including the frontend) may behave differently based on status codes.

**Files:** All controllers, [DatasetServiceException.cs](backend/src/DatasetPlatform.Application/Services/DatasetServiceException.cs)

**What to do:**
- Add an error-type discriminator to `DatasetServiceException`, e.g. an `ErrorCode` enum (`NotFound`, `Unauthorized`, `ValidationFailed`, `Conflict`).
- In each controller, map the error code to the appropriate HTTP status (`404`, `403`, `400`, `409`).

Example:
```csharp
catch (DatasetServiceException ex) when (ex.ErrorCode == ErrorCode.NotFound)
{
    return NotFound(ex.Message);
}
catch (DatasetServiceException ex) when (ex.ErrorCode == ErrorCode.Unauthorized)
{
    return Forbid();
}
catch (DatasetServiceException ex)
{
    return BadRequest(ex.Message);
}
```

---

### 2.3 Add a global exception handler middleware

**Problem:** Unhandled exceptions (e.g. storage errors, serialization failures) propagate to the client as HTTP 500 with stack traces in development, or with no useful body in production. There is no consistent error response format.

**What to do:**
- Add `app.UseExceptionHandler(...)` or implement a `ProblemDetails`-based exception handler in [Program.cs](backend/src/DatasetPlatform.Api/Program.cs).
- Use the RFC 7807 `ProblemDetails` format for all error responses.
- Log unexpected exceptions with `ILogger` at `Error` level.

---

### 2.4 Add controller-level input validation

**Problem:** Controllers accept DTOs but delegate all validation to the service. If a malformed request body cannot even be deserialized, ASP.NET returns a generic 400 with no useful message. There is also no schema-level validation on the schema PUT endpoint before the service is called.

**What to do:**
- Add `[ApiController]` data annotations (`[Required]`, `[StringLength]`) to DTO properties or use FluentValidation.
- Validate that schema `PUT` bodies have at least a `Key` and `Name` before reaching the service.
- Consider using `ModelState` validation in controllers for fast-fail on obviously malformed input.

---

### 2.5 Add integration and controller tests

**Problem:** The existing tests only cover `DatasetService` and the repository in isolation. There are no tests that exercise the full HTTP request pipeline (controllers, middleware, serialization, authorization).

**Files:** [tests/DatasetPlatform.Api.Tests/](backend/tests/DatasetPlatform.Api.Tests/)

**What to do:**
- Add `WebApplicationFactory<Program>` integration tests that start the full API in memory.
- Test each controller endpoint with valid inputs, invalid inputs, and unauthorized users.
- Test the `ApiDiagnosticsMiddleware` to confirm it logs correct output.
- Add frontend component tests using Angular Testing Library or Jasmine.

---

### 2.6 Gitignore the `data/` folder

**Problem:** The `data/` directory contains live production data (instances, schemas, audit logs). It should not be in source control.

**What to do:**
- Add `data/` to `.gitignore`.
- Document the data directory path in [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) so new developers know where to find it.
- Add a `seed-historical-data.ps1` note in the README explaining how to bootstrap local data.

---

### 2.7 Gitignore `frontend/dist/`

**Problem:** The compiled frontend build output is checked in to source control. This creates merge conflicts, inflates repository size, and should be produced by CI/CD instead.

**What to do:**
- Add `frontend/dist/` to `.gitignore`.
- Add a CI/CD step (GitHub Actions, Azure Pipelines) that runs `npm run build` and deploys the output.

---

## Priority 3 — Code quality improvements

### 3.1 Align frontend state type with backend enum

**Problem:** The frontend uses a TypeScript union type `type DatasetState = 'Draft' | 'PendingApproval' | 'Official'` (string literals), while the backend uses a `DatasetState` enum. If a backend developer renames a state value, the frontend will break silently at runtime rather than at compile time.

**File:** [frontend/src/app/models.ts](frontend/src/app/models.ts)

**What to do:**
- Use a TypeScript `const` enum or a string enum:
  ```typescript
  export const enum DatasetState {
    Draft = 'Draft',
    PendingApproval = 'PendingApproval',
    Official = 'Official',
  }
  ```
- Update all usages in `app.ts` to use the enum rather than raw strings.
- Consider generating the TypeScript models from the OpenAPI spec (e.g. with `openapi-typescript`) so they stay in sync automatically.

---

### 3.2 Eliminate the duplicated "WithInternalInfo" methods

**Problem:** `IDatasetService` has paired methods for almost every query:
- `GetInstancesAsync` / `GetInstancesWithInternalInfoAsync`
- `GetHeadersAsync` / `GetHeadersWithInternalInfoAsync`
- `GetLatestInstanceAsync` / `GetLatestInstanceWithInternalInfoAsync`

This doubles the number of methods and means any logic change must be applied twice.

**What to do:**
- Merge each pair into one method that always uses the traced path internally.
- Return the richer response type (with `InternalInfo`) always; when `includeInternalInfo` is `false`, simply omit the `InternalInfo` field from the response (or make the controller strip it before returning).

---

### 3.3 Replace magic string "anonymous" with a constant

**Problem:** The string `"anonymous"` is hardcoded in `RequestUserContextAccessor` and likely compared or logged elsewhere.

**File:** [RequestUserContextAccessor.cs](backend/src/DatasetPlatform.Api/Infrastructure/RequestUserContextAccessor.cs:42)

**What to do:**
```csharp
// In UserContext or a shared constants class:
public const string AnonymousUserId = "anonymous";
```

---

### 3.4 Move `DatasetHeaderPartitioning` to the Application layer

**Problem:** `DatasetHeaderPartitioning` contains pure domain/algorithmic logic (state token normalisation, header hash computation, date partition candidacy) but lives in the `DatasetPlatform.Api` infrastructure layer. This makes it harder to test in isolation and violates the layering principle.

**Files:** [DatasetHeaderPartitioning.cs](backend/src/DatasetPlatform.Api/Infrastructure/DatasetHeaderPartitioning.cs), [DatasetService.cs](backend/src/DatasetPlatform.Application/Services/DatasetService.cs)

**What to do:**
- Move to `DatasetPlatform.Application/Services/` or a new `DatasetPlatform.Application/Partitioning/` folder.
- Remove the `DatasetPlatform.Api` dependency on this type from `DatasetService`.

---

### 3.5 Replace file-based diagnostics log with structured logging

**Problem:** `ApiDiagnosticsMiddleware` writes a custom plain-text log format to a local file (`apiDiag.log`). This works in development but is incompatible with containerised deployments, log aggregation tools (Splunk, Datadog, Azure Monitor), and structured search.

**File:** [ApiDiagnosticsMiddleware.cs](backend/src/DatasetPlatform.Api/Infrastructure/ApiDiagnosticsMiddleware.cs)

**What to do:**
- Switch to `ILogger<T>` with structured log properties.
- Add OpenTelemetry (`dotnet add package OpenTelemetry.Extensions.Hosting`) for distributed tracing and metrics.
- Configure a log exporter (e.g. `Serilog` → Azure Application Insights / Seq / Grafana Loki).
- Keep the file-based log as a development-only fallback (controlled by `ApiDebug:Enabled`).

---

### 3.6 Add a CI/CD pipeline

**Problem:** There are no automated build or test pipelines. Changes could break tests or fail to build without anyone noticing until manual testing.

**What to do:** Add a GitHub Actions workflow (`.github/workflows/ci.yml`) that runs on every pull request:

```yaml
jobs:
  backend:
    steps:
      - dotnet restore
      - dotnet build --no-restore
      - dotnet test --no-build

  frontend:
    steps:
      - npm ci
      - npm run build
      - npm test -- --watch=false
```

---

### 3.7 Document the OpenAPI specification

**Problem:** The API exposes an OpenAPI endpoint (`/openapi`) in development, but controllers have no `[ProducesResponseType]` attributes or XML doc integration, so the generated spec is bare.

**What to do:**
- Add `/// <summary>` XML doc to controller action methods (already partially done).
- Add `[ProducesResponseType(typeof(DatasetInstance), 200)]` and `[ProducesResponseType(400)]` attributes.
- Enable XML documentation generation in the `.csproj`:
  ```xml
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  ```
- Wire the XML file into Swagger/OpenAPI in `Program.cs`.

---

## Summary table

| # | Issue | Effort | Priority |
|---|---|---|---|
| 1.1 | Real authentication (OIDC/JWT) | Large | Critical |
| 1.2 | Restrict CORS policy | Small | Critical |
| 1.3 | Optimistic locking / concurrency | Medium | High |
| 1.4 | Multi-instance deployment safety | Large | High |
| 2.1 | Split DatasetService | Medium | High |
| 2.2 | Correct HTTP status codes (404 vs 400) | Small | High |
| 2.3 | Global exception handler | Small | Medium |
| 2.4 | Controller input validation | Small | Medium |
| 2.5 | Integration & controller tests | Large | Medium |
| 2.6 | Gitignore `data/` folder | Trivial | Medium |
| 2.7 | Gitignore `frontend/dist/` | Trivial | Medium |
| 3.1 | Align frontend state type with backend | Small | Medium |
| 3.2 | Eliminate duplicate WithInternalInfo methods | Medium | Low |
| 3.3 | Replace magic string "anonymous" | Trivial | Low |
| 3.4 | Move DatasetHeaderPartitioning to Application layer | Small | Low |
| 3.5 | Structured logging / OpenTelemetry | Large | Medium |
| 3.6 | CI/CD pipeline | Medium | High |
| 3.7 | Document OpenAPI spec | Small | Low |
