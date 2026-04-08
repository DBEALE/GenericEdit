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

## Frontend TypeScript Models (`models.ts`)

```typescript
export type DatasetState = string;  // not an enum — free-text with suggestions

export interface SchemaField {
  name: string;
  label: string;
  type: 'String' | 'Number' | 'Date' | 'Boolean' | 'Select' | 'Lookup';
  isKey: boolean;
  required: boolean;
  maxLength?: number;
  minValue?: number;
  maxValue?: number;
  allowedValues: string[];
  lookupDatasetKey?: string;
}

export interface DatasetPermissions {
  readRoles: string[];
  writeRoles: string[];
  signoffRoles: string[];
  datasetAdminRoles: string[];
}

export interface DatasetSchema {
  key: string;
  name: string;
  description: string;
  headerFields: SchemaField[];
  detailFields: SchemaField[];
  permissions: DatasetPermissions;
}

export interface DatasetInstance {
  id: string;
  datasetKey: string;
  asOfDate: string;         // YYYY-MM-DD
  state: DatasetState;
  version: number;
  header: Record<string, unknown>;
  rows: Record<string, unknown>[];
  createdBy?: string;
  createdAtUtc?: string;
  lastModifiedBy: string;
  lastModifiedAtUtc: string;
}

export interface DatasetHeaderSummary {  // same as DatasetInstance but without rows
  id: string;
  datasetKey: string;
  asOfDate: string;
  state: DatasetState;
  version: number;
  header: Record<string, unknown>;
  createdBy?: string;
  createdAtUtc?: string;
  lastModifiedBy: string;
  lastModifiedAtUtc: string;
}

export interface DatasetInternalInfo {
  loadedFilenames: Array<{ fileName: string; reason: string }>;
  searchEfficiency?: {
    headerFilesRead: number;
    headerFilesTotal: number;
    detailFilesRead: number;
    detailFilesTotal: number;
    candidateHeaderFilesConsidered: number;
    matchedInstanceFileCount: number;
    headerFilesRebuilt: number;
    usedFilteredSearchPath: boolean;
  };
}

export interface DatasetInstancesQueryResponse { items: DatasetInstance[]; internalInfo?: DatasetInternalInfo; }
export interface DatasetHeadersQueryResponse   { items: DatasetHeaderSummary[]; internalInfo?: DatasetInternalInfo; }
export interface DatasetLatestInstanceQueryResponse { item: DatasetInstance | null; internalInfo?: DatasetInternalInfo; }

export interface AuditEvent {
  id: string;
  occurredAtUtc: string;
  userId: string;
  action: string;
  datasetKey: string;
  datasetInstanceId?: string;
  rowChanges?: AuditRowChange[];
}

export interface AuditRowChange {
  operation: 'added' | 'removed' | 'updated' | string;
  keyFields: Record<string, string>;
  sourceValues?: Record<string, string>;
  targetValues?: Record<string, string>;
}
```

---

## Frontend Structure

### Root Component (`app.ts`)
Single large standalone component (~2200 lines) managing all state via Angular Signals. Intentionally monolithic — all state co-located. Only the Query Page is a separate component.

**Imports:**
```typescript
import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { CellClassParams, ColDef, GetRowIdParams, GridApi, GridReadyEvent,
  ICellEditorComp, ICellEditorParams, ICellRendererParams,
  ValueParserParams, ValueSetterParams } from 'ag-grid-community';
```

**Internal types:**
```typescript
type DetailGridRow = Record<string, unknown> & { __rowKey: string };
type UserSimulationPreset = { label: string; userId: string; roles: string[] };
type SavedHeaderDatePreset = 'Last month' | 'Last 3 months' | 'Last 6 months' | 'Last 1 year';
type SignoffReviewItem = { id: string; occurredAtUtc: string; userId: string; action: string; rowChanges?: AuditRowChange[] };
type AuditDetailGridRow = { rowKey: string; operation: string; keyValues: Record<string,string>; beforeValues: Record<string,string>; afterValues: Record<string,string> };
type SignoffAuditGridRow = AuditDetailGridRow & { userId: string; occurredAtUtc: string };
```

