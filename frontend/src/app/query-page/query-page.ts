import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, Input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatasetApiService } from '../dataset-api.service';
import { DatasetSchema, SchemaField } from '../models';

type ApiMethodName =
  | 'getSchemas'
  | 'upsertSchema'
  | 'deleteSchema'
  | 'getLatestInstance'
  | 'getHeaders'
  | 'getInstances'
  | 'getInstanceById'
  | 'createInstance'
  | 'updateInstance'
  | 'deleteInstance'
  | 'signoff'
  | 'getAudit'
  | 'getLookupValues';

interface ApiMethodDescriptor {
  name: ApiMethodName;
  httpMethod: 'GET' | 'POST' | 'PUT' | 'DELETE';
  route: string;
  parameters: string;
  purpose: string;
}

interface SearchApiCallInfo {
  atUtc: string;
  methodName: ApiMethodName;
  route: string;
  parameters: Record<string, unknown>;
}

interface OpenApiParameter {
  name?: string;
  in?: 'path' | 'query' | 'header' | 'cookie' | string;
}

interface OpenApiOperation {
  parameters?: OpenApiParameter[];
}

interface OpenApiPathItem {
  get?: OpenApiOperation;
  post?: OpenApiOperation;
  put?: OpenApiOperation;
  delete?: OpenApiOperation;
}

interface OpenApiDocument {
  paths?: Record<string, OpenApiPathItem>;
}

@Component({
  selector: 'app-query-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './query-page.html',
  styleUrl: './query-page.scss',
})
export class QueryPageComponent {
  @Input() schemas: DatasetSchema[] = [];
  @Input() userId: string = 'viewer';
  @Input() roles: string[] = [];

  private readonly api = inject(DatasetApiService);

  readonly activeMethodTab = signal<ApiMethodName>('getInstances');
  readonly selectedSchemaKey = signal('');
  readonly selectedSchema = computed(() => {
    const normalizedKey = this.normalizeDatasetKey(this.selectedSchemaKey());
    if (!normalizedKey) {
      return null;
    }

    return this.schemas.find((s) => this.normalizeDatasetKey(s.key) === normalizedKey) ?? null;
  });
  readonly selectedSchemaHeaderFields = computed(() => this.selectedSchema()?.headerFields ?? []);

  readonly asOfDate = signal(this.todayDate());
  readonly minAsOfDate = signal('');
  readonly maxAsOfDate = signal('');
  readonly includeInternalInfo = signal(false);
  readonly stateFilter = signal('');
  readonly instanceId = signal('');
  readonly headerCriteriaJson = signal('{}');
  readonly schemaPayloadJson = signal(this.defaultSchemaPayload());
  readonly createPayloadJson = signal(this.defaultCreatePayload());
  readonly updatePayloadJson = signal(this.defaultUpdatePayload());
  readonly auditDatasetKey = signal('');

