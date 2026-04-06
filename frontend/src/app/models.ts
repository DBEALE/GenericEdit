export type DatasetState = string;

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
  details: string;
}