**All signals on the component:**
```typescript
// Navigation & theme
currentPage = signal<'editor' | 'query'>('editor')
theme = signal<'dark' | 'light'>('dark')   // default dark; persisted to localStorage

// User simulation
userId = signal('Admin')
roleInput = signal('Reader,Writer,Approver,Admin,DatasetAdmin')
userSimulationCollapsed = signal(true)

// Schemas
schemas = signal<DatasetSchema[]>([])
datasetSearch = signal('')
selectedSchemaKey = signal<string>('')
// computed:
selectedSchema = computed(() => schemas().find(x => x.key === selectedSchemaKey()) ?? null)
filteredSchemas = computed(() => /* search + sort by name */)
canCreateSchema = computed(() => hasRole('DatasetAdmin'))
canEditSchema = computed(() => /* DatasetAdmin or dataset-level admin */)
grantedDatasetRoles = computed(() => /* ['DatasetAdmin','Read','Write','Signoff'] as applicable */)
canWriteData = computed(() => grantedDatasetRoles().includes('Write'))
canSignoff = computed(() => grantedDatasetRoles().includes('Signoff'))

// Editor state
asOfDate = signal(todayDate())
state = signal<DatasetState>('Draft')
currentInstance = signal<DatasetInstance | null>(null)
headerDraft = signal<Record<string, unknown>>({})
rowDrafts = signal<Record<string, unknown>[]>([])
detailGridRows = signal<DetailGridRow[]>([])
isEditorFormVisible = signal(false)
showValidation = signal(false)
lookupPermissibleValues = signal<Record<string, string[]>>({})

// Computed change detection
hasHeaderChanges = computed(() => /* compare asOfDate, state, headerDraft vs currentInstance */)
hasDetailChanges = computed(() => /* compare rowDrafts vs currentInstance.rows */)
hasEditorChanges = computed(() => hasHeaderChanges() || hasDetailChanges())
validationMessages = computed(() => collectValidationMessages())

// ag-Grid column definitions (computed, schema-driven)
headerGridRows = computed(() => [{ asOfDate, state, ...headerDraft }])
headerColumnDefs = computed<ColDef[]>(() => /* asOfDate + state + headerFields */)
detailColumnDefs = computed<ColDef[]>(() => /* detailFields + delete column */)

// Instances list
datasetInstances = signal<DatasetHeaderSummary[]>([])
selectedInstanceId = signal('')
instanceSortCol = signal<string>('asOfDate')
instanceSortDir = signal<'asc' | 'desc'>('desc')
instanceFilterAsOfDate = signal('')
instanceFilterState = signal('')
instanceFilterHeader = signal('')
instanceDatePreset = signal<SavedHeaderDatePreset>('Last month')
instanceFilterAsOfDateMinDraft = signal('')
instanceFilterAsOfDateMaxDraft = signal('')
instanceFilterAsOfDateMin = signal('')
instanceFilterAsOfDateMax = signal('')
sortedInstances = computed(() => /* filter + sort datasetInstances */)
instancesHiddenCount = computed(() => datasetInstances().length - sortedInstances().length)
hasInstanceFilters = computed(() => /* any filter active */)

// Tabs
activeTab = signal<'editor' | 'schema' | 'audit'>('editor')

// Schema builder
schemaEditorJson = signal<string>('')
schemaBuilderDraft = signal<DatasetSchema | null>(null)
schemaEditorJsonError = signal<string>('')
schemaJsonPaneCollapsed = signal(false)

// Audit
auditEvents = signal<AuditEvent[]>([])
selectedAuditEventId = signal('')
filteredAuditEvents = computed(() => /* filter by selectedSchemaKey */)
selectedAuditEvent = computed(() => /* first or matching by id */)
selectedAuditDetailRows = computed(() => parseAuditDetailGridRows(selectedAuditEvent()))
selectedAuditKeyColumns / selectedAuditUpdatedRows / selectedAuditAddedRows / selectedAuditDeletedRows (computed)
selectedAuditUpdatedValueColumns / selectedAuditAddedValueColumns / selectedAuditDeletedValueColumns (computed)
selectedAuditHasTabularDetails = computed(() => selectedAuditDetailRows().length > 0)

// Signoff review dialog
signoffReviewDialogVisible = signal(false)
signoffReviewSummary = signal('')
signoffReviewItems = signal<SignoffReviewItem[]>([])
signoffAuditRows / signoffAuditKeyColumns / signoffAuditUpdatedRows / signoffAuditAddedRows / signoffAuditDeletedRows (computed)
signoffAuditHasTabularDetails (computed)

// Status / toasts
status = signal<string>('Ready')
isLoadingOverlayVisible = computed(() => status().toLowerCase().startsWith('loading'))
loadingOverlayMessage = computed(() => status() || 'Loading data...')
statusToast = signal('')
error = signal<string>('')
blockingDialogVisible = signal(false)
blockingDialogTitle = signal('Action blocked')
blockingDialogMessage = signal('')
saveNotice = signal('')
internalInfoStatus = signal('')

// ag-Grid API references
popupParent: HTMLElement = document.body  // renders popups at document level to avoid clipping
private headerGridApi = signal<GridApi | null>(null)
private gridApi = signal<GridApi | null>(null)
```