  readonly running = signal(false);
  readonly callError = signal('');
  readonly apiMethodsCollapsed = signal(false);
  readonly lookupHeaderOptionsByField = signal<Record<string, string[]>>({});
  readonly lookupHeaderLoadingByField = signal<Record<string, boolean>>({});
  readonly highlightedMethodName = signal<ApiMethodName | null>(null);
  readonly lastSearchApiCall = signal<SearchApiCallInfo | null>(null);
  readonly lastSearchApiResponse = signal<unknown>(null);
  readonly requestCopyLabel = signal('Copy');
  readonly responseCopyLabel = signal('Copy');
  readonly apiMethods = signal<ApiMethodDescriptor[]>([
    {
      name: 'getSchemas',
      httpMethod: 'GET',
      route: '/api/schemas',
      parameters: 'userId, roles[]',
      purpose: 'Returns dataset catalog entries the current user can read.',
    },
    {
      name: 'upsertSchema',
      httpMethod: 'PUT',
      route: '/api/schemas/{datasetKey}',
      parameters: 'userId, roles[], schema payload',
      purpose: 'Creates or updates a dataset schema definition.',
    },
    {
      name: 'deleteSchema',
      httpMethod: 'DELETE',
      route: '/api/schemas/{datasetKey}',
      parameters: 'userId, roles[], datasetKey',
      purpose: 'Deletes a dataset schema and associated stored instances.',
    },
    {
      name: 'getLatestInstance',
      httpMethod: 'GET',
      route: '/api/datasets/{datasetKey}/instances/latest?asOfDate={asOfDate}&state={state}&includeInternalInfo={bool?}',
      parameters: 'userId, roles[], datasetKey, asOfDate, state, headerCriteria, optional includeInternalInfo',
      purpose: 'Returns the latest up-to-asOfDate instance matching state and header criteria.',
    },
    {
      name: 'getHeaders',
      httpMethod: 'GET',
      route: '/api/datasets/{datasetKey}/headers',
      parameters: 'userId, roles[], datasetKey, minAsOfDate, maxAsOfDate, state, headerCriteria, optional includeInternalInfo',
      purpose: 'Returns matching instance headers only (without detail rows).',
    },
    {
      name: 'getInstances',
      httpMethod: 'GET',
      route: '/api/datasets/{datasetKey}/instances',
      parameters: 'userId, roles[], datasetKey, minAsOfDate, maxAsOfDate, state, headerCriteria, optional includeInternalInfo',
      purpose: 'Returns all dataset instances matching server-side criteria.',
    },
    {
      name: 'getInstanceById',
      httpMethod: 'GET',
      route: '/api/datasets/{datasetKey}/instances/{instanceId}',
      parameters: 'userId, roles[], datasetKey, instanceId',
      purpose: 'Returns one dataset instance by id.',
    },
    {
      name: 'createInstance',
      httpMethod: 'POST',
      route: '/api/datasets/{datasetKey}/instances',
      parameters: 'userId, roles[], create payload',
      purpose: 'Creates a new dataset instance/header and detail rows.',
    },
    {
      name: 'updateInstance',
      httpMethod: 'PUT',
      route: '/api/datasets/{datasetKey}/instances/{instanceId}',
      parameters: 'userId, roles[], update payload',
      purpose: 'Updates an existing dataset instance in place.',
    },
    {
      name: 'deleteInstance',
      httpMethod: 'DELETE',
      route: '/api/datasets/{datasetKey}/instances/{instanceId}',
      parameters: 'userId, roles[], datasetKey, instanceId',
      purpose: 'Deletes one dataset instance by id.',
    },
    {
      name: 'signoff',
      httpMethod: 'POST',
      route: '/api/datasets/{datasetKey}/instances/{instanceId}/signoff',
      parameters: 'userId, roles[], datasetKey, instanceId',
      purpose: 'Signs off an instance by setting state to Official.',
    },
    {
      name: 'getAudit',
      httpMethod: 'GET',
      route: '/api/audit?datasetKey={datasetKey?}',
      parameters: 'userId, roles[], optional datasetKey',
      purpose: 'Returns audit events visible to the caller context.',
    },
    {
      name: 'getLookupValues',
      httpMethod: 'GET',
      route: '/api/lookups/{datasetKey}/values',
      parameters: 'userId, roles[], datasetKey',
      purpose: 'Returns permissible lookup values for a lookup dataset.',
    },
  ]);
  readonly lastSearchApiCallJson = computed(() => {
    const call = this.lastSearchApiCall();
    if (!call) {
      return '';
    }

    return this.safeJson(call);
  });
  readonly lastSearchApiResponseJson = computed(() => {
    const response = this.lastSearchApiResponse();
    if (response === null || response === undefined) {
      return '';
    }

    return this.safeJson(response);
  });
  readonly justCalledMethodName = computed(() => this.highlightedMethodName());
  private requestCopyResetTimer?: ReturnType<typeof setTimeout>;
  private responseCopyResetTimer?: ReturnType<typeof setTimeout>;
  private openApiRefreshTimer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    void this.syncApiMethodsFromOpenApi();
    this.openApiRefreshTimer = globalThis.setInterval(() => {
      void this.syncApiMethodsFromOpenApi();
    }, 30000);
  }

  ngOnDestroy(): void {
    if (this.openApiRefreshTimer) {
      globalThis.clearInterval(this.openApiRefreshTimer);
      this.openApiRefreshTimer = undefined;
    }
  }

  onSchemaKeyChange(value: string): void {
    this.selectedSchemaKey.set(this.normalizeDatasetKey(value));
    this.initializeHeaderCriteriaTemplateForSchema();
    this.loadLookupHeaderFieldOptions();
    this.resetCallArtifacts();
  }

  getHeaderCriteriaValue(fieldName: string): string {
    const criteria = this.tryParseHeaderCriteriaJson();
    const value = criteria[fieldName];
    return typeof value === 'string' ? value : '';
  }

  onHeaderCriteriaFieldChange(fieldName: string, rawValue: string): void {
    const criteria = this.tryParseHeaderCriteriaJson();
    const value = (rawValue ?? '').trim();
    if (value.length === 0) {
      delete criteria[fieldName];
    } else {
      criteria[fieldName] = value;
    }

    this.headerCriteriaJson.set(JSON.stringify(criteria, null, 2));
  }

  isHeaderFieldSelect(field: SchemaField): boolean {
    if (field.type === 'Boolean' || field.type === 'Select' || field.type === 'Lookup') {
      return true;
    }

    return field.allowedValues.length > 0;
  }

  getHeaderFieldOptions(field: SchemaField): string[] {
    if (field.type === 'Lookup') {
      const lookupOptions = this.lookupHeaderOptionsByField()[(field.name ?? '').trim()];
      if (Array.isArray(lookupOptions) && lookupOptions.length > 0) {
        return lookupOptions;
      }
    }

    if (field.type === 'Boolean') {
      return ['true', 'false'];
    }

    const options = field.allowedValues
      .map((value) => (value ?? '').trim())
      .filter((value, index, all) => value.length > 0 && all.findIndex((x) => x.toLowerCase() === value.toLowerCase()) === index)
      .sort((left, right) => left.localeCompare(right));

    return options;
  }

  isLookupHeaderFieldLoading(field: SchemaField): boolean {
    if (field.type !== 'Lookup') {
      return false;
    }

    const fieldName = (field.name ?? '').trim();
    if (!fieldName) {
      return false;
    }

    return this.lookupHeaderLoadingByField()[fieldName] === true;
  }

  runActiveMethod(): void {
    if (this.running()) {
      return;
    }

    const methodName = this.activeMethodTab();
    const method = this.apiMethods().find((x) => x.name === methodName);
    if (!method) {
      return;
    }

    this.running.set(true);
    this.callError.set('');
    this.lastSearchApiResponse.set(null);
    this.highlightMethodCall(methodName);

    const finishSuccess = (response: unknown, parameters: Record<string, unknown>) => {
      this.running.set(false);
      this.lastSearchApiCall.set({
        atUtc: new Date().toISOString(),
        methodName,
        route: method.route,
        parameters
      });
      this.lastSearchApiResponse.set(response);
    };

    const finishError = (err: unknown, parameters: Record<string, unknown>) => {
      this.running.set(false);
      this.lastSearchApiCall.set({
        atUtc: new Date().toISOString(),
        methodName,
        route: method.route,
        parameters
      });
      this.lastSearchApiResponse.set(err);
      this.callError.set(this.extractError(err));
    };

    try {
      switch (methodName) {
        case 'getSchemas': {
          const parameters = { userId: this.userId, roles: this.roles };
          this.api.getSchemas(this.userId, this.roles).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'upsertSchema': {
          const schemaPayload = this.parseJson<DatasetSchema>(this.schemaPayloadJson(), 'Schema payload JSON');
          const datasetKey = this.normalizeDatasetKey(schemaPayload.key || this.selectedSchemaKey());
          if (!datasetKey) {
            throw new Error('Dataset key is required for upsertSchema.');
          }

          const normalizedPayload: DatasetSchema = { ...schemaPayload, key: datasetKey };
          const parameters = { userId: this.userId, roles: this.roles, datasetKey, payload: normalizedPayload };
          this.api.upsertSchema(this.userId, this.roles, normalizedPayload).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'deleteSchema': {
          const datasetKey = this.requireDatasetKey();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey };
          this.api.deleteSchema(this.userId, this.roles, datasetKey).subscribe({
            next: (response) => finishSuccess(response ?? { deleted: true }, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'getLatestInstance': {
          const datasetKey = this.requireDatasetKey();
          const asOfDate = this.requireAsOfDate();
          const state = this.requireState();
          const headerCriteria = this.parseHeaderCriteriaInput();
          const includeInternalInfo = this.includeInternalInfo();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey, asOfDate, state, headerCriteria, includeInternalInfo };
          this.api.getLatestInstance(this.userId, this.roles, datasetKey, asOfDate, state, headerCriteria, includeInternalInfo).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'getInstances': {
          const datasetKey = this.requireDatasetKey();
          const asOfDate = this.asOfDate().trim() || undefined;
          const minAsOfDateInput = this.minAsOfDate().trim() || undefined;
          const maxAsOfDateInput = this.maxAsOfDate().trim() || undefined;
          const minAsOfDate = minAsOfDateInput ?? (asOfDate && !maxAsOfDateInput ? asOfDate : undefined);
          const maxAsOfDate = maxAsOfDateInput ?? (asOfDate && !minAsOfDateInput ? asOfDate : undefined);
          const state = this.stateFilter().trim() || undefined;
          const headerCriteria = this.parseHeaderCriteriaInput();
          const includeInternalInfo = this.includeInternalInfo();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey, minAsOfDate, maxAsOfDate, state, headerCriteria, includeInternalInfo };
          this.api.getInstances(this.userId, this.roles, datasetKey, state, headerCriteria, minAsOfDate, maxAsOfDate, includeInternalInfo).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'getHeaders': {
          const datasetKey = this.requireDatasetKey();
          const asOfDate = this.asOfDate().trim() || undefined;
          const minAsOfDateInput = this.minAsOfDate().trim() || undefined;
          const maxAsOfDateInput = this.maxAsOfDate().trim() || undefined;
          const minAsOfDate = minAsOfDateInput ?? (asOfDate && !maxAsOfDateInput ? asOfDate : undefined);
          const maxAsOfDate = maxAsOfDateInput ?? (asOfDate && !minAsOfDateInput ? asOfDate : undefined);
          const state = this.stateFilter().trim() || undefined;
          const headerCriteria = this.parseHeaderCriteriaInput();
          const includeInternalInfo = this.includeInternalInfo();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey, minAsOfDate, maxAsOfDate, state, headerCriteria, includeInternalInfo };
          this.api.getHeaders(this.userId, this.roles, datasetKey, state, headerCriteria, minAsOfDate, maxAsOfDate, includeInternalInfo).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'getInstanceById': {
          const datasetKey = this.requireDatasetKey();
          const instanceId = this.requireInstanceId();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey, instanceId };
          this.api.getInstanceById(this.userId, this.roles, datasetKey, instanceId).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'createInstance': {
          const payload = this.parseJson<{
            datasetKey: string;
            asOfDate: string;
            state: string;
            header: Record<string, unknown>;
            rows: Record<string, unknown>[];
            resetVersion?: boolean;
          }>(this.createPayloadJson(), 'Create payload JSON');

          const normalizedPayload = {
            ...payload,
            datasetKey: this.normalizeDatasetKey(payload.datasetKey || this.selectedSchemaKey())
          };
          if (!normalizedPayload.datasetKey) {
            throw new Error('datasetKey is required in create payload.');
          }

          const parameters = { userId: this.userId, roles: this.roles, payload: normalizedPayload };
          this.api.createInstance(this.userId, this.roles, normalizedPayload).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'updateInstance': {
          const payload = this.parseJson<{
            datasetKey: string;
            instanceId: string;
            asOfDate: string;
            state: string;
            header: Record<string, unknown>;
            rows: Record<string, unknown>[];
          }>(this.updatePayloadJson(), 'Update payload JSON');

          const normalizedPayload = {
            ...payload,
            datasetKey: this.normalizeDatasetKey(payload.datasetKey || this.selectedSchemaKey())
          };
          if (!normalizedPayload.datasetKey || !normalizedPayload.instanceId) {
            throw new Error('datasetKey and instanceId are required in update payload.');
          }

          const parameters = { userId: this.userId, roles: this.roles, payload: normalizedPayload };
          this.api.updateInstance(this.userId, this.roles, normalizedPayload).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'deleteInstance': {
          const datasetKey = this.requireDatasetKey();
          const instanceId = this.requireInstanceId();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey, instanceId };
          this.api.deleteInstance(this.userId, this.roles, datasetKey, instanceId).subscribe({
            next: (response) => finishSuccess(response ?? { deleted: true }, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'signoff': {
          const datasetKey = this.requireDatasetKey();
          const instanceId = this.requireInstanceId();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey, instanceId };
          this.api.signoff(this.userId, this.roles, datasetKey, instanceId).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'getAudit': {
          const datasetKey = this.normalizeDatasetKey(this.auditDatasetKey());
          const parameters = { userId: this.userId, roles: this.roles, datasetKey: datasetKey || undefined };
          this.api.getAudit(this.userId, this.roles, datasetKey || undefined).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }

        case 'getLookupValues': {
          const datasetKey = this.requireDatasetKey();
          const parameters = { userId: this.userId, roles: this.roles, datasetKey };
          this.api.getLookupValues(this.userId, this.roles, datasetKey).subscribe({
            next: (response) => finishSuccess(response, parameters),
            error: (err) => finishError(err, parameters)
          });
          break;
        }
      }
    } catch (error) {
      this.running.set(false);
      this.callError.set(this.extractError(error));
      this.lastSearchApiResponse.set({ error: this.extractError(error) });
    }
  }

  isMethodHighlighted(methodName: string): boolean {
    return this.justCalledMethodName() === methodName;
  }

  onMethodNameClick(methodName: ApiMethodName): void {
    this.activeMethodTab.set(methodName);
  }

  copyRequestPayload(): void {
    void this.copyPayloadText(this.lastSearchApiCallJson(), 'request');
  }

  copyResponsePayload(): void {
    void this.copyPayloadText(this.lastSearchApiResponseJson(), 'response');
  }

  private resetCallArtifacts(): void {
    this.callError.set('');
    this.lastSearchApiCall.set(null);
    this.lastSearchApiResponse.set(null);
    this.requestCopyLabel.set('Copy');
    this.responseCopyLabel.set('Copy');
  }

  private async copyPayloadText(text: string, target: 'request' | 'response'): Promise<void> {
    const setLabel = (value: string) => {
      if (target === 'request') {
        this.requestCopyLabel.set(value);
        if (this.requestCopyResetTimer) {
          globalThis.clearTimeout(this.requestCopyResetTimer);
        }
        this.requestCopyResetTimer = globalThis.setTimeout(() => this.requestCopyLabel.set('Copy'), 1400);
      } else {
        this.responseCopyLabel.set(value);
        if (this.responseCopyResetTimer) {
          globalThis.clearTimeout(this.responseCopyResetTimer);
        }
        this.responseCopyResetTimer = globalThis.setTimeout(() => this.responseCopyLabel.set('Copy'), 1400);
      }
    };

    if (!text || text.trim().length === 0) {
      setLabel('No content');
      return;
    }

    try {
      if (!navigator.clipboard || typeof navigator.clipboard.writeText !== 'function') {
        throw new Error('Clipboard API is unavailable.');
      }

      await navigator.clipboard.writeText(text);
      setLabel('Copied');
    } catch {
      setLabel('Copy failed');
    }
  }

  private todayDate(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private normalizeDatasetKey(value: string | null | undefined): string {
    return (value ?? '').trim();
  }

  private requireDatasetKey(): string {
    const key = this.normalizeDatasetKey(this.selectedSchemaKey());
    if (!key) {
      throw new Error('Dataset key is required.');
    }

    return key;
  }

  private requireAsOfDate(): string {
    const asOfDate = this.asOfDate().trim();
    if (!asOfDate) {
      throw new Error('As Of Date is required.');
    }

    return asOfDate;
  }

  private requireState(): string {
    const state = this.stateFilter().trim();
    if (!state) {
      throw new Error('State is required.');
    }

    return state;
  }

  private requireInstanceId(): string {
    const instanceId = this.instanceId().trim();
    if (!instanceId) {
      throw new Error('Instance ID is required.');
    }

    return instanceId;
  }

  private parseHeaderCriteriaInput(): Record<string, string> | undefined {
    const parsedCriteria = this.parseJson<Record<string, string>>(this.headerCriteriaJson(), 'Header criteria JSON');
    const normalizedCriteria = Object.entries(parsedCriteria)
      .filter(([key, value]) => key.trim().length > 0 && typeof value === 'string' && value.trim().length > 0)
      .reduce((acc, [key, value]) => {
        acc[key.trim()] = value.trim();
        return acc;
      }, {} as Record<string, string>);

    return Object.keys(normalizedCriteria).length > 0 ? normalizedCriteria : undefined;
  }

  private initializeHeaderCriteriaTemplateForSchema(): void {
    const headerFields = this.selectedSchemaHeaderFields();
    if (headerFields.length === 0) {
      this.headerCriteriaJson.set('{}');
      return;
    }

    const existing = this.tryParseHeaderCriteriaJson();
    const template: Record<string, string> = {};
    for (const field of headerFields) {
      const fieldName = (field.name ?? '').trim();
      if (!fieldName) {
        continue;
      }

      const existingValue = existing[fieldName];
      if (typeof existingValue === 'string' && existingValue.trim().length > 0) {
        template[fieldName] = existingValue.trim();
      }
    }

    this.headerCriteriaJson.set(JSON.stringify(template, null, 2));
  }

  private loadLookupHeaderFieldOptions(): void {
    this.lookupHeaderOptionsByField.set({});
    this.lookupHeaderLoadingByField.set({});

    const schema = this.selectedSchema();
    if (!schema) {
      return;
    }

    const schemaKeyAtRequestStart = this.selectedSchemaKey();
    for (const field of schema.headerFields) {
      if (field.type !== 'Lookup') {
        continue;
      }

      const fieldName = (field.name ?? '').trim();
      const lookupDatasetKey = this.normalizeDatasetKey(field.lookupDatasetKey);
      if (!fieldName || !lookupDatasetKey) {
        continue;
      }

      this.setLookupHeaderFieldLoading(fieldName, true);
      this.api.getLookupValues(this.userId, this.roles, lookupDatasetKey).subscribe({
        next: (values) => {
          if (this.selectedSchemaKey() !== schemaKeyAtRequestStart) {
            return;
          }

          const normalizedValues = values
            .map((value) => (value ?? '').trim())
            .filter((value, index, all) => value.length > 0 && all.findIndex((x) => x.toLowerCase() === value.toLowerCase()) === index)
            .sort((left, right) => left.localeCompare(right));

          this.setLookupHeaderFieldOptions(fieldName, normalizedValues);
          this.setLookupHeaderFieldLoading(fieldName, false);
        },
        error: () => {
          if (this.selectedSchemaKey() !== schemaKeyAtRequestStart) {
            return;
          }

          this.setLookupHeaderFieldLoading(fieldName, false);
        }
      });
    }
  }

  private setLookupHeaderFieldOptions(fieldName: string, values: string[]): void {
    const current = this.lookupHeaderOptionsByField();
    this.lookupHeaderOptionsByField.set({
      ...current,
      [fieldName]: values
    });
  }

  private setLookupHeaderFieldLoading(fieldName: string, isLoading: boolean): void {
    const current = this.lookupHeaderLoadingByField();
    this.lookupHeaderLoadingByField.set({
      ...current,
      [fieldName]: isLoading
    });
  }

  private tryParseHeaderCriteriaJson(): Record<string, string> {
    try {
      return this.parseJson<Record<string, string>>(this.headerCriteriaJson(), 'Header criteria JSON');
    } catch {
      return {};
    }
  }

  private parseJson<T>(value: string, label: string): T {
    try {
      return JSON.parse(value) as T;
    } catch {
      throw new Error(`${label} is not valid JSON.`);
    }
  }

  private extractError(err: unknown): string {
    const e = err as { error?: { message?: string } | string; message?: string };
    if (typeof e?.error === 'string' && e.error.trim().length > 0) {
      return e.error;
    }

    if (typeof e?.error === 'object' && typeof e.error?.message === 'string') {
      return e.error.message;
    }

    if (typeof e?.message === 'string' && e.message.trim().length > 0) {
      return e.message;
    }

    return 'API call failed.';
  }

  private defaultSchemaPayload(): string {
    return JSON.stringify({
      key: 'FX_RATES',
      name: 'FX Rates',
      description: 'Schema payload example',
      headerFields: [],
      detailFields: [],
      permissions: {
        readRoles: ['viewer'],
        writeRoles: ['writer'],
        signoffRoles: ['approver'],
        datasetAdminRoles: []
      }
    }, null, 2);
  }

  private defaultCreatePayload(): string {
    return JSON.stringify({
      datasetKey: 'FX_RATES',
      asOfDate: this.todayDate(),
      state: 'Draft',
      header: {},
      rows: []
    }, null, 2);
  }

  private defaultUpdatePayload(): string {
    return JSON.stringify({
      datasetKey: 'FX_RATES',
      instanceId: '',
      asOfDate: this.todayDate(),
      state: 'Draft',
      header: {},
      rows: []
    }, null, 2);
  }

  private safeJson(value: unknown): string {
    try {
      return JSON.stringify(value, null, 2);
    } catch {
      return String(value);
    }
  }

  private highlightMethodCall(methodName: ApiMethodName): void {
    this.highlightedMethodName.set(methodName);
  }

  private async syncApiMethodsFromOpenApi(): Promise<void> {
    const openApiDocument = await this.fetchOpenApiDocument();
    if (!openApiDocument?.paths) {
      return;
    }

    const methodToEndpoint: Record<ApiMethodName, { path: string; verb: 'get' | 'post' | 'put' | 'delete' }> = {
      getSchemas: { path: '/api/schemas', verb: 'get' },
      upsertSchema: { path: '/api/schemas/{datasetKey}', verb: 'put' },
      deleteSchema: { path: '/api/schemas/{datasetKey}', verb: 'delete' },
      getLatestInstance: { path: '/api/datasets/{datasetKey}/instances/latest', verb: 'get' },
      getHeaders: { path: '/api/datasets/{datasetKey}/headers', verb: 'get' },
      getInstances: { path: '/api/datasets/{datasetKey}/instances', verb: 'get' },
      getInstanceById: { path: '/api/datasets/{datasetKey}/instances/{instanceId}', verb: 'get' },
      createInstance: { path: '/api/datasets/{datasetKey}/instances', verb: 'post' },
      updateInstance: { path: '/api/datasets/{datasetKey}/instances/{instanceId}', verb: 'put' },
      deleteInstance: { path: '/api/datasets/{datasetKey}/instances/{instanceId}', verb: 'delete' },
      signoff: { path: '/api/datasets/{datasetKey}/instances/{instanceId}/signoff', verb: 'post' },
      getAudit: { path: '/api/audit', verb: 'get' },
      getLookupValues: { path: '/api/lookups/{datasetKey}/values', verb: 'get' }
    };

    const currentMethods = this.apiMethods();
    const updatedMethods = currentMethods.map((descriptor) => {
      const endpoint = methodToEndpoint[descriptor.name];
      const pathItem = endpoint ? openApiDocument.paths?.[endpoint.path] : undefined;
      const operation = pathItem?.[endpoint.verb];
      if (!operation) {
        return descriptor;
      }

      const route = this.buildRouteTemplate(endpoint.path, operation.parameters ?? []);
      const parameters = this.buildParameterSummary(operation.parameters ?? [], descriptor.parameters);
      return { ...descriptor, route, parameters };
    });

    this.apiMethods.set(updatedMethods);
  }

  private buildRouteTemplate(path: string, parameters: OpenApiParameter[]): string {
    const queryParameters = parameters
      .filter((parameter) => parameter.in === 'query' && (parameter.name ?? '').trim().length > 0)
      .map((parameter) => parameter.name!.trim());

    if (queryParameters.length === 0) {
      return path;
    }

    const queryTemplate = queryParameters
      .map((name) => `${name}={${name}}`)
      .join('&');

    return `${path}?${queryTemplate}`;
  }

  private buildParameterSummary(parameters: OpenApiParameter[], fallback: string): string {
    const parts = parameters
      .filter((parameter) => {
        const name = (parameter.name ?? '').trim();
        return name.length > 0 && (parameter.in === 'path' || parameter.in === 'query');
      })
      .map((parameter) => (parameter.name ?? '').trim());

    if (parts.length === 0) {
      return fallback;
    }

    return `userId, roles[], ${parts.join(', ')}`;
  }

  private async fetchOpenApiDocument(): Promise<OpenApiDocument | null> {
    try {
      const apiBaseUrl = (globalThis as { __datasetApiBaseUrl?: string }).__datasetApiBaseUrl ?? 'http://localhost:5201/api';
      const openApiBaseUrl = apiBaseUrl.replace(/\/api\/?$/, '');
      const response = await fetch(`${openApiBaseUrl}/openapi/v1.json`);
      if (!response.ok) {
        return null;
      }

      return await response.json() as OpenApiDocument;
    } catch {
      return null;
    }
  }
}
