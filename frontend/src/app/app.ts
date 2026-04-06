import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { CellClassParams, ColDef, GetRowIdParams, GridApi, GridReadyEvent, ICellEditorComp, ICellEditorParams, ICellRendererParams, ValueParserParams, ValueSetterParams } from 'ag-grid-community';
import { DatasetApiService } from './dataset-api.service';
import { AuditEvent, DatasetHeaderSummary, DatasetHeadersQueryResponse, DatasetInstance, DatasetInternalInfo, DatasetLatestInstanceQueryResponse, DatasetSchema, DatasetState, SchemaField } from './models';
import { QueryPageComponent } from './query-page/query-page';

type DetailGridRow = Record<string, unknown> & { __rowKey: string };
type UserSimulationPreset = { label: string; userId: string; roles: string[] };
type SavedHeaderDatePreset = 'Last month' | 'Last 3 months' | 'Last 6 months' | 'Last 1 year';

class StateHybridCellEditor implements ICellEditorComp {
  private eContainer!: HTMLDivElement;
  private eInput!: HTMLInputElement;

  init(params: ICellEditorParams<Record<string, unknown>, string>): void {
    const listId = `state-options-${Math.random().toString(36).slice(2)}`;

    this.eContainer = document.createElement('div');
    this.eContainer.style.width = '100%';
    this.eContainer.style.height = '100%';

    this.eInput = document.createElement('input');
    this.eInput.type = 'text';
    this.eInput.value = params.value ?? '';
    this.eInput.setAttribute('list', listId);
    this.eInput.style.width = '100%';
    this.eInput.style.height = '100%';
    this.eInput.style.boxSizing = 'border-box';

    const datalist = document.createElement('datalist');
    datalist.id = listId;
    ['Draft', 'PendingApproval', 'Pending Approval', 'Official', 'Scenario Testing'].forEach((state) => {
      const option = document.createElement('option');
      option.value = state;
      datalist.appendChild(option);
    });

    this.eContainer.append(this.eInput, datalist);
  }

  getGui(): HTMLElement {
    return this.eContainer;
  }

  afterGuiAttached(): void {
    this.eInput.focus();
    this.eInput.select();
  }

  getValue(): string {
    return this.eInput.value.trim();
  }
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule, AgGridAngular, QueryPageComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly api = inject(DatasetApiService);
  private readonly includeInternalInfoDebug = (globalThis as { __datasetUiDebugIncludeInternalInfo?: boolean }).__datasetUiDebugIncludeInternalInfo === true;

  readonly currentPage = signal<'editor' | 'query'>('editor');
  readonly userId = signal('Admin');
  readonly roleInput = signal('Reader,Writer,Approver,Admin,DatasetAdmin');
  readonly userSimulationPresets: UserSimulationPreset[] = [
    { label: 'Lookup reader', userId: 'lookup-reader', roles: ['LookupReader'] },
    { label: 'Lookup writer', userId: 'lookup-writer', roles: ['LookupWriter'] },
    { label: 'Lookup approver', userId: 'lookup-approver', roles: ['LookupApprover'] },
    { label: 'Lookup admin', userId: 'lookup-admin', roles: ['LookupAdmin'] },
    { label: 'FX superuser', userId: 'fx-superuser', roles: ['FXWriter', 'FXAdmin'] },
    { label: 'Dataset admin', userId: 'dataset-admin', roles: ['DatasetAdmin'] }
  ];

  readonly schemas = signal<DatasetSchema[]>([]);
  readonly datasetSearch = signal('');
  readonly selectedSchemaKey = signal<string>('');
  readonly selectedSchema = computed(() => this.schemas().find((x) => x.key === this.selectedSchemaKey()) ?? null);
  readonly filteredSchemas = computed(() => {
    const search = this.datasetSearch().trim().toLowerCase();
    const matchingSchemas = search.length === 0
      ? [...this.schemas()]
      : this.schemas().filter((schema) =>
          schema.name.toLowerCase().includes(search) ||
          schema.key.toLowerCase().includes(search));

    return matchingSchemas.sort((left, right) =>
      left.name.localeCompare(right.name, undefined, { sensitivity: 'base' }));
  });

  readonly asOfDate = signal(this.todayDate());
  readonly state = signal<DatasetState>('Draft');

  readonly currentInstance = signal<DatasetInstance | null>(null);
  readonly headerDraft = signal<Record<string, unknown>>({});
  readonly rowDrafts = signal<Record<string, unknown>[]>([]);
  readonly detailGridRows = signal<DetailGridRow[]>([]);

  readonly schemaEditorJson = signal<string>('');
  readonly schemaBuilderDraft = signal<DatasetSchema | null>(null);
  readonly schemaEditorJsonError = signal<string>('');
  readonly lookupPermissibleValues = signal<Record<string, string[]>>({});
  readonly auditEvents = signal<AuditEvent[]>([]);
  readonly filteredAuditEvents = computed(() => {
    const selectedDatasetKey = this.selectedSchemaKey().trim();
    if (selectedDatasetKey.length === 0) {
      return this.auditEvents();
    }

    return this.auditEvents().filter((event) =>
      event.datasetKey.localeCompare(selectedDatasetKey, undefined, { sensitivity: 'accent' }) === 0);
  });
  readonly datasetInstances = signal<DatasetHeaderSummary[]>([]);
  readonly selectedInstanceId = signal('');
  readonly instanceSortCol = signal<string>('asOfDate');
  readonly instanceSortDir = signal<'asc' | 'desc'>('desc');
  readonly instanceFilterAsOfDate = signal('');
  readonly instanceFilterState = signal('');
  readonly instanceFilterHeader = signal('');
  readonly savedHeaderDatePresets: SavedHeaderDatePreset[] = ['Last month', 'Last 3 months', 'Last 6 months', 'Last 1 year'];
  readonly instanceDatePreset = signal<SavedHeaderDatePreset>('Last month');
  readonly instanceFilterAsOfDateMinDraft = signal('');
  readonly instanceFilterAsOfDateMaxDraft = signal('');
  readonly instanceFilterAsOfDateMin = signal('');
  readonly instanceFilterAsOfDateMax = signal('');
  readonly sortedInstances = computed(() => {
    const col = this.instanceSortCol();
    const dir = this.instanceSortDir();
    const fDate = this.instanceFilterAsOfDate().trim().toLowerCase();
    const fState = this.instanceFilterState().trim().toLowerCase();
    const fHeader = this.instanceFilterHeader().trim().toLowerCase();
    let rows = [...this.datasetInstances()];
    if (fDate) rows = rows.filter((r) => r.asOfDate.toLowerCase().includes(fDate));
    if (fState) rows = rows.filter((r) => r.state.toLowerCase().includes(fState));
    if (fHeader) rows = rows.filter((r) =>
      Object.entries(r.header).some(([k, v]) =>
        `${k}: ${v}`.toLowerCase().includes(fHeader)));
    if (!col) return rows;
    return rows.sort((a, b) => {
      let av: unknown;
      let bv: unknown;
      if (col === 'asOfDate') { av = a.asOfDate; bv = b.asOfDate; }
      else if (col === 'state') { av = a.state; bv = b.state; }
      else if (col === 'version') { av = a.version; bv = b.version; }
      else return 0;
      if (av === bv) return 0;
      const cmp = av! < bv! ? -1 : 1;
      return dir === 'asc' ? cmp : -cmp;
    });
  });
  readonly instancesHiddenCount = computed(() =>
    this.datasetInstances().length - this.sortedInstances().length);
  readonly hasInstanceFilters = computed(() =>
    this.instanceFilterAsOfDate().trim().length > 0 ||
    this.instanceFilterAsOfDateMin().trim().length > 0 ||
    this.instanceFilterAsOfDateMax().trim().length > 0 ||
    this.instanceFilterState().trim().length > 0 ||
    this.instanceFilterHeader().trim().length > 0);
  readonly activeTab = signal<'editor' | 'schema' | 'audit'>('editor');
  readonly userSimulationCollapsed = signal(true);
  readonly isEditorFormVisible = signal(false);

  readonly status = signal<string>('Ready');
  readonly isLoadingOverlayVisible = computed(() => this.status().trim().toLowerCase().startsWith('loading'));
  readonly loadingOverlayMessage = computed(() => {
    const text = this.status().trim();
    return text.length > 0 ? text : 'Loading data...';
  });
  readonly statusToast = signal('');
  readonly error = signal<string>('');
  readonly saveNotice = signal('');
  readonly internalInfoStatus = signal('');
  readonly showValidation = signal(false);
  private readonly headerGridApi = signal<GridApi<Record<string, unknown>> | null>(null);
  private readonly gridApi = signal<GridApi<Record<string, unknown>> | null>(null);
  private statusToastTimer?: ReturnType<typeof setTimeout>;
  private saveNoticeTimer?: ReturnType<typeof setTimeout>;
  private errorToastTimer?: ReturnType<typeof setTimeout>;
  private schemaJsonSyncInProgress = false;
  private lookupOptionsRequestId = 0;