**User simulation presets (hardcoded):**
```typescript
userSimulationPresets = [
  { label: 'Lookup reader',   userId: 'lookup-reader',   roles: ['LookupReader'] },
  { label: 'Lookup writer',   userId: 'lookup-writer',   roles: ['LookupWriter'] },
  { label: 'Lookup approver', userId: 'lookup-approver', roles: ['LookupApprover'] },
  { label: 'Lookup admin',    userId: 'lookup-admin',    roles: ['LookupAdmin'] },
  { label: 'FX superuser',    userId: 'fx-superuser',    roles: ['FXWriter','FXAdmin'] },
  { label: 'Dataset admin',   userId: 'dataset-admin',   roles: ['DatasetAdmin'] }
]
```

**Constructor:**
```typescript
constructor() {
  const savedTheme = this.readSavedTheme();  // localStorage 'theme'
  this.theme.set(savedTheme);
  this.applySavedHeaderDatePreset('Last month', true);  // sets default date range
  this.loadSchemas();
}
```

---

### Key Methods

**Schema loading and selection:**
- `loadSchemas()` — calls `GET /api/schemas`, sets `schemas`, auto-selects first schema, updates `schemaBuilderDraft`
- `selectSchema(key)` — confirms discard of pending changes, loads instances, resets editor to blank draft
- `createNewDataset()` — prompts for key (uppercase, `[A-Z0-9_]+`) and name, calls `upsertSchema`, opens schema editor tab
- `deleteDatasetSchema(key, event)` — confirms, calls `deleteSchema`, removes from list

**Instance management:**
- `loadDatasetInstances(showStatus?)` — calls `GET /headers` with date range filters; lazy update `datasetInstances`
- `loadInstanceIntoEditor(summary)` — calls `GET /instances/{id}`, populates `headerDraft`, `rowDrafts`, `detailGridRows`; sets `isEditorFormVisible = true`
- `startNewHeaderDraft()` — resets all draft state to empty, sets `isEditorFormVisible = true`
- `deleteInstanceHeader(summary, event)` — confirms, calls `deleteInstance`, clears editor if current
- `loadLatest()` — calls `GET /instances/latest?asOfDate=&state=`, loads into editor
- `mergeInstanceIntoList(instance)` — updates `datasetInstances` in-place after save (avoids server round-trip)

**Save / Create / Update:**
- `saveData()` — delegates to `createInstance()` or `saveEdit()` based on whether `currentInstance` is set
- `createInstance()` — validates, calls `POST /instances`, merges into list
- `saveEdit()` — validates, calls `PUT /instances/{id}`, merges into list
- `saveAsCopy()` — calls `POST /instances` with `resetVersion: true`, creating a fresh copy

**Signoff workflow:**
1. `signoff()` — checks `canSignoff()`, loads audit for dataset
2. Calls `evaluateSignoffEligibility(audit, instanceId, userId)` — blocks if current user made changes since last signoff (four-eyes principle)
3. Calls `buildSignoffReview(audit, instanceId)` — finds all create/update events since last signoff, builds summary
4. Shows `signoffReviewDialogVisible` with list of changes and contributor names
5. `confirmSignoffReview()` → `executeSignoff()` → calls `POST /instances/{id}/signoff`

**Four-eyes enforcement (client-side):**
`evaluateSignoffEligibility` blocks approval if the approver's `userId` appears in any `INSTANCE_CREATE`/`INSTANCE_UPDATE` event since the last `INSTANCE_SIGNOFF`. Returns `{ allowed: false, reason: "..." }` if blocked.

