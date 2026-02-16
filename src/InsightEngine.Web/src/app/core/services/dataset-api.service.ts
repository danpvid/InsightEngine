import { Injectable } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../environments/environment.development';
import { ApiResponse } from '../models/api-response.model';
import { UploadDatasetResponse, DataSetSummary, DatasetProfile } from '../models/dataset.model';
import { RecommendationsResponse } from '../models/recommendation.model';
import { ChartResponse, ScenarioSimulationRequest, ScenarioSimulationResponse } from '../models/chart.model';

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
   * Get dataset profile (schema + stats)
   */
  getProfile(datasetId: string): Observable<ApiResponse<DatasetProfile>> {
    return this.http.get<ApiResponse<DatasetProfile>>(
      `${this.baseUrl}/api/v1/datasets/${datasetId}/profile`
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
      metricY?: string;
      yColumn?: string;
      groupBy?: string;
      filters?: string[];
    }
  ): Observable<ApiResponse<ChartResponse>> {
    let url = `${this.baseUrl}/api/v1/datasets/${datasetId}/charts/${recommendationId}`;
    
    console.log('🌐 dataset-api.service: getChart called with options:', options);
    
    if (options) {
      const params = new URLSearchParams();
      if (options.aggregation) params.append('aggregation', options.aggregation);
      if (options.timeBin) params.append('timeBin', options.timeBin);
      if (options.metricY) params.append('metricY', options.metricY);
      if (options.yColumn) params.append('yColumn', options.yColumn);
      if (options.groupBy) params.append('groupBy', options.groupBy);
      if (options.filters && options.filters.length > 0) {
        options.filters.forEach(filter => params.append('filters', filter));
      }
      
      const queryString = params.toString();
      if (queryString) {
        url += `?${queryString}`;
        console.log('🌐 Final URL:', url);
      }
    }

    return this.http.get<ApiResponse<ChartResponse>>(url);
  }

  simulate(
    datasetId: string,
    payload: ScenarioSimulationRequest
  ): Observable<ApiResponse<ScenarioSimulationResponse>> {
    return this.http.post<ApiResponse<ScenarioSimulationResponse>>(
      `${this.baseUrl}/api/v1/datasets/${datasetId}/simulate`,
      payload
    );
  }
}
