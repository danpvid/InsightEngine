import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, shareReplay } from 'rxjs/operators';
import { environment } from '../../../environments/environment.development';
import { ApiResponse } from '../models/api-response.model';
import {
  BuildIndexRequest,
  BuildIndexResponseData,
  DatasetIndex,
  DatasetIndexStatus
} from '../models/metadata-index.model';

@Injectable({
  providedIn: 'root'
})
export class MetadataIndexApiService {
  private readonly baseUrl = environment.apiBaseUrl;
  private readonly indexCache = new Map<string, Observable<ApiResponse<DatasetIndex>>>();
  private readonly statusCache = new Map<string, Observable<ApiResponse<DatasetIndexStatus>>>();

  constructor(private http: HttpClient) {}

  buildIndex(datasetId: string, request?: BuildIndexRequest): Observable<ApiResponse<BuildIndexResponseData>> {
    this.clearDatasetCache(datasetId);
    return this.http.post<ApiResponse<BuildIndexResponseData>>(
      `${this.baseUrl}/api/v1/datasets/${datasetId}/index:build`,
      request ?? {}
    );
  }

  getIndex(datasetId: string, forceRefresh: boolean = false): Observable<ApiResponse<DatasetIndex>> {
    if (forceRefresh) {
      this.indexCache.delete(datasetId);
    }

    const cached = this.indexCache.get(datasetId);
    if (cached) {
      return cached;
    }

    const request$ = this.http
      .get<ApiResponse<DatasetIndex>>(`${this.baseUrl}/api/v1/datasets/${datasetId}/index`)
      .pipe(
        shareReplay(1),
        catchError(error => {
          this.indexCache.delete(datasetId);
          return throwError(() => error);
        })
      );

    this.indexCache.set(datasetId, request$);
    return request$;
  }

  getIndexStatus(datasetId: string, forceRefresh: boolean = false): Observable<ApiResponse<DatasetIndexStatus>> {
    if (forceRefresh) {
      this.statusCache.delete(datasetId);
    }

    const cached = this.statusCache.get(datasetId);
    if (cached) {
      return cached;
    }

    const request$ = this.http
      .get<ApiResponse<DatasetIndexStatus>>(`${this.baseUrl}/api/v1/datasets/${datasetId}/index/status`)
      .pipe(
        shareReplay(1),
        catchError(error => {
          this.statusCache.delete(datasetId);
          return throwError(() => error);
        })
      );

    this.statusCache.set(datasetId, request$);
    return request$;
  }

  clearDatasetCache(datasetId: string): void {
    this.indexCache.delete(datasetId);
    this.statusCache.delete(datasetId);
  }

  clearAllCache(): void {
    this.indexCache.clear();
    this.statusCache.clear();
  }
}
