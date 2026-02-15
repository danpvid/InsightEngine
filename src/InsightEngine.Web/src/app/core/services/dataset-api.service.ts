import { Injectable } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../environments/environment.development';
import { ApiResponse } from '../models/api-response.model';
import { UploadDatasetResponse, DataSetSummary } from '../models/dataset.model';
import { RecommendationsResponse } from '../models/recommendation.model';
import { ChartResponse } from '../models/chart.model';

export interface UploadProgress {
  progress: number;
  response?: ApiResponse<UploadDatasetResponse>;
}

@Injectable({
  providedIn: 'root'
})
export class DatasetApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  /**
   * List all datasets
   */
  listDatasets(): Observable<ApiResponse<DataSetSummary[]>> {
    return this.http.get<ApiResponse<DataSetSummary[]>>(
      `${this.baseUrl}/api/v1/datasets`
    );
  }

  /**
   * Upload CSV file to create a new dataset
   */
  uploadDataset(file: File): Observable<ApiResponse<UploadDatasetResponse>> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<ApiResponse<UploadDatasetResponse>>(
      `${this.baseUrl}/api/v1/datasets`,
      formData
    );
  }

  /**
   * Upload CSV file with progress tracking
   */
  uploadDatasetWithProgress(file: File): Observable<UploadProgress> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<ApiResponse<UploadDatasetResponse>>(
      `${this.baseUrl}/api/v1/datasets`,
      formData,
      {
        reportProgress: true,
        observe: 'events'
      }
    ).pipe(
      map((event: HttpEvent<any>) => {
        if (event.type === HttpEventType.UploadProgress) {
          const progress = event.total ? Math.round((100 * event.loaded) / event.total) : 0;
          return { progress };
        } else if (event.type === HttpEventType.Response) {
          return { progress: 100, response: event.body };
        }
        return { progress: 0 };
      })
    );
  }

  /**
   * Get chart recommendations for a dataset
   */
  getRecommendations(datasetId: string): Observable<ApiResponse<RecommendationsResponse>> {
    return this.http.get<ApiResponse<RecommendationsResponse>>(
      `${this.baseUrl}/api/v1/datasets/${datasetId}/recommendations`
    );
  }

  /**
   * Get chart data for a specific recommendation with optional dynamic parameters
   */
  getChart(
    datasetId: string, 
    recommendationId: string,
    options?: {
      aggregation?: string;
      timeBin?: string;
      yColumn?: string;
    }
  ): Observable<ApiResponse<ChartResponse>> {
    let url = `${this.baseUrl}/api/v1/datasets/${datasetId}/charts/${recommendationId}`;
    
    console.log('🌐 dataset-api.service: getChart called with options:', options);
    
    if (options) {
      const params = new URLSearchParams();
      if (options.aggregation) params.append('aggregation', options.aggregation);
      if (options.timeBin) params.append('timeBin', options.timeBin);
      if (options.yColumn) params.append('yColumn', options.yColumn);
      
      const queryString = params.toString();
      if (queryString) {
        url += `?${queryString}`;
        console.log('🌐 Final URL:', url);
      }
    }

    return this.http.get<ApiResponse<ChartResponse>>(url);
  }
}
