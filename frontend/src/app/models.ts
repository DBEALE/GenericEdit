export type DatasetState = string;

export interface SchemaField {
  name: string;
  label: string;
  type: 'String' | 'Number' | 'Date' | 'Boolean' | 'Select' | 'Lookup';
  isKey: boolean;
  required: boolean;
  defaultValue?: string;
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

export interface Catalogue {
  key: string;
  name: string;
  description: string;
  headerFields: SchemaField[];
  createdAtUtc?: string;
  updatedAtUtc?: string;
}

export interface DatasetSchema {
  key: string;
  name: string;
  description: string;
  catalogueKey?: string;
  headerFields: SchemaField[];
  detailFields: SchemaField[];
  permissions: DatasetPermissions;
}

export interface DatasetInstance {
  id: string;
  datasetKey: string;
  asOfDate: string;
  state: DatasetState;
  version: number;
  header: Record<string, unknown>;
  rows: Record<string, unknown>[];
  createdBy?: string;
  createdAtUtc?: string;
  lastModifiedBy: string;
  lastModifiedAtUtc: string;
}

export interface DatasetHeaderSummary {
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
  loadedFilenames: Array<{
    fileName: string;
    reason: string;
  }>;
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

export interface DatasetInstancesQueryResponse {
  items: DatasetInstance[];
  internalInfo?: DatasetInternalInfo;
}

export interface DatasetHeadersQueryResponse {
  items: DatasetHeaderSummary[];
  internalInfo?: DatasetInternalInfo;
}

export interface DatasetLatestInstanceQueryResponse {
  item: DatasetInstance | null;
  internalInfo?: DatasetInternalInfo;
}

export interface AuditEvent {
  id: string;
  occurredAtUtc: string;
  userId: string;
  action: string;
  datasetKey: string;
  datasetInstanceId?: string;
  asOfDate?: string;
  state?: string;
  instanceHeader?: Record<string, string>;
  rowChanges?: AuditRowChange[];
}

export interface AuditRowChange {
  operation: 'added' | 'removed' | 'updated' | string;
  keyFields: Record<string, string>;
  sourceValues?: Record<string, string>;
  targetValues?: Record<string, string>;
}