**Row operations:**
- `addRow()` — appends empty row to grid via `api.applyTransaction({ add: [newRow] })`
- `deleteDetailRow(rowKey)` — removes by `__rowKey` from signal and ag-Grid
- `removeSelectedRow()` — removes ag-Grid selected row
- `deleteAllDetailRows()` — confirms, clears grid

**CSV import/export:**
- `importCsvFromClipboard()` — reads clipboard text, calls `appendRowsFromCsv()`
- `importCsvFile(event)` — file input handler, reads via `FileReader`
- `copyDetailRowsAsCsv()` — collects current rows, builds CSV, writes to clipboard
- `appendRowsFromCsv(text, schema, source)` — detects delimiter (tab/comma/semicolon by frequency), parses RFC 4180 CSV, maps columns to schema fields by name or label (case-insensitive, underscore/space-insensitive), appends to existing rows
- `buildDetailRowsCsv(rows, schema)` — header row (field names) + data rows

**Schema builder:**
- `openSchemaEditorTab()` — sets `activeTab = 'schema'`, ensures `schemaBuilderDraft`
- `saveSchemaEditor()` — parses JSON, calls `upsertSchema`, reloads schemas
- `onSchemaEditorJsonChange(value)` — parses JSON and syncs to form controls; sets `schemaEditorJsonError` on parse failure
- `updateSchemaMeta(field, value)` — updates `schemaBuilderDraft`, syncs JSON
- `updatePermissionUsers(kind, value)` — parses comma list, updates permissions, syncs JSON
- `addSchemaField(section)` — appends default field (`String` type, `required: true`, `isKey: true` for detail), syncs JSON
- `removeSchemaField(section, index)` — removes field by index, syncs JSON
- `updateSchemaField(section, index, field, value)` — updates single field property with coercion (type→clears allowedValues/lookupDatasetKey, numeric fields→parsed, booleans→coerced)
- `toggleSchemaJsonPane()` — collapses/expands JSON side panel
- `copySchemaJsonToClipboard()` — copies schema JSON text

**Schema normalization (`normalizeSchema`):**
- Always uppercases `key`
- Handles legacy `readUsers`/`writeUsers`/`signoffUsers`/`datasetAdminUsers` → new `readRoles`/etc.
- Handles legacy `keyFields` array → sets `isKey: true` on matching detail fields
- Strips unrecognized field types back to `'String'`
- Clears `allowedValues` if type is not Select; clears `lookupDatasetKey` if type is not Lookup

**Audit tab:**
- `activateAuditTab()` — sets `activeTab = 'audit'`, calls `loadAudit()`
- `loadAudit()` — calls `GET /audit?datasetKey=`, sets `auditEvents`
- `selectAuditEvent(id)` — sets `selectedAuditEventId`
- Audit detail computed as matrix of: key columns + operation (Added/Updated/Removed) + before/after value columns

**ag-Grid integration:**
- `onGridReady(event)` / `onHeaderGridReady(event)` — captures `GridApi` reference
- `onGridCellValueChanged()` — iterates all nodes to sync `detailGridRows` and `rowDrafts`
- `onHeaderGridCellValueChanged()` — reads first row, splits out `asOfDate`/`state`, updates signals
- `getDetailRowId(params)` — returns `__rowKey` for stable row identity
- `refreshGridRows()` / `refreshHeaderGridRows()` — calls `api.refreshCells({ force: true })`
- `collectCurrentRows()` — calls `api.stopEditing()` then iterates nodes; used before save/validate
- `collectCurrentHeader()` — calls `api.stopEditing()` then reads row 0; used before save/validate
- Both grids use `popupParent = document.body` so cell editor popups aren't clipped

**Validation (`collectValidationMessages`):**
- Runs before every save/signoff (`validateBeforeSubmit()`)
- Checks header fields and detail rows against schema constraints
- Sets `showValidation = true` to render inline cell highlighting (`cell-invalid` CSS class)
- Returns array of error strings; if non-empty, blocks submission

**Column definitions (computed, schema-driven):**
- Header grid: `asOfDate` (date string editor) + `state` (StateHybridCellEditor) + HeaderFields
- Detail grid: DetailFields + fixed delete column (pinned right, 52px, custom button renderer)
- For `Select`/`Lookup` fields with options: uses `LargeListCellEditor` (popup, `cellEditorPopupPosition: 'under'`)
- For `Boolean` fields: uses `LargeListCellEditor` with values `['true', 'false']`
- All cells: `cellClassRules: { 'cell-invalid': ... }` for validation highlighting
- All columns: `singleClickEdit: true`, `resizable: true`, `sortable: true`