  readonly canCreateSchema = computed(() => this.hasRole('DatasetAdmin'));
  readonly canEditSchema = computed(() => {
    if (this.hasRole('DatasetAdmin')) {
      return true;
    }

    const schema = this.selectedSchema();
    if (!schema) {
      return false;
    }

    return this.includesPrincipal(schema.permissions.datasetAdminRoles, this.userId().trim(), this.roles());
  });
  readonly grantedDatasetRoles = computed(() => {
    const schema = this.selectedSchema();
    if (!schema) {
      return [] as string[];
    }

    if (this.hasRole('DatasetAdmin')) {
      return ['DatasetAdmin', 'Read', 'Write', 'Signoff'];
    }

    const granted: string[] = [];
    const user = this.userId().trim();
    if (this.includesPrincipal(schema.permissions.datasetAdminRoles, user, this.roles())) {
      granted.push('DatasetAdmin');
    }

    if (this.includesPrincipal(schema.permissions.readRoles, user, this.roles())) {
      granted.push('Read');
    }

    if (this.includesPrincipal(schema.permissions.writeRoles, user, this.roles())) {
      granted.push('Write');
    }

    if (this.includesPrincipal(schema.permissions.signoffRoles, user, this.roles())) {
      granted.push('Signoff');
    }

    return granted;
  });
  readonly canWriteData = computed(() => this.grantedDatasetRoles().includes('Write'));
  readonly canSignoff = computed(() => this.grantedDatasetRoles().includes('Signoff'));
  readonly hasHeaderChanges = computed(() => {
    const schema = this.selectedSchema();
    if (!schema) {
      return false;
    }

    const baselineInstance = this.currentInstance();
    const baselineAsOfDate = baselineInstance?.asOfDate ?? this.todayDate();
    const baselineState = baselineInstance?.state ?? 'Draft';
    if (this.toComparableString(this.asOfDate()).trim() !== this.toComparableString(baselineAsOfDate).trim()) {
      return true;
    }

    if (this.toComparableString(this.state()).trim() !== this.toComparableString(baselineState).trim()) {
      return true;
    }

    const baselineHeader = this.currentInstance()?.header ?? this.emptyValues(schema.headerFields);
    return schema.headerFields.some((field) => {
      const currentValue = this.toComparableString(this.headerDraft()[field.name]).trim();
      const baselineValue = this.toComparableString((baselineHeader as Record<string, unknown>)[field.name]).trim();
      return currentValue !== baselineValue;
    });
  });
  readonly hasDetailChanges = computed(() => {
    const schema = this.selectedSchema();
    if (!schema) {
      return false;
    }

    const baselineRows = this.currentInstance()?.rows ?? [this.emptyValues(schema.detailFields)];
    return !this.areRowsEqualBySchemaFields(this.rowDrafts(), baselineRows, schema.detailFields);
  });
  readonly hasEditorChanges = computed(() => this.hasHeaderChanges() || this.hasDetailChanges());
  readonly saveActionLabel = computed(() => 'Save');
  readonly validationMessages = computed(() => this.collectValidationMessages());
  readonly headerGridRows = computed<Record<string, unknown>[]>(() => [{ asOfDate: this.asOfDate(), state: this.state(), ...this.headerDraft() }]);
  readonly headerColumnDefs = computed<ColDef<Record<string, unknown>>[]>(() => {
    const schema = this.selectedSchema();
    if (!schema) {
      return [];
    }

    const baseColumns: ColDef<Record<string, unknown>>[] = [
      {
        field: 'asOfDate',
        headerName: 'As Of Date',
        editable: this.canWriteData(),
        singleClickEdit: true,
        resizable: true,
        sortable: true,
        cellDataType: 'dateString',
        cellEditor: 'agDateStringCellEditor',
        minWidth: 150,
        valueParser: (params: ValueParserParams<Record<string, unknown>>) => params.newValue?.toString().trim() ?? ''
      },
      {
        field: 'state',
        headerName: 'State',
        editable: this.canWriteData(),
        singleClickEdit: true,
        resizable: true,
        sortable: true,
        minWidth: 170,
        cellEditor: StateHybridCellEditor,
        valueParser: (params: ValueParserParams<Record<string, unknown>>) => params.newValue?.toString().trim() ?? ''
      }
    ];

    const headerFieldColumns = schema.headerFields.map((field) => {
      const column: ColDef<Record<string, unknown>> = {
        field: field.name,
        headerName: field.label,
        editable: this.canWriteData(),
        singleClickEdit: true,
        resizable: true,
        sortable: true,
        flex: 1,
        minWidth: 170,
        valueParser: (params: ValueParserParams<Record<string, unknown>>) => this.normalizeFieldValue(field, params.newValue),
        valueSetter: (params: ValueSetterParams<Record<string, unknown>>) => {
          params.data[field.name] = this.normalizeFieldValue(field, params.newValue);
          return true;
        },
        cellClassRules: {
          'cell-invalid': (params: CellClassParams<Record<string, unknown>>) => this.getFieldValidationMessage(field, params.value) !== null
        }
      };

      const fieldOptions = this.getPermissibleValuesForField(field);
      if ((field.type === 'Select' || field.type === 'Lookup') && fieldOptions.length > 0) {
        column.cellEditor = 'agSelectCellEditor';
        column.cellEditorParams = {
          values: fieldOptions
        };
      }

      if (field.type === 'Boolean') {
        column.cellEditor = 'agSelectCellEditor';
        column.cellEditorParams = {
          values: ['true', 'false']
        };
      }

      return column;
    });

    return [...baseColumns, ...headerFieldColumns];
  });
  readonly detailColumnDefs = computed<ColDef<Record<string, unknown>>[]>(() => {
    const schema = this.selectedSchema();
    if (!schema) {
      return [];
    }

    const detailColumns = schema.detailFields.map((field) => {
      const column: ColDef<Record<string, unknown>> = {
        field: field.name,
        headerName: field.label,
        editable: this.canWriteData(),
        singleClickEdit: true,
        resizable: true,
        sortable: true,
        flex: 1,
        minWidth: 150,
        valueParser: (params: ValueParserParams<Record<string, unknown>>) => this.normalizeFieldValue(field, params.newValue),
        valueSetter: (params: ValueSetterParams<Record<string, unknown>>) => {
          params.data[field.name] = this.normalizeFieldValue(field, params.newValue);
          return true;
        },
        cellClassRules: {
          'cell-invalid': (params: CellClassParams<Record<string, unknown>>) => this.getFieldValidationMessage(field, params.value) !== null
        }
      };

      const fieldOptions = this.getPermissibleValuesForField(field);
      if ((field.type === 'Select' || field.type === 'Lookup') && fieldOptions.length > 0) {
        column.cellEditor = 'agSelectCellEditor';
        column.cellEditorParams = {
          values: fieldOptions
        };
      }

      if (field.type === 'Boolean') {
        column.cellEditor = 'agSelectCellEditor';
        column.cellEditorParams = {
          values: ['true', 'false']
        };
      }

      return column;
    });

    const deleteColumn: ColDef<Record<string, unknown>> = {
      colId: '__delete',
      headerName: '',
      editable: false,
      sortable: false,
      resizable: false,
      width: 52,
      minWidth: 52,
      maxWidth: 52,
      pinned: 'right',
      lockPosition: 'right',
      cellRenderer: (params: ICellRendererParams<Record<string, unknown>>) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'danger icon-delete-btn detail-row-delete-btn';
        button.textContent = '✕';
        button.title = 'Delete row';
        button.ariaLabel = 'Delete row';
        button.disabled = !this.canWriteData();
        button.addEventListener('click', (event) => {
          event.stopPropagation();
          const rowKey = params.data?.['__rowKey']?.toString();
          if (!rowKey) {
            return;
          }

          this.deleteDetailRow(rowKey);
        });
        return button;
      }
    };

    return [...detailColumns, deleteColumn];
  });

  constructor() {
    this.applySavedHeaderDatePreset('Last month', true);
    this.loadSchemas();
  }

  applySavedHeaderDatePreset(preset: SavedHeaderDatePreset, autoSearch = true): void {
    this.instanceDatePreset.set(preset);
    const { minAsOfDate, maxAsOfDate } = this.getSavedHeaderPresetRange(preset);
    this.instanceFilterAsOfDateMinDraft.set(minAsOfDate);
    this.instanceFilterAsOfDateMaxDraft.set(maxAsOfDate);

    if (autoSearch) {
      this.runSavedHeaderSearch();
    }
  }

  runSavedHeaderSearch(): void {
    this.instanceFilterAsOfDateMin.set(this.instanceFilterAsOfDateMinDraft().trim());
    this.instanceFilterAsOfDateMax.set(this.instanceFilterAsOfDateMaxDraft().trim());
    this.loadDatasetInstances();
  }

  toggleUserSimulation(): void {
    this.userSimulationCollapsed.update((value) => !value);
  }

  applyUserSimulationPreset(preset: UserSimulationPreset): void {
    this.userId.set(preset.userId);
    this.roleInput.set(preset.roles.join(','));
    this.loadSchemas();
  }

  roles(): string[] {
    return this.roleInput()
      .split(',')
      .map((x) => x.trim())
      .filter((x) => x.length > 0);
  }

  loadSchemas(): void {
    this.clearError();
    this.setStatus('Loading dataset catalog...');
    this.api.getSchemas(this.userId(), this.roles()).subscribe({
      next: (schemas) => {
        this.schemas.set(schemas);
        const selectedKey = this.selectedSchemaKey();
        if (selectedKey && schemas.some((x) => x.key === selectedKey)) {
          const selectedSchema = schemas.find((x) => x.key === selectedKey);
          if (selectedSchema) {
            this.setSchemaBuilderFromSchema(selectedSchema);
          }
          this.setStatus('Dataset catalog loaded');
          return;
        }

        if (schemas.length > 0) {
          this.selectSchema(schemas[0].key);
        } else {
          this.selectedSchemaKey.set('');
          this.currentInstance.set(null);
          this.datasetInstances.set([]);
        }
        this.setStatus('Dataset catalog loaded');
      },
      error: (err) => this.setError(err)
    });
  }

  createNewDataset(): void {
    if (!this.canCreateSchema()) {
      this.setError({ error: 'DatasetAdmin role is required to create datasets.' });
      return;
    }

    const keyInput = globalThis.prompt('New dataset key (e.g. FX_NEW):', 'NEW_DATASET');
    if (!keyInput) {
      return;
    }

    const key = keyInput.trim().toUpperCase();
    if (!/^[A-Z0-9_]+$/.test(key)) {
      this.setError({ error: 'Dataset key must use only A-Z, 0-9, and underscore.' });
      return;
    }

    const nameInput = globalThis.prompt('New dataset name:', key);
    if (!nameInput) {
      return;
    }

    const schema = this.buildNewSchemaTemplate(key, nameInput.trim());
    this.clearError();
    this.setStatus(`Creating schema ${key}...`);
    this.api.upsertSchema(this.userId(), this.roles(), schema).subscribe({
      next: (saved) => {
        this.setStatus(`Schema ${saved.key} created`);
        this.setSaveNotice(`Dataset created: ${saved.key}`);
        this.loadSchemas();
        this.selectSchema(saved.key);
        this.openSchemaEditorTab();
      },
      error: (err) => this.setError(err)
    });
  }

  openSchemaEditorTab(): void {
    this.ensureSchemaBuilderDraft();
    this.activeTab.set('schema');
  }

  deleteDatasetSchema(datasetKey: string, event: Event): void {
    event.stopPropagation();
    const schema = this.schemas().find((x) => x.key === datasetKey);
    if (!schema || !this.canManageSchema(schema)) {
      this.setError({ error: 'You are not authorized to maintain this dataset schema.' });
      return;
    }

    const confirmed = globalThis.confirm(`Delete dataset ${datasetKey}? This removes schema and related instances.`);
    if (!confirmed) {
      return;
    }

    this.clearError();
    this.setStatus(`Deleting schema ${datasetKey}...`);
    this.api.deleteSchema(this.userId(), this.roles(), datasetKey).subscribe({
      next: () => {
        if (this.selectedSchemaKey() === datasetKey) {
          this.selectedSchemaKey.set('');
        }

        this.setStatus(`Schema ${datasetKey} deleted`);
        this.setSaveNotice(`Dataset deleted: ${datasetKey}`);
        this.loadSchemas();
      },
      error: (err) => this.setError(err)
    });
  }

  selectSchema(datasetKey: string): void {
    if (datasetKey === this.selectedSchemaKey()) {
      return;
    }

    if (!this.confirmDiscardPendingChanges()) {
      return;
    }

    this.selectedSchemaKey.set(datasetKey);
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    this.setStatus(`Loading ${schema.name}...`);

    this.setSchemaBuilderFromSchema(schema);
    this.asOfDate.set(this.todayDate());
    this.state.set('Draft');
    this.headerDraft.set(this.emptyValues(schema.headerFields));
    this.setDetailRows([this.emptyValues(schema.detailFields)]);
    this.currentInstance.set(null);
    this.selectedInstanceId.set('');
    this.isEditorFormVisible.set(false);
    this.activeTab.set('editor');
    this.showValidation.set(false);
    this.lookupPermissibleValues.set({});
    this.loadLookupPermissibleValues(schema);
    this.loadDatasetInstances(true);
  }

  private loadLookupPermissibleValues(schema: DatasetSchema): void {
    const requestId = ++this.lookupOptionsRequestId;
    const lookupFields = [...schema.headerFields, ...schema.detailFields]
      .filter((field) => field.type === 'Lookup' && (field.lookupDatasetKey?.trim().length ?? 0) > 0);

    const lookupDatasets = Array.from(new Set(
      lookupFields
        .map((field) => field.lookupDatasetKey?.trim().toUpperCase() ?? '')
        .filter((key) => key.length > 0)));

    if (lookupDatasets.length === 0) {
      this.lookupPermissibleValues.set({});
      return;
    }

    const nextValues: Record<string, string[]> = {};
    for (const lookupDatasetKey of lookupDatasets) {
      this.api.getLookupValues(this.userId(), this.roles(), lookupDatasetKey).subscribe({
        next: (values) => {
          if (requestId !== this.lookupOptionsRequestId) {
            return;
          }

          nextValues[lookupDatasetKey] = values;
          this.lookupPermissibleValues.set({ ...nextValues });
          this.refreshHeaderGridRows();
          this.refreshGridRows();
        },
        error: () => {
          if (requestId !== this.lookupOptionsRequestId) {
            return;
          }

          nextValues[lookupDatasetKey] = [];
          this.lookupPermissibleValues.set({ ...nextValues });
          this.refreshHeaderGridRows();
          this.refreshGridRows();
        }
      });
    }
  }

  // Converts the full DatasetInstance returned by write endpoints into the lightweight
  // DatasetHeaderSummary shape used by the instances list, without needing a server round-trip.
  private toHeaderSummary(instance: DatasetInstance): DatasetHeaderSummary {
    return {
      id: instance.id,
      datasetKey: instance.datasetKey,
      asOfDate: instance.asOfDate,
      state: instance.state,
      version: instance.version,
      header: instance.header,
      createdBy: instance.createdBy,
      createdAtUtc: instance.createdAtUtc,
      lastModifiedBy: instance.lastModifiedBy,
      lastModifiedAtUtc: instance.lastModifiedAtUtc
    };
  }

  // After a successful create, update, or signoff, patches the local list in-place
  // instead of re-fetching the full headers list from the server.
  // - Existing entry (update/signoff): replaced at its current position.
  // - New entry (create/save-as): prepended so it appears immediately at the top.
  private mergeInstanceIntoList(instance: DatasetInstance): void {
    const summary = this.toHeaderSummary(instance);
    this.datasetInstances.update((current) => {
      const idx = current.findIndex((x) => x.id === instance.id);
      if (idx >= 0) {
        const updated = [...current];
        updated[idx] = summary;
        return updated;
      }
      return [summary, ...current];
    });
  }

  loadDatasetInstances(showStatus = false): void {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    if (showStatus) {
      this.setStatus(`Loading ${schema.name}...`);
    }

    this.api.getHeaders(
      this.userId(),
      this.roles(),
      schema.key,
      undefined,
      undefined,
      this.instanceFilterAsOfDateMin().trim() || undefined,
      this.instanceFilterAsOfDateMax().trim() || undefined,
      this.includeInternalInfoDebug
    ).subscribe({
      next: (response) => {
        const isWrapped = this.isHeadersInternalResponse(response);
        const headers = isWrapped ? response.items : response;
        const internalInfo = isWrapped ? response.internalInfo : undefined;

        this.datasetInstances.set(headers);
        this.setInternalInfoStatus(internalInfo);
        if (showStatus) {
          this.setStatus(`Loaded ${schema.name}`);
        }
      },
      error: (err) => this.setError(err)
    });
  }

  loadInstanceIntoEditor(instance: DatasetHeaderSummary): void {
    if (this.currentInstance()?.id === instance.id) {
      return;
    }

    if (!this.confirmDiscardPendingChanges()) {
      return;
    }

    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    this.setStatus(`Loading instance ${instance.id}...`);
    this.api.getInstanceById(this.userId(), this.roles(), schema.key, instance.id).subscribe({
      next: (fullInstance) => {
        this.currentInstance.set(fullInstance);
        this.selectedInstanceId.set(fullInstance.id);
        this.state.set(fullInstance.state);
        this.asOfDate.set(fullInstance.asOfDate);
        this.headerDraft.set({ ...fullInstance.header });
        this.setDetailRows(fullInstance.rows.map((row) => ({ ...row })));
        this.isEditorFormVisible.set(true);
        this.setStatus(`Loaded instance ${fullInstance.id} (v${fullInstance.version})`);
        this.showValidation.set(false);
      },
      error: (err) => this.setError(err)
    });
  }

  startNewHeaderDraft(): void {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    if (!this.confirmDiscardPendingChanges()) {
      return;
    }

    this.currentInstance.set(null);
    this.selectedInstanceId.set('');
    this.asOfDate.set(this.todayDate());
    this.state.set('Draft');
    this.headerDraft.set(this.emptyValues(schema.headerFields));
    this.setDetailRows([this.emptyValues(schema.detailFields)]);
    this.isEditorFormVisible.set(true);
    this.showValidation.set(false);
    this.refreshHeaderGridRows();
    this.refreshGridRows();
    this.setStatus('New header draft ready');
  }

  deleteInstanceHeader(instance: DatasetHeaderSummary, event: Event): void {
    event.stopPropagation();
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    const confirmed = globalThis.confirm(`Delete header ${instance.asOfDate} / ${instance.state} v${instance.version}?`);
    if (!confirmed) {
      return;
    }

    this.clearError();
    this.setStatus('Deleting header...');
    this.api.deleteInstance(this.userId(), this.roles(), schema.key, instance.id).subscribe({
      next: () => {
        const isCurrent = this.currentInstance()?.id === instance.id;
        if (isCurrent) {
          this.currentInstance.set(null);
          this.selectedInstanceId.set('');
          this.headerDraft.set(this.emptyValues(schema.headerFields));
          this.setDetailRows([this.emptyValues(schema.detailFields)]);
          this.isEditorFormVisible.set(false);
        }

        this.setStatus('Header deleted');
        this.setSaveNotice('Header deleted');
        this.loadDatasetInstances();
      },
      error: (err) => this.setError(err)
    });
  }

  loadLatest(): void {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    this.clearError();
    this.setStatus('Loading latest dataset version...');
    this.api.getLatestInstance(this.userId(), this.roles(), schema.key, this.asOfDate(), this.state(), undefined, this.includeInternalInfoDebug).subscribe({
      next: (response) => {
        const isWrapped = this.isLatestInternalResponse(response);
        const instance = isWrapped ? response.item : response;
        const internalInfo = isWrapped ? response.internalInfo : undefined;

        this.currentInstance.set(instance);
        this.headerDraft.set((instance?.header ?? this.emptyValues(schema.headerFields)) as Record<string, unknown>);
        this.setDetailRows((instance?.rows?.length ? instance.rows : [this.emptyValues(schema.detailFields)]) as Record<string, unknown>[]);
        this.isEditorFormVisible.set(!!instance);
        this.setInternalInfoStatus(internalInfo);
        this.showValidation.set(false);
        this.setStatus(instance ? `Loaded version ${instance.version}` : 'No data found for selected key/date/state');
      },
      error: (err) => this.setError(err)
    });
  }

  private isHeadersInternalResponse(
    response: DatasetHeaderSummary[] | DatasetHeadersQueryResponse
  ): response is DatasetHeadersQueryResponse {
    return typeof response === 'object' && response !== null && 'items' in response;
  }

  private isLatestInternalResponse(
    response: DatasetInstance | DatasetLatestInstanceQueryResponse | null
  ): response is DatasetLatestInstanceQueryResponse {
    return typeof response === 'object' && response !== null && 'item' in response;
  }

  private setInternalInfoStatus(internalInfo?: DatasetInternalInfo): void {
    if (!this.includeInternalInfoDebug || !internalInfo?.searchEfficiency) {
      this.internalInfoStatus.set('');
      return;
    }

    const stats = internalInfo.searchEfficiency;
    this.internalInfoStatus.set(
      `Debug includeInternalInfo: headers ${stats.headerFilesRead}/${stats.headerFilesTotal}, details ${stats.detailFilesRead}/${stats.detailFilesTotal}, matched ${stats.matchedInstanceFileCount}`);
    this.setStatus(this.internalInfoStatus());
  }

  addRow(): void {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    const newRow = this.toGridRow(this.emptyValues(schema.detailFields));
    const nextRows = [...this.detailGridRows(), newRow];

    this.detailGridRows.set(nextRows);
    this.rowDrafts.set(this.toPlainRows(nextRows));

    const api = this.gridApi();
    if (api) {
      api.applyTransaction({ add: [newRow] });
      api.ensureIndexVisible(nextRows.length - 1, 'bottom');
    }

    this.refreshGridRows();
    this.setStatus(`Added row ${nextRows.length}`);
  }

  async importCsvFromClipboard(): Promise<void> {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    this.clearError();
    try {
      const csvText = await navigator.clipboard.readText();
      this.appendRowsFromCsv(csvText, schema, 'clipboard');
    } catch {
      this.setError({ error: 'Unable to read clipboard. Please allow clipboard access, then try again.' });
    }
  }

  async copyDetailRowsAsCsv(): Promise<void> {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    const rows = this.collectCurrentRows();
    const csvText = this.buildDetailRowsCsv(rows, schema);
    try {
      await navigator.clipboard.writeText(csvText);
      this.setStatus(`Copied ${rows.length} row(s) to clipboard as CSV`);
      this.setSaveNotice('CSV copied to clipboard');
    } catch {
      this.setError({ error: 'Unable to copy to clipboard. Please allow clipboard access, then try again.' });
    }
  }

  deleteAllDetailRows(): void {
    const totalRows = this.detailGridRows().length;
    if (totalRows === 0) {
      return;
    }

    const confirmed = globalThis.confirm(`Delete all ${totalRows} detail row(s)?`);
    if (!confirmed) {
      return;
    }

    this.detailGridRows.set([]);
    this.rowDrafts.set([]);

    const api = this.gridApi();
    if (api) {
      api.setGridOption('rowData', []);
    }

    this.refreshGridRows();
    this.setStatus('All detail rows deleted');
    this.setSaveNotice('Detail rows cleared');
  }

  importCsvFile(event: Event): void {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.clearError();
    const reader = new FileReader();
    reader.onload = () => {
      const text = typeof reader.result === 'string' ? reader.result : '';
      this.appendRowsFromCsv(text, schema, file.name);
      input.value = '';
    };
    reader.onerror = () => this.setError({ error: 'Failed to read CSV file.' });
    reader.readAsText(file);
  }

  saveData(): void {
    if (this.currentInstance()) {
      this.saveEdit();
      return;
    }

    this.createInstance();
  }

  saveAsCopy(): void {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    if (!this.validateBeforeSubmit()) {
      return;
    }

    this.clearError();
    this.setStatus('Saving as new dataset copy...');
    this.api.createInstance(this.userId(), this.roles(), {
      datasetKey: schema.key,
      asOfDate: this.asOfDate(),
      state: this.state(),
      resetVersion: true,
      header: this.headerDraft(),
      rows: this.collectCurrentRows()
    }).subscribe({
      next: (instance) => {
        this.currentInstance.set(instance);
        this.selectedInstanceId.set(instance.id);
        this.setStatus(`Saved as new copy v${instance.version}`);
        this.setSaveNotice(`Save As complete: version ${instance.version}`);
        this.mergeInstanceIntoList(instance);
      },
      error: (err) => this.setError(err)
    });
  }

  removeSelectedRow(): void {
    const api = this.gridApi();
    if (!api) {
      return;
    }

    const selected = api.getSelectedRows();
    if (selected.length === 0) {
      return;
    }

    const rowKey = selected[0]['__rowKey']?.toString();
    if (!rowKey) {
      return;
    }

    this.deleteDetailRow(rowKey);
  }

  private deleteDetailRow(rowKey: string): void {
    const api = this.gridApi();
    if (!api) {
      return;
    }

    const remaining = this.detailGridRows().filter((row) => row.__rowKey !== rowKey);
    this.detailGridRows.set(remaining);
    this.rowDrafts.set(this.toPlainRows(remaining));

    const rowToRemove = api.getRowNode(rowKey)?.data;
    if (rowToRemove) {
      api.applyTransaction({ remove: [rowToRemove] });
    }

    this.refreshGridRows();
  }

  private appendRowsFromCsv(csvText: string, schema: DatasetSchema, source: string): void {
    try {
      const parsedRows = this.parseCsv(csvText);
      if (parsedRows.length === 0) {
        this.setError({ error: 'CSV is empty.' });
        return;
      }

      const imported = this.mapCsvRowsToDetailRows(parsedRows, schema);
      if (imported.length === 0) {
        this.setError({ error: 'No data rows found in CSV.' });
        return;
      }

      const currentRows = this.collectCurrentRows();
      this.setDetailRows([...currentRows, ...imported]);
      this.refreshGridRows();
      this.setStatus(`Imported ${imported.length} rows from ${source}`);
      this.setSaveNotice(`CSV import complete: ${imported.length} row(s) appended`);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'CSV import failed.';
      this.setError({ error: message });
    }
  }

  private mapCsvRowsToDetailRows(csvRows: string[][], schema: DatasetSchema): Record<string, unknown>[] {
    const detailFields = schema.detailFields;
    if (detailFields.length === 0) {
      return [];
    }

    const firstRow = csvRows[0].map((cell) => cell.trim());
    const fieldByCanonical = new Map<string, SchemaField>();
    detailFields.forEach((field) => {
      fieldByCanonical.set(this.canonicalCsvToken(field.name), field);
      fieldByCanonical.set(this.canonicalCsvToken(field.label), field);
    });

    const headerFieldByColumn = firstRow.map((header) => fieldByCanonical.get(this.canonicalCsvToken(header)) ?? null);
    const hasHeaderMatch = headerFieldByColumn.some((field) => field !== null);

    const duplicateFields = new Set<string>();
    const seenFields = new Set<string>();
    headerFieldByColumn.forEach((field) => {
      if (!field) {
        return;
      }

      if (seenFields.has(field.name)) {
        duplicateFields.add(field.name);
        return;
      }

      seenFields.add(field.name);
    });

    if (duplicateFields.size > 0) {
      throw new Error(`CSV header contains duplicate mapped fields: ${Array.from(duplicateFields).join(', ')}`);
    }

    const usingHeader = hasHeaderMatch;
    const dataRows = usingHeader ? csvRows.slice(1) : csvRows;

    if (!usingHeader && firstRow.length !== detailFields.length) {
      throw new Error('CSV must include a header row with detail field names/labels, or have exactly the same number of columns as detail fields.');
    }

    const columnFields = usingHeader
      ? headerFieldByColumn
      : detailFields.map((field) => field as SchemaField | null);

    return dataRows
      .filter((row) => row.some((cell) => cell.trim().length > 0))
      .map((row) => {
        const next = this.emptyValues(detailFields);
        for (let i = 0; i < columnFields.length; i += 1) {
          const field = columnFields[i];
          if (!field) {
            continue;
          }

          const value = row[i] ?? '';
          next[field.name] = this.normalizeFieldValue(field, value);
        }

        return next;
      });
  }

  private parseCsv(csvText: string): string[][] {
    const rows: string[][] = [];
    let row: string[] = [];
    let value = '';
    let inQuotes = false;

    for (let i = 0; i < csvText.length; i += 1) {
      const ch = csvText[i];
      const next = csvText[i + 1];

      if (ch === '"') {
        if (inQuotes && next === '"') {
          value += '"';
          i += 1;
        } else {
          inQuotes = !inQuotes;
        }
        continue;
      }

      if (!inQuotes && ch === ',') {
        row.push(value);
        value = '';
        continue;
      }

      if (!inQuotes && (ch === '\n' || ch === '\r')) {
        if (ch === '\r' && next === '\n') {
          i += 1;
        }

        row.push(value);
        value = '';
        rows.push(row);
        row = [];
        continue;
      }

      value += ch;
    }

    if (value.length > 0 || row.length > 0) {
      row.push(value);
      rows.push(row);
    }

    return rows
      .map((cells) => cells.map((cell) => cell.trim()))
      .filter((cells) => cells.some((cell) => cell.length > 0));
  }

  private buildDetailRowsCsv(rows: Record<string, unknown>[], schema: DatasetSchema): string {
    const headers = schema.detailFields.map((field) => field.name);
    const dataLines = rows.map((row) => headers.map((header) => this.escapeCsvValue(row[header])));
    const headerLine = headers.map((header) => this.escapeCsvValue(header));
    return [headerLine, ...dataLines].map((line) => line.join(',')).join('\r\n');
  }

  private escapeCsvValue(value: unknown): string {
    const text = value === null || value === undefined ? '' : value.toString();
    if (/[",\r\n]/.test(text)) {
      return `"${text.replace(/"/g, '""')}"`;
    }

    return text;
  }

  private canonicalCsvToken(value: string): string {
    return value.toLowerCase().replace(/[\s_-]+/g, '');
  }

  createInstance(): void {
    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    if (!this.validateBeforeSubmit()) {
      return;
    }

    this.clearError();
  this.setStatus('Creating dataset instance...');
    this.api.createInstance(this.userId(), this.roles(), {
      datasetKey: schema.key,
      asOfDate: this.asOfDate(),
      state: this.state(),
      header: this.headerDraft(),
      rows: this.collectCurrentRows()
    }).subscribe({
      next: (instance) => {
        this.currentInstance.set(instance);
        this.selectedInstanceId.set(instance.id);
        this.setStatus(`Created version ${instance.version}`);
        this.setSaveNotice(`Data saved: version ${instance.version}`);
        this.mergeInstanceIntoList(instance);
      },
      error: (err) => this.setError(err)
    });
  }

  saveEdit(): void {
    const schema = this.selectedSchema();
    const current = this.currentInstance();
    if (!schema || !current) {
      return;
    }

    if (!this.validateBeforeSubmit()) {
      return;
    }

    this.clearError();
  this.setStatus('Saving dataset edits...');
    this.api.updateInstance(this.userId(), this.roles(), {
      datasetKey: schema.key,
      instanceId: current.id,
      asOfDate: this.asOfDate(),
      state: this.state(),
      header: this.headerDraft(),
      rows: this.collectCurrentRows()
    }).subscribe({
      next: (instance) => {
        this.currentInstance.set(instance);
        this.selectedInstanceId.set(instance.id);
        this.setStatus(`Saved changes to loaded dataset (v${instance.version})`);
        this.setSaveNotice(`Changes saved: version ${instance.version}`);
        this.mergeInstanceIntoList(instance);
      },
      error: (err) => this.setError(err)
    });
  }

  signoff(): void {
    const schema = this.selectedSchema();
    const current = this.currentInstance();
    if (!schema || !current) {
      return;
    }

    this.clearError();
    this.setStatus('Submitting signoff...');
    this.api.signoff(this.userId(), this.roles(), schema.key, current.id).subscribe({
      next: (official) => {
        this.currentInstance.set(official);
        this.selectedInstanceId.set(official.id);
        this.state.set('Official');
        this.setStatus(`Signoff complete. Official version ${official.version}`);
        this.setSaveNotice(`Data signed off: version ${official.version}`);
        this.mergeInstanceIntoList(official);
      },
      error: (err) => this.setError(err)
    });
  }

  saveSchemaEditor(): void {
    this.clearError();

    try {
      const parsed = this.normalizeSchema(JSON.parse(this.schemaEditorJson()));
      this.setStatus('Saving schema...');
      this.api.upsertSchema(this.userId(), this.roles(), parsed).subscribe({
        next: (saved) => {
          this.setSchemaBuilderFromSchema(saved);
          this.setStatus(`Schema ${saved.key} saved`);
          this.setSaveNotice(`Schema saved: ${saved.key}`);
          this.loadSchemas();
        },
        error: (err) => this.setError(err)
      });
    } catch {
      this.setError({ error: 'Schema JSON is invalid.' });
    }
  }

  onSchemaEditorJsonChange(value: string): void {
    this.schemaEditorJson.set(value);
    if (this.schemaJsonSyncInProgress) {
      return;
    }

    try {
      const parsed = this.normalizeSchema(JSON.parse(value));
      this.schemaBuilderDraft.set(parsed);
      this.schemaEditorJsonError.set('');
    } catch {
      this.schemaEditorJsonError.set('Schema JSON is invalid. Fix JSON to update form controls.');
    }
  }

  updateSchemaMeta(field: 'key' | 'name' | 'description', value: string): void {
    const current = this.ensureSchemaBuilderDraft();
    if (!current) {
      return;
    }

    const nextValue = field === 'key' ? value.trim().toUpperCase() : value;
    this.schemaBuilderDraft.set({ ...current, [field]: nextValue });
    this.syncSchemaJsonFromBuilder();
  }

  getPermissionUsersText(kind: keyof DatasetSchema['permissions']): string {
    const current = this.schemaBuilderDraft();
    if (!current) {
      return '';
    }

    return current.permissions[kind].join(', ');
  }

  updatePermissionUsers(kind: keyof DatasetSchema['permissions'], value: string): void {
    const current = this.ensureSchemaBuilderDraft();
    if (!current) {
      return;
    }

    const users = value
      .split(',')
      .map((x) => x.trim())
      .filter((x) => x.length > 0);

    this.schemaBuilderDraft.set({
      ...current,
      permissions: {
        ...current.permissions,
        [kind]: users
      }
    });
    this.syncSchemaJsonFromBuilder();
  }

  addSchemaField(section: 'headerFields' | 'detailFields'): void {
    const current = this.ensureSchemaBuilderDraft();
    if (!current) {
      return;
    }

    const newField: SchemaField = {
      name: '',
      label: '',
      type: 'String',
      isKey: false,
      required: false,
      allowedValues: []
    };

    this.schemaBuilderDraft.set({
      ...current,
      [section]: [...current[section], newField]
    });
    this.syncSchemaJsonFromBuilder();
  }

  removeSchemaField(section: 'headerFields' | 'detailFields', index: number): void {
    const current = this.ensureSchemaBuilderDraft();
    if (!current) {
      return;
    }

    this.schemaBuilderDraft.set({
      ...current,
      [section]: current[section].filter((_, i) => i !== index)
    });
    this.syncSchemaJsonFromBuilder();
  }

  updateSchemaField(
    section: 'headerFields' | 'detailFields',
    index: number,
    field: keyof SchemaField,
    value: unknown): void {
    const current = this.ensureSchemaBuilderDraft();
    if (!current) {
      return;
    }

    const nextFields = [...current[section]];
    const existing = nextFields[index];
    if (!existing) {
      return;
    }

    const updated: SchemaField = { ...existing };
    if (field === 'required') {
      updated.required = value === true || value === 'true';
    } else if (field === 'isKey') {
      updated.isKey = value === true || value === 'true';
    } else if (field === 'allowedValues') {
      updated.allowedValues = (value?.toString() ?? '')
        .split(',')
        .map((x) => x.trim())
        .filter((x) => x.length > 0);
    } else if (field === 'lookupDatasetKey') {
      const lookupDatasetKey = value?.toString().trim().toUpperCase() ?? '';
      updated.lookupDatasetKey = lookupDatasetKey.length > 0 ? lookupDatasetKey : undefined;
    } else if (field === 'type') {
      updated.type = (value?.toString() ?? 'String') as SchemaField['type'];
      if (updated.type !== 'Select') {
        updated.allowedValues = [];
      }

      if (updated.type !== 'Lookup') {
        delete updated.lookupDatasetKey;
      }
    } else if (field === 'maxLength' || field === 'minValue' || field === 'maxValue') {
      const raw = value?.toString().trim() ?? '';
      if (raw.length === 0) {
        delete updated[field];
      } else {
        const parsed = Number(raw);
        if (!Number.isNaN(parsed)) {
          updated[field] = parsed;
        }
      }
    } else {
      updated[field] = value?.toString() ?? '';
    }

    nextFields[index] = updated;
    this.schemaBuilderDraft.set({
      ...current,
      [section]: nextFields
    });
    this.syncSchemaJsonFromBuilder();
  }

  getFieldAllowedValuesText(field: SchemaField): string {
    return field.allowedValues.join(', ');
  }

  trackByFieldIndex(index: number): number {
    return index;
  }

  loadAudit(): void {
    this.clearError();
    this.setStatus('Loading audit records...');
    this.api.getAudit(this.userId(), this.roles(), this.selectedSchemaKey()).subscribe({
      next: (audit) => {
        this.auditEvents.set(audit);
        this.setStatus(`Loaded ${audit.length} audit events for ${this.selectedSchemaKey()}`);
      },
      error: (err) => this.setError(err)
    });
  }

  activateAuditTab(): void {
    this.activeTab.set('audit');
    this.loadAudit();
  }

  updateHeaderField(field: SchemaField, value: unknown): void {
    this.headerDraft.update((current) => ({ ...current, [field.name]: this.normalizeFieldValue(field, value) }));
  }

  onHeaderGridReady(event: GridReadyEvent<Record<string, unknown>>): void {
    this.headerGridApi.set(event.api);
    this.refreshHeaderGridRows();
  }

  onHeaderGridCellValueChanged(): void {
    const api = this.getActiveHeaderGridApi();
    if (!api) {
      return;
    }

    const row = api.getDisplayedRowAtIndex(0)?.data;
    if (!row) {
      return;
    }

    const nextRow = row as Record<string, unknown>;
    const nextAsOfDate = nextRow['asOfDate']?.toString() ?? this.asOfDate();
    const nextState = nextRow['state']?.toString() ?? this.state();
    this.asOfDate.set(nextAsOfDate);
    this.state.set(nextState);

    const {
      asOfDate: _asOfDate,
      state: _state,
      ...headerFields
    } = nextRow;
    this.headerDraft.set({ ...headerFields });
  }

  onGridReady(event: GridReadyEvent<Record<string, unknown>>): void {
    this.gridApi.set(event.api);
    this.refreshGridRows();
  }

  onGridCellValueChanged(): void {
    const api = this.getActiveDetailGridApi();
    if (!api) {
      return;
    }

    const nextRows: DetailGridRow[] = [];
    api.forEachNode((node) => {
      if (node.data) {
        nextRows.push({ ...(node.data as DetailGridRow) });
      }
    });

    this.detailGridRows.set(nextRows);
    this.rowDrafts.set(this.toPlainRows(nextRows));
  }

  getDetailRowId(params: GetRowIdParams<Record<string, unknown>>): string {
    return params.data['__rowKey']?.toString() ?? '';
  }

  getHeaderInputType(field: SchemaField): string {
    switch (field.type) {
      case 'Number':
        return 'number';
      case 'Date':
        return 'date';
      default:
        return 'text';
    }
  }

  getHeaderFieldValue(field: SchemaField): string | number | boolean {
    const value = this.headerDraft()[field.name];
    if (field.type === 'Boolean') {
      return this.coerceBoolean(value);
    }

    if (field.type === 'Number') {
      if (typeof value === 'number') {
        return value;
      }

      return value?.toString() ?? '';
    }

    return value?.toString() ?? '';
  }

  getHeaderFieldError(field: SchemaField): string | null {
    return this.getFieldValidationMessage(field, this.headerDraft()[field.name]);
  }

  shouldShowValidation(): boolean {
    return this.showValidation() && this.validationMessages().length > 0;
  }

  trackByField(_: number, field: SchemaField): string {
    return field.name;
  }

  private emptyValues(fields: SchemaField[]): Record<string, unknown> {
    return Object.fromEntries(fields.map((field) => [field.name, field.type === 'Boolean' ? false : '']));
  }

  private setSchemaBuilderFromSchema(schema: DatasetSchema): void {
    const normalized = this.normalizeSchema(schema);
    this.schemaBuilderDraft.set(normalized);
    this.schemaEditorJsonError.set('');
    this.schemaJsonSyncInProgress = true;
    this.schemaEditorJson.set(JSON.stringify(normalized, null, 2));
    this.schemaJsonSyncInProgress = false;
  }

  private ensureSchemaBuilderDraft(): DatasetSchema | null {
    const existing = this.schemaBuilderDraft();
    if (existing) {
      return existing;
    }

    const schema = this.selectedSchema();
    if (!schema) {
      return null;
    }

    this.setSchemaBuilderFromSchema(schema);
    return this.schemaBuilderDraft();
  }

  private syncSchemaJsonFromBuilder(): void {
    const current = this.schemaBuilderDraft();
    if (!current) {
      return;
    }

    this.schemaJsonSyncInProgress = true;
    this.schemaEditorJson.set(JSON.stringify(current, null, 2));
    this.schemaJsonSyncInProgress = false;
    this.schemaEditorJsonError.set('');
  }

  private normalizeSchema(value: unknown): DatasetSchema {
    const input = (value ?? {}) as Partial<DatasetSchema>;
    const legacyKeyFields = this.normalizeUsers((input as { keyFields?: unknown }).keyFields);
    const permissions = (input.permissions ?? {}) as Record<string, unknown>;
    return {
      key: input.key?.toString().trim().toUpperCase() ?? '',
      name: input.name?.toString() ?? '',
      description: input.description?.toString() ?? '',
      headerFields: this.normalizeSchemaFields(input.headerFields),
      detailFields: this.normalizeSchemaFields(input.detailFields, legacyKeyFields),
      permissions: {
        readRoles: this.normalizeUsers(permissions['readRoles'] ?? permissions['readUsers']),
        writeRoles: this.normalizeUsers(permissions['writeRoles'] ?? permissions['writeUsers']),
        signoffRoles: this.normalizeUsers(permissions['signoffRoles'] ?? permissions['signoffUsers']),
        datasetAdminRoles: this.normalizeUsers(permissions['datasetAdminRoles'] ?? permissions['datasetAdminUsers'])
      }
    };
  }

  private normalizeSchemaFields(fields: unknown, legacyKeyFields: string[] = []): SchemaField[] {
    const list = Array.isArray(fields) ? fields : [];
    const legacyKeyFieldSet = new Set(legacyKeyFields.map((x) => x.toLowerCase()));
    return list.map((raw) => {
      const field = (raw ?? {}) as Partial<SchemaField>;
      const type = field.type ?? 'String';
      const normalizedType: SchemaField['type'] =
        type === 'String' || type === 'Number' || type === 'Date' || type === 'Boolean' || type === 'Select' || type === 'Lookup'
          ? type
          : 'String';

      return {
        name: field.name?.toString() ?? '',
        label: field.label?.toString() ?? '',
        type: normalizedType,
        isKey: field.isKey === true || legacyKeyFieldSet.has((field.name?.toString() ?? '').toLowerCase()),
        required: field.required === true,
        maxLength: typeof field.maxLength === 'number' ? field.maxLength : undefined,
        minValue: typeof field.minValue === 'number' ? field.minValue : undefined,
        maxValue: typeof field.maxValue === 'number' ? field.maxValue : undefined,
        allowedValues: normalizedType === 'Select' ? this.normalizeUsers(field.allowedValues) : [],
        lookupDatasetKey: normalizedType === 'Lookup'
          ? (field.lookupDatasetKey?.toString().trim().toUpperCase() || undefined)
          : undefined
      };
    });
  }

  private normalizeUsers(users: unknown): string[] {
    if (!Array.isArray(users)) {
      return [];
    }

    return users
      .map((x) => x?.toString().trim() ?? '')
      .filter((x) => x.length > 0);
  }

  private setDetailRows(rows: Record<string, unknown>[]): void {
    this.rowDrafts.set(rows.map((row) => ({ ...row })));
    this.detailGridRows.set(rows.map((row) => this.toGridRow(row)));
  }

  private toGridRow(row: Record<string, unknown>): DetailGridRow {
    return {
      __rowKey: this.newRowKey(),
      ...row
    };
  }

  private toPlainRows(rows: DetailGridRow[]): Record<string, unknown>[] {
    return rows.map(({ __rowKey, ...row }) => ({ ...row }));
  }

  private getActiveDetailGridApi(): GridApi<Record<string, unknown>> | null {
    const api = this.gridApi();
    if (!api) {
      return null;
    }

    if (api.isDestroyed()) {
      this.gridApi.set(null);
      return null;
    }

    return api;
  }

  private getActiveHeaderGridApi(): GridApi<Record<string, unknown>> | null {
    const api = this.headerGridApi();
    if (!api) {
      return null;
    }

    if (api.isDestroyed()) {
      this.headerGridApi.set(null);
      return null;
    }

    return api;
  }

  private collectCurrentRows(): Record<string, unknown>[] {
    const api = this.getActiveDetailGridApi();
    if (!api) {
      return this.rowDrafts();
    }
    api.stopEditing();
    const rows: DetailGridRow[] = [];
    api.forEachNode((node) => {
      if (node.data) {
        rows.push({ ...(node.data as DetailGridRow) });
      }
    });
    const plain = this.toPlainRows(rows);
    this.detailGridRows.set(rows);
    this.rowDrafts.set(plain);
    return plain;
  }

  private collectCurrentHeader(): Record<string, unknown> {
    const api = this.getActiveHeaderGridApi();
    if (!api) {
      return this.headerDraft();
    }

    api.stopEditing();
    const row = api.getDisplayedRowAtIndex(0)?.data;
    const next = row
      ? { ...(row as Record<string, unknown>) }
      : {
          asOfDate: this.asOfDate(),
          state: this.state(),
          ...this.headerDraft()
        };
    const nextAsOfDate = next['asOfDate']?.toString() ?? this.asOfDate();
    const nextState = next['state']?.toString() ?? this.state();
    this.asOfDate.set(nextAsOfDate);
    this.state.set(nextState);
    const {
      asOfDate: _asOfDate,
      state: _state,
      ...headerFields
    } = next;
    this.headerDraft.set({ ...headerFields });
    return headerFields;
  }

  private refreshGridRows(): void {
    const api = this.getActiveDetailGridApi();
    if (!api) {
      return;
    }

    api.refreshCells({ force: true });
  }

  private refreshHeaderGridRows(): void {
    const api = this.getActiveHeaderGridApi();
    if (!api) {
      return;
    }

    api.refreshCells({ force: true });
  }

  private newRowKey(): string {
    return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`;
  }

  private validateBeforeSubmit(): boolean {
    this.clearError();
    this.collectCurrentHeader();
    this.collectCurrentRows();
    this.showValidation.set(true);
    this.refreshHeaderGridRows();
    this.refreshGridRows();

    if (this.validationMessages().length > 0) {
      this.setStatus('Validation failed');
      return false;
    }

    return true;
  }

  private confirmDiscardPendingChanges(): boolean {
    if (!this.hasPendingEditorChanges()) {
      return true;
    }

    return globalThis.confirm('You have unsaved changes. Continue and discard them?');
  }

  private hasPendingEditorChanges(): boolean {
    if (!this.isEditorFormVisible()) {
      return false;
    }

    const schema = this.selectedSchema();
    if (!schema) {
      return false;
    }

    const currentHeader = this.headerGridApi() ? this.collectCurrentHeader() : this.headerDraft();
    const currentRows = this.gridApi() ? this.collectCurrentRows() : this.rowDrafts();

    const baselineInstance = this.currentInstance();
    const baselineHeader = baselineInstance?.header ?? this.emptyValues(schema.headerFields);
    const baselineRows = baselineInstance?.rows ?? [this.emptyValues(schema.detailFields)];
    const baselineAsOfDate = baselineInstance?.asOfDate ?? this.todayDate();
    const baselineState = baselineInstance?.state ?? 'Draft';

    const asOfChanged = this.toComparableString(this.asOfDate()).trim() !== this.toComparableString(baselineAsOfDate).trim();
    const stateChanged = this.normalizeStateTokenForComparison(this.state()) !== this.normalizeStateTokenForComparison(baselineState);
    if (asOfChanged || stateChanged) {
      return true;
    }

    const headerChanged = schema.headerFields.some((field) =>
      this.toComparableString(currentHeader[field.name]).trim() !== this.toComparableString((baselineHeader as Record<string, unknown>)[field.name]).trim());
    if (headerChanged) {
      return true;
    }

    return !this.areRowsEqualBySchemaFields(currentRows, baselineRows, schema.detailFields);
  }

  private areRowsEqualBySchemaFields(
    leftRows: ReadonlyArray<Record<string, unknown>>,
    rightRows: ReadonlyArray<Record<string, unknown>>,
    fields: ReadonlyArray<SchemaField>): boolean {
    if (leftRows.length !== rightRows.length) {
      return false;
    }

    for (let i = 0; i < leftRows.length; i += 1) {
      const left = leftRows[i];
      const right = rightRows[i];
      for (const field of fields) {
        const leftValue = this.toComparableString(left[field.name]).trim();
        const rightValue = this.toComparableString(right[field.name]).trim();
        if (leftValue !== rightValue) {
          return false;
        }
      }
    }

    return true;
  }

  private collectValidationMessages(): string[] {
    const schema = this.selectedSchema();
    if (!schema) {
      return [];
    }

    const messages: string[] = [];
    for (const field of schema.headerFields) {
      const error = this.getFieldValidationMessage(field, this.headerDraft()[field.name]);
      if (error) {
        messages.push(`Header ${field.label}: ${error}`);
      }
    }

    this.rowDrafts().forEach((row, index) => {
      for (const field of schema.detailFields) {
        const error = this.getFieldValidationMessage(field, row[field.name]);
        if (error) {
          messages.push(`Row ${index + 1}, ${field.label}: ${error}`);
        }
      }
    });

    messages.push(...this.collectDuplicateKeyFieldMessages(schema));

    return messages;
  }

  private collectDuplicateKeyFieldMessages(schema: DatasetSchema): string[] {
    const keyFields = schema.detailFields
      .filter((x) => x.isKey)
      .map((x) => x.name)
      .map((x) => x.trim())
      .filter((x) => x.length > 0);

    if (keyFields.length === 0 || this.rowDrafts().length < 2) {
      return [];
    }

    const fieldSet = new Set(schema.detailFields.map((x) => x.name.toLowerCase()));
    const missing = keyFields.filter((x) => !fieldSet.has(x.toLowerCase()));
    if (missing.length > 0) {
      return [`Schema key fields must exist in detail fields: ${missing.join(', ')}`];
    }

    const rowKeyToFirstIndex = new Map<string, number>();
    const messages: string[] = [];
    this.rowDrafts().forEach((row, index) => {
      const parts = keyFields.map((fieldName) => this.toComparableString(row[fieldName]).trim().toUpperCase());
      const key = parts.join('\u001F');
      const existingIndex = rowKeyToFirstIndex.get(key);
      if (existingIndex !== undefined) {
        const valuesText = keyFields
          .map((fieldName) => `${fieldName}=${this.toComparableString(row[fieldName]).trim()}`)
          .join(', ');
        messages.push(`Detail key field values are duplicated: [${valuesText}].`);
        return;
      }

      rowKeyToFirstIndex.set(key, index);
    });

    return messages;
  }

  private getFieldValidationMessage(field: SchemaField, value: unknown): string | null {
    const text = this.toComparableString(value);
    const hasValue = text.trim().length > 0;

    if (field.required && !hasValue) {
      return 'required';
    }

    if (!hasValue) {
      return null;
    }

    if (field.maxLength && text.length > field.maxLength) {
      return `must be ${field.maxLength} characters or fewer`;
    }

    switch (field.type) {
      case 'Number': {
        const numericValue = Number(text);
        if (Number.isNaN(numericValue)) {
          return 'must be numeric';
        }

        if (field.minValue !== undefined && field.minValue !== null && numericValue < field.minValue) {
          return `must be greater than or equal to ${field.minValue}`;
        }

        if (field.maxValue !== undefined && field.maxValue !== null && numericValue > field.maxValue) {
          return `must be less than or equal to ${field.maxValue}`;
        }

        return null;
      }
      case 'Date':
        return Number.isNaN(Date.parse(text)) ? 'must be a valid date' : null;
      case 'Boolean':
        return ['true', 'false'].includes(text.toLowerCase()) ? null : 'must be true or false';
      case 'Select':
        return this.getPermissibleValuesForField(field).some((allowed) => allowed.toLowerCase() === text.toLowerCase())
          ? null
          : `must be one of: ${this.getPermissibleValuesForField(field).join(', ')}`;
      case 'Lookup': {
        const values = this.getPermissibleValuesForField(field);
        if (values.length === 0) {
          return 'has no lookup values available';
        }

        return values.some((allowed) => allowed.toLowerCase() === text.toLowerCase())
          ? null
          : `must be one of: ${values.join(', ')}`;
      }
      default:
        return null;
    }
  }

  private getPermissibleValuesForField(field: SchemaField): string[] {
    if (field.type === 'Select') {
      return field.allowedValues;
    }

    if (field.type !== 'Lookup') {
      return [];
    }

    const lookupKey = field.lookupDatasetKey?.trim().toUpperCase() ?? '';
    if (!lookupKey) {
      return [];
    }

    return this.lookupPermissibleValues()[lookupKey] ?? [];
  }

  private normalizeFieldValue(field: SchemaField, value: unknown): unknown {
    if (value === null || value === undefined) {
      return field.type === 'Boolean' ? false : '';
    }

    if (field.type === 'Boolean') {
      return this.coerceBoolean(value);
    }

    if (field.type === 'Number') {
      const textValue = value.toString().trim();
      if (textValue.length === 0) {
        return '';
      }

      const numericValue = Number(textValue);
      return Number.isNaN(numericValue) ? textValue : numericValue;
    }

    return value.toString();
  }

  private coerceBoolean(value: unknown): boolean {
    if (typeof value === 'boolean') {
      return value;
    }

    return value?.toString().toLowerCase() === 'true';
  }

  private toComparableString(value: unknown): string {
    if (value === null || value === undefined) {
      return '';
    }

    if (typeof value === 'boolean') {
      return value ? 'true' : 'false';
    }

    return value.toString();
  }

  private normalizeStateTokenForComparison(value: unknown): string {
    return this.toComparableString(value)
      .trim()
      .toLowerCase()
      .replace(/[\s_-]+/g, '');
  }

  private hasRole(role: string): boolean {
    return this.roles().some((x) => x.toLowerCase() === role.toLowerCase());
  }

  sortInstances(col: string): void {
    if (this.instanceSortCol() === col) {
      this.instanceSortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.instanceSortCol.set(col);
      this.instanceSortDir.set('asc');
    }
  }

  clearInstanceFilters(): void {
    this.instanceFilterAsOfDate.set('');
    this.instanceFilterAsOfDateMinDraft.set('');
    this.instanceFilterAsOfDateMaxDraft.set('');
    this.instanceFilterAsOfDateMin.set('');
    this.instanceFilterAsOfDateMax.set('');
    this.instanceFilterState.set('');
    this.instanceFilterHeader.set('');
    this.loadDatasetInstances();
  }

  private getSavedHeaderPresetRange(preset: SavedHeaderDatePreset): { minAsOfDate: string; maxAsOfDate: string } {
    const end = new Date();
    end.setHours(0, 0, 0, 0);

    const start = new Date(end);
    if (preset === 'Last month') {
      start.setMonth(start.getMonth() - 1);
    } else if (preset === 'Last 3 months') {
      start.setMonth(start.getMonth() - 3);
    } else if (preset === 'Last 6 months') {
      start.setMonth(start.getMonth() - 6);
    } else {
      start.setFullYear(start.getFullYear() - 1);
    }

    return {
      minAsOfDate: this.toDateInputValue(start),
      maxAsOfDate: this.toDateInputValue(end)
    };
  }

  private toDateInputValue(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private includesPrincipal(principals: string[], userId: string, roles: string[]): boolean {
    const roleSet = new Set(roles.map((x) => x.toLowerCase()));
    const normalizedUserId = userId.toLowerCase();
    return principals.some((principal) => {
      const normalized = principal.toLowerCase();
      return normalized === normalizedUserId || roleSet.has(normalized);
    });
  }

  canManageSchema(schema: DatasetSchema): boolean {
    if (this.hasRole('DatasetAdmin')) {
      return true;
    }

    return this.includesPrincipal(schema.permissions.datasetAdminRoles, this.userId().trim(), this.roles());
  }

  private todayDate(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private buildNewSchemaTemplate(key: string, name: string): DatasetSchema {
    return {
      key,
      name,
      description: 'New dataset schema',
      headerFields: [
        {
          name: 'headerId',
          label: 'Header Id',
          type: 'String',
          isKey: false,
          required: true,
          maxLength: 64,
          allowedValues: []
        }
      ],
      detailFields: [
        {
          name: 'value',
          label: 'Value',
          type: 'String',
          isKey: true,
          required: false,
          maxLength: 256,
          allowedValues: []
        }
      ],
      permissions: {
        readRoles: ['Reader'],
        writeRoles: ['Writer'],
        signoffRoles: ['Approver'],
        datasetAdminRoles: ['Admin']
      }
    };
  }

  private clearError(): void {
    this.error.set('');
    if (this.errorToastTimer) {
      clearTimeout(this.errorToastTimer);
    }
  }

  private setStatus(message: string): void {
    this.status.set(message);
    this.statusToast.set(message);
    if (this.statusToastTimer) {
      clearTimeout(this.statusToastTimer);
    }

    this.statusToastTimer = setTimeout(() => {
      this.statusToast.set('');
    }, 2600);
  }

  private setSaveNotice(message: string): void {
    this.saveNotice.set(message);
    if (this.saveNoticeTimer) {
      clearTimeout(this.saveNoticeTimer);
    }

    this.saveNoticeTimer = setTimeout(() => {
      this.saveNotice.set('');
    }, 3500);
  }

  private setError(err: { error?: unknown; message?: string }): void {
    const backendError = typeof err.error === 'string' ? err.error : '';
    this.error.set(backendError || err.message || 'Unexpected error.');
    this.status.set('Failed');
    if (this.errorToastTimer) {
      clearTimeout(this.errorToastTimer);
    }

    this.errorToastTimer = setTimeout(() => {
      this.error.set('');
    }, 4500);
  }
}
