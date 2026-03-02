import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment.development';
import { ApiResponse } from '../core/models/api-response.model';
import { DashboardViewModel } from './models/dashboard.model';

@Injectable({
  providedIn: 'root'
})
export class DashboardService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getDashboard(datasetId: string): Observable<ApiResponse<DashboardViewModel>> {
    return this.http.get<ApiResponse<DashboardViewModel>>(
      `${this.baseUrl}/api/v1/dashboard?datasetId=${encodeURIComponent(datasetId)}`
    );
  }
}