**Lookup values loading:**
- On `selectSchema()`, scans all header+detail fields for type `Lookup`
- Fires parallel `GET /lookups/{key}/values` requests for each unique lookup dataset
- Populates `lookupPermissibleValues` signal keyed by dataset key
- Uses request ID guard to discard stale responses on rapid schema switches

**Unsaved-changes guard:**
- `confirmDiscardPendingChanges()` — compares current draft vs baseline; prompts if changed
- Called on `selectSchema()` and `loadInstanceIntoEditor()` to prevent accidental data loss

**Theme persistence:**
- `toggleTheme()` — toggles dark/light, saves to `localStorage`
- `readSavedTheme()` — reads from `localStorage`, defaults to `'dark'`

**Date preset calculation (`getSavedHeaderPresetRange`):**
- 'Last month' → `minAsOfDate = today - 1 month`
- 'Last 3 months' → `minAsOfDate = today - 3 months`
- 'Last 6 months' → `minAsOfDate = today - 6 months`
- 'Last 1 year' → `minAsOfDate = today - 1 year`
- All presets set `maxAsOfDate = today`
- `applySavedHeaderDatePreset(preset, autoSearch)` — updates draft and optionally fires search

---

### Custom ag-Grid Cell Editors

**`StateHybridCellEditor` (implements `ICellEditorComp`):**
```typescript
// Text input with attached <datalist> for suggestions (not enforced)
// Suggestions: 'Draft', 'PendingApproval', 'Pending Approval', 'Official', 'Scenario Testing'
// init(): builds div > input[list=uniqueId] + datalist
// afterGuiAttached(): focus + select all
// getValue(): input.value.trim()
```

**`LargeListCellEditor` (implements `ICellEditorComp`):**
```typescript
// Container div with styled border/shadow + <select size=N> (N = min(max(values.length,4),10))
// Container style: minWidth 18rem, maxWidth 28rem, white background, border #9dc0d2, border-radius 0.35rem
// Select style: width 100%, no border, fontSize 0.95rem
// If current value not in list, inserts it as first option
// keydown 'Enter': stopPropagation (prevent grid closing)
// dblclick: calls params.stopEditing() (commit on double-click)
// afterGuiAttached(): select.focus()
// getValue(): select.value
// params.values: string[] injected via cellEditorParams
```

---

### CSS Design System (`app.scss`)

All colors are CSS custom properties scoped to `:host`. Theme switching adds `.theme-dark` or `.theme-light` to `.page-shell`.

#### Light Theme (default on `:host`)
```scss
--bg0: #f5f4ef
--bg1: #fffdf8
--panel: #ffffff
--panel-soft: color-mix(in oklab, var(--panel) 94%, var(--bg0))
--panel-strong: color-mix(in oklab, var(--panel) 86%, var(--bg0))
--ink: #15222d
--muted: #5f6c77
--accent: #005f73
--accent-soft: #d9f2f5
--danger: #9f2a2a
--success: #1f6f3a
--warning: #b54708
--ink-on-accent: #ffffff
--ink-on-danger: #ffffff
--border: #dae2e8
--focus-ring: color-mix(in oklab, var(--accent) 26%, transparent)
--shadow: 0 18px 45px rgba(0, 47, 73, 0.1)
```

Background (light):
```scss
background:
  radial-gradient(circle at 15% 10%, rgba(0, 95, 115, 0.11), transparent 35%),
  radial-gradient(circle at 80% 0%, rgba(238, 155, 0, 0.14), transparent 30%),
  linear-gradient(180deg, var(--bg0), var(--bg1));
```

#### Dark Theme (`.page-shell.theme-dark`)
```scss
--bg0: #1d262c
--bg1: #232830
--panel: #252b33
--panel-soft: #21272f
--panel-strong: #2a3039
--ink: #e4e8ed
--muted: #c2cad3
--accent: #8ab8cf
--accent-soft: #313b47
--danger: #ff6d6d
--success: #6bd596
--warning: #f8b56c
--ink-on-accent: #f7fbff
--ink-on-danger: #fff5f5
--border: #3a434e
--focus-ring: color-mix(in oklab, var(--accent) 34%, transparent)
--shadow: 0 8px 20px rgba(0, 0, 0, 0.24)
background: linear-gradient(180deg, var(--bg0), var(--bg1));
```

