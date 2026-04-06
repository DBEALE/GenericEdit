# Backend Class Interaction Map

This document summarizes how backend classes and interfaces interact in the `DatasetPlatform` backend.

## 1. Layered Structure

- **API Layer**
  - `Program`
  - `SchemasController`
  - `DatasetInstancesController`
  - `AuditController`
  - `RequestUserContextAccessor`
- **Application Layer**
  - `IDatasetService`
  - `DatasetService`
  - `DatasetAuthorizer`
  - `DatasetServiceException`
  - DTOs: `CreateDatasetInstanceRequest`, `UpdateDatasetInstanceRequest`, `SignoffDatasetRequest`
- **Persistence / Infrastructure Layer**
  - `IDataRepository`
  - `BlobDataRepository`
  - `StorageOptions`
- **Domain Layer**
  - `DatasetSchema`, `SchemaField`, `DatasetPermissions`
  - `DatasetInstance`, `DatasetState`
  - `AuditEvent`, `UserContext`, `FieldType`

## 2. Dependency Graph

```mermaid
flowchart LR
  subgraph API[API Layer]
    Program
    SchemasController
    DatasetInstancesController
    AuditController
    RequestUserContextAccessor
  end

  subgraph APP[Application Layer]
    IDatasetService
    DatasetService
    DatasetAuthorizer
    DatasetServiceException
    CreateReq[CreateDatasetInstanceRequest]
    UpdateReq[UpdateDatasetInstanceRequest]
    SignoffReq[SignoffDatasetRequest]
  end

  subgraph PERSIST[Persistence Layer]
    IDataRepository
    BlobDataRepository
    StorageOptions
  end

  subgraph DOMAIN[Domain Models]
    DatasetSchema
    SchemaField
    DatasetPermissions
    DatasetInstance
    DatasetState
    AuditEvent
    UserContext
    FieldType
  end

  Program -->|DI registration| IDatasetService
  Program -->|DI registration| IDataRepository
  Program -->|DI registration| RequestUserContextAccessor

  SchemasController --> IDatasetService
  DatasetInstancesController --> IDatasetService
  AuditController --> IDatasetService

  SchemasController --> RequestUserContextAccessor
  DatasetInstancesController --> RequestUserContextAccessor
  AuditController --> RequestUserContextAccessor

  IDatasetService --> DatasetService
  DatasetService --> IDataRepository
  DatasetService --> DatasetAuthorizer
  DatasetService --> DatasetServiceException

  DatasetInstancesController --> CreateReq
  DatasetInstancesController --> UpdateReq
  DatasetInstancesController --> SignoffReq

  IDataRepository --> BlobDataRepository
  BlobDataRepository --> StorageOptions

  DatasetService --> DatasetSchema
  DatasetService --> DatasetInstance
  DatasetService --> DatasetState
  DatasetService --> AuditEvent
  DatasetService --> UserContext

  DatasetSchema --> SchemaField
  DatasetSchema --> DatasetPermissions
```

## 3. Runtime Interaction Sequences

### 3.1 Save Existing Instance (Update)

```mermaid
sequenceDiagram
  participant Client
  participant C as DatasetInstancesController
  participant U as RequestUserContextAccessor
  participant S as DatasetService
  participant A as DatasetAuthorizer
  participant R as IDataRepository/BlobDataRepository

  Client->>C: PUT /api/datasets/{datasetKey}/instances/{instanceId}
  C->>U: GetCurrent()
  U-->>C: UserContext
  C->>S: UpdateInstanceAsync(request, user)
  S->>R: GetSchemaAsync(datasetKey)
  S->>A: CanWrite(schema, user)
  S->>R: GetInstanceAsync(datasetKey, instanceId)
  S->>S: ValidateBySchema(header, rows)
  S->>S: Unique-header check (only if identity changed)
  S->>R: ReplaceInstanceAsync(updated)
  S->>R: AddAuditEventAsync(INSTANCE_UPDATE)
  S-->>C: DatasetInstance
  C-->>Client: 200 OK + updated instance
```

### 3.2 Create New Instance

```mermaid
sequenceDiagram
  participant Client
  participant C as DatasetInstancesController
  participant U as RequestUserContextAccessor
  participant S as DatasetService
  participant R as IDataRepository/BlobDataRepository

  Client->>C: POST /api/datasets/{datasetKey}/instances
  C->>U: GetCurrent()
  U-->>C: UserContext
  C->>S: CreateInstanceAsync(request, user)
  S->>R: GetSchemaAsync(datasetKey)
  S->>R: GetInstancesAsync(datasetKey)
  S->>S: EnsureUniqueHeader(...)
  S->>R: SaveInstanceAsync(instance)
  S->>R: AddAuditEventAsync(INSTANCE_CREATE)
  S-->>C: DatasetInstance
  C-->>Client: 200 OK
```

### 3.3 Read Schemas / Authorization

```mermaid
sequenceDiagram
  participant Client
  participant C as SchemasController
  participant U as RequestUserContextAccessor
  participant S as DatasetService
  participant R as IDataRepository/BlobDataRepository
  participant A as DatasetAuthorizer

  Client->>C: GET /api/schemas
  C->>U: GetCurrent()
  U-->>C: UserContext
  C->>S: GetAccessibleSchemasAsync(user)
  S->>R: GetSchemasAsync()
  S->>A: CanRead(schema, user) for each schema
  S-->>C: filtered schemas
  C-->>Client: 200 OK
```

## 4. Key Interaction Notes

- Controllers are thin: they validate route/body consistency, resolve current user context, and delegate to `IDatasetService`.
- `DatasetService` is the orchestration core: authorization, validation, uniqueness checks, versioning, and audit event creation.
- `DatasetAuthorizer` centralizes role/user permission decisions.
- `IDataRepository` is the persistence abstraction; `BlobDataRepository` is the concrete JSON blob implementation (backed by local filesystem or S3).
- `RequestUserContextAccessor` builds `UserContext` from HTTP headers (`x-user-id`, `x-user-roles`).
- Domain models are shared contracts across API, application, and persistence layers.

## 5. Dependency Registration (Program)

- `IDatasetService` -> `DatasetService` (scoped)
- `IDataRepository` -> `BlobDataRepository` (singleton)
- `IRequestUserContextAccessor` -> `RequestUserContextAccessor` (scoped)

This wiring defines the runtime interaction chain used by all backend endpoints.
