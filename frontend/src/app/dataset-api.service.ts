import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AuditEvent, Catalogue, DatasetHeaderSummary, DatasetHeadersQueryResponse, DatasetInstance, DatasetInstancesQueryResponse, DatasetLatestInstanceQueryResponse, DatasetSchema, DatasetState } from './models';

@Injectable({ providedIn: 'root' })
export class DatasetApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl =
    (globalThis as { __datasetApiBaseUrl?: string }).__datasetApiBaseUrl ??
    'http://localhost:5201/api';

  getSchemas(userId: string, roles: string[]): Observable<DatasetSchema[]> {
    return this.http.get<DatasetSchema[]>(`${this.baseUrl}/schemas`, {
      headers: this.headers(userId, roles)
    });
  }

  upsertSchema(userId: string, roles: string[], schema: DatasetSchema): Observable<DatasetSchema> {
    return this.http.put<DatasetSchema>(`${this.baseUrl}/schemas/${schema.key}`, schema, {
      headers: this.headers(userId, roles)
    });
  }

  deleteSchema(userId: string, roles: string[], datasetKey: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/schemas/${datasetKey}`, {
      headers: this.headers(userId, roles)
    });
  }

  getLatestInstance(userId: string, roles: string[], datasetKey: string, asOfDate: string, state: DatasetState): Observable<DatasetInstance | null>;
  getLatestInstance(userId: string, roles: string[], datasetKey: string, asOfDate: string, state: DatasetState, headerCriteria: Record<string, string> | undefined, includeInternalInfo: false): Observable<DatasetInstance | null>;
  getLatestInstance(userId: string, roles: string[], datasetKey: string, asOfDate: string, state: DatasetState, headerCriteria: Record<string, string> | undefined, includeInternalInfo: true): Observable<DatasetLatestInstanceQueryResponse>;
  getLatestInstance(userId: string, roles: string[], datasetKey: string, asOfDate: string, state: DatasetState, headerCriteria?: Record<string, string>, includeInternalInfo?: boolean): Observable<DatasetInstance | DatasetLatestInstanceQueryResponse | null>;
  getLatestInstance(
    userId: string,
    roles: string[],
    datasetKey: string,
    asOfDate: string,
    state: DatasetState,
    headerCriteria?: Record<string, string>,
    includeInternalInfo = false
  ): Observable<DatasetInstance | DatasetLatestInstanceQueryResponse | null> {
    let params = new HttpParams()
      .set('asOfDate', asOfDate)
      .set('state', state);

    if (headerCriteria && Object.keys(headerCriteria).length > 0) {
      params = params.set('headerCriteria', JSON.stringify(headerCriteria));
    }

    if (includeInternalInfo) {
      params = params.set('includeInternalInfo', 'true');
    }

    return this.http.get<DatasetInstance | DatasetLatestInstanceQueryResponse | null>(
      `${this.baseUrl}/datasets/${datasetKey}/instances/latest`,
      { headers: this.headers(userId, roles), params }
    );
  }

  getInstances(
    userId: string,
    roles: string[],
    datasetKey: string,
    state?: DatasetState,
    headerCriteria?: Record<string, string>,
    minAsOfDate?: string,
    maxAsOfDate?: string
  ): Observable<DatasetInstance[]>;
  getInstances(
    userId: string,
    roles: string[],
    datasetKey: string,
    state: DatasetState | undefined,
    headerCriteria: Record<string, string> | undefined,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo?: false
  ): Observable<DatasetInstance[]>;
  getInstances(
    userId: string,
    roles: string[],
    datasetKey: string,
    state: DatasetState | undefined,
    headerCriteria: Record<string, string> | undefined,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo?: true
  ): Observable<DatasetInstancesQueryResponse>;
  getInstances(
    userId: string,
    roles: string[],
    datasetKey: string,
    state?: DatasetState,
    headerCriteria?: Record<string, string>,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo?: boolean
  ): Observable<DatasetInstance[] | DatasetInstancesQueryResponse>;
  getInstances(
    userId: string,
    roles: string[],
    datasetKey: string,
    state?: DatasetState,
    headerCriteria?: Record<string, string>,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo = false
  ): Observable<DatasetInstance[] | DatasetInstancesQueryResponse> {
    let params = new HttpParams();
    if (state) {
      params = params.set('state', state);
    }

    if (headerCriteria && Object.keys(headerCriteria).length > 0) {
      params = params.set('headerCriteria', JSON.stringify(headerCriteria));
    }

    if (minAsOfDate) {
      params = params.set('minAsOfDate', minAsOfDate);
    }

    if (maxAsOfDate) {
      params = params.set('maxAsOfDate', maxAsOfDate);
    }

    if (includeInternalInfo) {
      params = params.set('includeInternalInfo', 'true');
    }

    return this.http.get<DatasetInstance[] | DatasetInstancesQueryResponse>(`${this.baseUrl}/datasets/${datasetKey}/instances`, {
      headers: this.headers(userId, roles),
      params
    });
  }

  getHeaders(
    userId: string,
    roles: string[],
    datasetKey: string,
    state?: DatasetState,
    headerCriteria?: Record<string, string>,
    minAsOfDate?: string,
    maxAsOfDate?: string
  ): Observable<DatasetHeaderSummary[]>;
  getHeaders(
    userId: string,
    roles: string[],
    datasetKey: string,
    state: DatasetState | undefined,
    headerCriteria: Record<string, string> | undefined,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo?: false
  ): Observable<DatasetHeaderSummary[]>;
  getHeaders(
    userId: string,
    roles: string[],
    datasetKey: string,
    state: DatasetState | undefined,
    headerCriteria: Record<string, string> | undefined,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo?: true
  ): Observable<DatasetHeadersQueryResponse>;
  getHeaders(
    userId: string,
    roles: string[],
    datasetKey: string,
    state?: DatasetState,
    headerCriteria?: Record<string, string>,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo?: boolean
  ): Observable<DatasetHeaderSummary[] | DatasetHeadersQueryResponse>;
  getHeaders(
    userId: string,
    roles: string[],
    datasetKey: string,
    state?: DatasetState,
    headerCriteria?: Record<string, string>,
    minAsOfDate?: string,
    maxAsOfDate?: string,
    includeInternalInfo = false
  ): Observable<DatasetHeaderSummary[] | DatasetHeadersQueryResponse> {
    let params = new HttpParams();
    if (state) {
      params = params.set('state', state);
    }

    if (headerCriteria && Object.keys(headerCriteria).length > 0) {
      params = params.set('headerCriteria', JSON.stringify(headerCriteria));
    }

    if (minAsOfDate) {
      params = params.set('minAsOfDate', minAsOfDate);
    }

    if (maxAsOfDate) {
      params = params.set('maxAsOfDate', maxAsOfDate);
    }

    if (includeInternalInfo) {
      params = params.set('includeInternalInfo', 'true');
    }

    return this.http.get<DatasetHeaderSummary[] | DatasetHeadersQueryResponse>(`${this.baseUrl}/datasets/${datasetKey}/headers`, {
      headers: this.headers(userId, roles),
      params
    });
  }

  getInstanceById(userId: string, roles: string[], datasetKey: string, instanceId: string): Observable<DatasetInstance> {
    return this.http.get<DatasetInstance>(`${this.baseUrl}/datasets/${datasetKey}/instances/${instanceId}`, {
      headers: this.headers(userId, roles)
    });
  }

  createInstance(
    userId: string,
    roles: string[],
    payload: {
      datasetKey: string;
      asOfDate: string;
      state: DatasetState;
      header: Record<string, unknown>;
      rows: Record<string, unknown>[];
      resetVersion?: boolean;
    }
  ): Observable<DatasetInstance> {
    return this.http.post<DatasetInstance>(`${this.baseUrl}/datasets/${payload.datasetKey}/instances`, payload, {
      headers: this.headers(userId, roles)
    });
  }

  updateInstance(
    userId: string,
    roles: string[],
    payload: { datasetKey: string; instanceId: string; asOfDate: string; state: DatasetState; header: Record<string, unknown>; rows: Record<string, unknown>[] }
  ): Observable<DatasetInstance> {
    return this.http.put<DatasetInstance>(`${this.baseUrl}/datasets/${payload.datasetKey}/instances/${payload.instanceId}`, payload, {
      headers: this.headers(userId, roles)
    });
  }

  deleteInstance(userId: string, roles: string[], datasetKey: string, instanceId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/datasets/${datasetKey}/instances/${instanceId}`, {
      headers: this.headers(userId, roles)
    });
  }

  signoff(userId: string, roles: string[], datasetKey: string, instanceId: string): Observable<DatasetInstance> {
    return this.http.post<DatasetInstance>(`${this.baseUrl}/datasets/${datasetKey}/instances/${instanceId}/signoff`, {}, {
      headers: this.headers(userId, roles)
    });
  }

  getAudit(
    userId: string,
    roles: string[],
    datasetKey?: string,
    instanceId?: string,
    minOccurredDate?: string,
    maxOccurredDate?: string
  ): Observable<AuditEvent[]> {
    let params = new HttpParams();
    if (datasetKey && datasetKey.trim().length > 0) {
      params = params.set('datasetKey', datasetKey.trim());
    }
    if (instanceId && instanceId.trim().length > 0) {
      params = params.set('instanceId', instanceId.trim());
    }
    if (minOccurredDate && minOccurredDate.trim().length > 0) {
      params = params.set('minOccurredDate', minOccurredDate.trim());
    }
    if (maxOccurredDate && maxOccurredDate.trim().length > 0) {
      params = params.set('maxOccurredDate', maxOccurredDate.trim());
    }

    return this.http.get<AuditEvent[]>(`${this.baseUrl}/audit`, {
      headers: this.headers(userId, roles),
      params
    });
  }

  getCatalogues(userId: string, roles: string[]): Observable<Catalogue[]> {
    return this.http.get<Catalogue[]>(`${this.baseUrl}/catalogues`, {
      headers: this.headers(userId, roles)
    });
  }

  upsertCatalogue(userId: string, roles: string[], catalogue: Catalogue): Observable<Catalogue> {
    return this.http.put<Catalogue>(`${this.baseUrl}/catalogues/${encodeURIComponent(catalogue.key)}`, catalogue, {
      headers: this.headers(userId, roles)
    });
  }

  deleteCatalogue(userId: string, roles: string[], catalogueKey: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/catalogues/${encodeURIComponent(catalogueKey)}`, {
      headers: this.headers(userId, roles)
    });
  }

  getLookupValues(userId: string, roles: string[], datasetKey: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/lookups/${datasetKey}/values`, {
      headers: this.headers(userId, roles)
    });
  }

  private headers(userId: string, roles: string[]): HttpHeaders {
    return new HttpHeaders({
      'x-user-id': userId,
      'x-user-roles': roles.join(',')
    });
  }
}