#### Global base styles
```scss
font-family: 'Segoe UI', 'Helvetica Neue', Arial, sans-serif
input, select, textarea: border 1px solid var(--border), border-radius 0.45rem, padding 0.4rem 0.5rem, background var(--panel), color var(--ink)
textarea: font-family 'Lucida Console', Monaco, monospace; min-height 260px
button: border-radius 0.35rem, min-height 1.9rem, padding 0.25rem 0.65rem, background var(--accent), color var(--ink-on-accent)
button.danger: background var(--danger), color var(--ink-on-danger)
button.mini: min-height 1.55rem, padding 0.18rem 0.5rem, font-size 0.74rem
button:disabled: opacity 0.45, cursor not-allowed
table: border-collapse collapse, font-family 'Lucida Console' Monaco monospace, font-size 0.8rem
th, td: border-bottom 1px solid var(--border), padding 0.3rem 0.35rem
.invalid-input: border-color var(--danger), background color-mix(danger 7%, panel)
```

#### Layout classes
```scss
.page-shell: min-height 100vh
.hero: flex, justify-between, padding 0.75rem 1rem, background var(--panel-strong), border-bottom 1px solid var(--border)
.eyebrow: font-size clamp(1.22rem, 1.1vw + 0.95rem, 1.5rem), font-weight 560
.page-nav: flex, border-bottom 1px solid var(--border), padding 0 1rem, background var(--panel-soft)
.nav-tab: border-bottom 3px solid transparent; .active-nav: border-bottom-color var(--ink)
.grid-layout: grid, grid-template-columns 285px minmax(0,1fr), gap 0.6rem, padding 0 0.7rem 0.7rem
.catalog: left panel (285px); .editor: right panel
.panel: border 1px solid var(--border), background var(--panel), border-radius 0.45rem, box-shadow var(--shadow), padding 0.65rem
```

#### Dataset catalog
```scss
.catalog-create-btn: width 100%, margin-bottom 0.7rem
.catalog-search-wrap: relative, with .catalog-search-clear at right
.dataset-list: grid, gap 0.5rem
.dataset-entry: grid, grid-template-columns minmax(0,1fr) auto
.dataset-item: border 1px solid var(--border), background var(--panel), border-radius 0.7rem, padding 0.6rem 0.75rem
  strong: font-size 1.02rem, font-weight 700
  span: font-size 0.9rem, color var(--muted)
.dataset-item.active:
  border-color var(--accent)
  background: color-mix(accent-soft 72%, panel)
  box-shadow: inset 0 0 0 1px color-mix(accent 65%, border)
  strong: font-weight 800
  span: color color-mix(accent 85%, ink)
```

#### Instances header grid (saved headers table)
```scss
.instance-header-grid-wrap: max-height clamp(150px,20vh,280px), overflow auto, border 1px solid var(--border), border-radius 0.7rem, background var(--panel-soft)
.instance-header-grid: border-collapse separate, border-spacing 0, font-family 'Lucida Console', font-size 0.76rem
thead th: sticky top, background var(--panel-strong), border-bottom 1px solid var(--border)
thead th.sortable-col: cursor pointer, user-select none; hover: background accent-soft 62%
tr.filter-row th: background var(--panel-soft), border-bottom 2px solid var(--border)
.col-filter: border 1px solid var(--border), border-radius 0.4rem, font-size 0.7rem; focus: border-color accent, box-shadow focus-ring
tbody tr: cursor pointer
even rows (not active): color-mix(panel 88%, panel-soft)
odd rows (not active): color-mix(panel 95%, bg0)
hover: color-mix(accent-soft 62%, panel)
.active-instance-row: color-mix(accent-soft 74%, panel)
dark theme overrides:
  thead/th/td: color-mix(panel 86%, black)
  hover td: color-mix(accent-soft 52%, panel)
  active-instance-row td: color-mix(accent-soft 72%, panel) + box-shadow inset accent 58%
```

#### Editor grids
```scss
.header-table-wrap: overflow-x auto, border 1px solid var(--border), border-radius 0.8rem, height 74px (header row + one data row)
.table-wrap: overflow hidden, border 1px solid var(--border), border-radius 0.8rem, height clamp(300px,48vh,70vh)
```

#### ag-Grid theme overrides
```scss
// Applied via :host ::ng-deep .ag-theme-quartz
--ag-grid-size: 4px
--ag-list-item-height: 24px
--ag-header-height: 28px
--ag-row-height: 24px
--ag-font-size: 12px

// Dark mode (inside .page-shell.theme-dark .ag-theme-quartz):
--ag-background-color: color-mix(panel 92%, black)
--ag-foreground-color: var(--ink)
--ag-data-color: var(--ink)
--ag-header-text-color: var(--ink)
--ag-header-background-color: color-mix(panel 82%, black)
--ag-odd-row-background-color: color-mix(panel 88%, black)
--ag-row-hover-color: color-mix(accent-soft 46%, panel)
--ag-selected-row-background-color: color-mix(accent-soft 62%, panel)
--ag-input-background-color: color-mix(panel 95%, black)
--ag-input-text-color: var(--ink)
--ag-input-border-color: var(--border)
--ag-border-color: var(--border)
```

#### Cell class rules (validation + audit)
```scss
// Validation
.cell-invalid: background color-mix(danger 12%, panel), box-shadow inset danger 42%

// Audit operation type (row operation column)
.audit-cell-op: font-weight 700
.audit-cell-op-added: background color-mix(success 20%, panel)  // dark: 24%
.audit-cell-op-removed: background color-mix(danger 20%, panel)  // dark: 24%
.audit-cell-op-updated: background color-mix(warning 20%, panel)  // dark: 24%

// Audit value cells
.audit-cell-added: background color-mix(success 15%, panel), box-shadow inset success 40%
.audit-cell-removed: background color-mix(danger 15%, panel), box-shadow inset danger 42%
.audit-cell-updated-before: background color-mix(warning 12%, panel), box-shadow inset warning 35%
.audit-cell-updated-after: background color-mix(success 12%, panel), box-shadow inset success 35%
```

#### Tab styles (within editor panels)
```scss
.tab-row: flex, gap 0.5rem, border-bottom 1px solid var(--border)
.tab: transparent bg, color var(--muted), border-bottom 3px solid transparent, min-height 1.8rem
.tab.active-tab: color var(--ink), border-bottom-color var(--ink)
```

#### Toast notifications
```scss
.toast-stack: fixed, left 50%, bottom 1rem, translateX(-50%), width min(92vw,760px), z-index 3000, pointer-events none
.toast: border-radius 0.75rem, padding 0.65rem 0.9rem, font-weight 600, backdrop-filter blur(2px)
.info-toast: background accent-soft 74%, border accent 34%, color var(--accent)
.success-toast: background success 12%, border success 32%, color var(--success)
.error-toast: background danger 12%, border danger 36%, color var(--danger)
```

#### Modal dialogs
```scss
.blocking-dialog-overlay: fixed inset-0, z-index 5200, place-items center, background rgba(20,28,34,0.62), backdrop-filter blur(1px)
.blocking-dialog-card: width min(92vw,860px), max-height min(80vh,720px), overflow auto, border-radius 0.95rem, background var(--panel)
.loading-overlay: fixed inset-0, z-index 4000, place-items center, background rgba(23,29,35,0.46), backdrop-filter blur(1.5px) grayscale(0.35)
.loading-overlay-card: min-width min(86vw,520px), border-radius 0.9rem, font-weight 700
.signoff-review-card: max-height min(84vh,760px)
.signoff-review-list: max-height min(52vh,520px), overflow auto, border 1px solid var(--border), border-radius 0.7rem, background var(--panel-soft)
```

#### Identity / user simulation card
```scss
.identity-card: border 1px solid var(--border), background var(--panel), border-radius 0.45rem, padding 0.5rem 0.7rem, max-width 360px
.simulation-presets: flex, flex-wrap wrap, gap 0.4rem
.simulation-preset-btn: background accent-soft, color var(--accent), border 1px solid accent 25%, border-radius 999px, font-size 0.82rem
```

#### Pill / role badges
```scss
.pill: border 1px solid accent 26%, background accent-soft 70%, color var(--accent), border-radius 999px, padding 0.15rem 0.55rem, font-size 0.82rem
.pill.muted: border-color var(--border), background var(--panel-soft), color var(--muted)
.role-pills: flex, flex-wrap wrap, gap 0.4rem
```

#### Validation panel
```scss
.validation-panel: border 1px solid danger 40%, background danger 10%, border-radius 0.8rem, padding 0.6rem 0.75rem
```

#### Schema builder layout
```scss
.schema-layout: grid, grid-template-columns minmax(0,1fr) minmax(170px,0.48fr); .json-collapsed: 1fr 220px
.schema-builder-grid: grid, grid-template-columns 1fr 1.4fr 1.2fr
.schema-builder-panel: border 1px solid var(--border), border-radius 0.7rem, background var(--panel-soft)
.schema-field-card: position relative, border 1px solid var(--border), border-radius 0.55rem, padding 1.75rem 0.45rem 0.45rem
.schema-field-grid: grid, grid-template-columns repeat(2, 1fr)
.schema-json-panel: position sticky, top 0.5rem
.schema-editor-area: width 100%, min-height calc(100vh - 250px), max-height calc(100vh - 250px), resize none
.schema-dataset-grid: grid, grid-template-columns minmax(0,1fr) minmax(260px,0.9fr)
.schema-dataset-roles: padding-left 0.65rem, border-left 1px solid var(--border)
```

#### Audit layout
```scss
.audit-layout: grid, grid-template-columns minmax(260px,0.9fr) minmax(0,1.7fr)
.audit-event-btn: width 100%, text-align left, border 1px solid var(--border), border-radius 0.7rem, background var(--panel-soft)
.audit-event-btn.active: border-color accent, box-shadow inset accent 50%, background accent-soft 56%
.audit-details-panel: border 1px solid var(--border), border-radius 0.7rem, background var(--panel-soft)
.audit-grid-wrap: border 1px solid var(--border), border-radius 0.55rem, max-height clamp(220px,34vh,380px)
.audit-change-table: font-size 0.78rem, thead th sticky with panel-strong bg, tbody td white-space nowrap
```

#### Responsive breakpoint
```scss
@media (max-width: 1200px) {
  .grid-layout: grid-template-columns 1fr, grid-template-areas 'catalog' 'editor'
  .hero: flex-direction column
}
```

#### Delete button in ag-Grid column (injected via cellRenderer)
```scss
// Applied via :host ::ng-deep .ag-theme-quartz button.detail-row-delete-btn
min-height/min-width: 1.08rem, border none, border-radius 0.28rem
background: var(--danger), color #fff, font-size 0.62rem
```

---

### Query Page CSS (`query-page.scss`)

Uses the same CSS custom property tokens. Key classes:
```scss
.query-page: grid, gap 1.25rem, padding-top 0.5rem
.criteria-card: background var(--panel), border 1px solid var(--border), border-radius 1rem, padding 1.25rem, box-shadow var(--shadow)
.criteria-title: color var(--accent), font-size 0.85rem, font-weight 700, uppercase, letter-spacing 0.06em
.field-group: grid, gap 0.3rem, min-width 180px, flex 1, max-width 300px
.field-input: border 1px solid var(--border), border-radius 0.5rem, background var(--panel-soft)
.search-btn: padding 0.5rem 1.75rem, font-weight 700, background var(--accent), color var(--ink-on-accent), border-radius 0.6rem
.http-pill: inline-block, min-width 3.1rem, border-radius 999px, background accent-soft 68%, color var(--accent), font-weight 700
.api-methods-table: font-size 0.75rem
  .api-method-called: animation methodPulse 0.55s ease-out
  .api-method-selected: background accent-soft 78%, box-shadow inset 3px 0 0 accent
.called-badge: border-radius 999px, background warning 30%, color var(--warning), font-size 0.67rem
.method-link: color var(--accent), font-weight 700, text-decoration underline
.api-call-json: border 1px solid var(--border), border-radius 0.55rem, font-size 0.74rem, max-height 255px, overflow auto
.results-table: th uppercase, background panel-strong; tr hover: accent-soft 45%; .selected-row: accent-soft
.header-chip (query page): background accent-soft 68%, color var(--accent), border 1px solid accent 30%
.detail-section/.results-section: background var(--panel), border 1px solid var(--border), border-radius 1rem, box-shadow var(--shadow)
@media (max-width: 1050px): .api-call-panels → grid-template-columns 1fr
```

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
