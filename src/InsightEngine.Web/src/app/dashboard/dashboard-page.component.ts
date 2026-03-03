import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { DatasetApiService } from '../core/services/dataset-api.service';
import { AuthService } from '../core/services/auth.service';
import { DataSetSummary } from '../core/models/dataset.model';
import { DashboardViewModel } from './models/dashboard.model';
import { DashboardService } from './dashboard.service';
import { DatasetSwitcherComponent } from './components/dataset-switcher.component';
import { KpiCardsComponent } from './components/kpi-cards.component';
import { ChartsGridComponent } from './components/charts-grid.component';
import { TopFeaturesTableComponent } from './components/top-features-table.component';
import { DataQualityTableComponent } from './components/data-quality-table.component';
import { InsightsPanelComponent } from './components/insights-panel.component';
import { DatasetMetaComponent } from './components/dataset-meta.component';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    DatasetSwitcherComponent,
    KpiCardsComponent,
    ChartsGridComponent,
    TopFeaturesTableComponent,
    DataQualityTableComponent,
    InsightsPanelComponent,
    DatasetMetaComponent
  ],
  templateUrl: './dashboard-page.component.html',
  styleUrls: ['./dashboard-page.component.scss']
})
export class DashboardPageComponent implements OnInit {
  datasets: DataSetSummary[] = [];
  selectedDatasetId: string | null = null;
  dashboard: DashboardViewModel | null = null;
  loading = false;
  lang = 'pt-br';
  errorMessage: string | null = null;

  constructor(
    private readonly datasetApi: DatasetApiService,
    private readonly dashboardService: DashboardService,
    private readonly authService: AuthService,
    private readonly route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.lang = this.route.snapshot.paramMap.get('lang') || 'pt-br';
    this.loadDatasets();
  }

  onDatasetChange(datasetId: string): void {
    this.selectedDatasetId = datasetId;
    this.persistSelection(datasetId);
    this.loadDashboard();
  }

  refreshDashboard(): void {
    this.loadDashboard();
  }

  get selectedDataset(): DataSetSummary | undefined {
    if (!this.selectedDatasetId) {
      return undefined;
    }

    return this.datasets.find(item => item.datasetId === this.selectedDatasetId);
  }

  private loadDatasets(): void {
    this.datasetApi.listDatasets().subscribe({
      next: response => {
        this.datasets = response.data || [];
        const preferred = this.resolvePreferredDatasetId();
        this.selectedDatasetId = preferred;
        if (this.selectedDatasetId) {
          this.loadDashboard();
        }
      },
      error: () => {
        this.datasets = [];
        this.errorMessage = 'Falha ao carregar datasets.';
      }
    });
  }

  private loadDashboard(): void {
    if (!this.selectedDatasetId) {
      this.dashboard = null;
      return;
    }

    this.errorMessage = null;
    this.loading = true;
    this.dashboardService.getDashboard(this.selectedDatasetId).subscribe({
      next: response => {
        this.loading = false;
        this.dashboard = response.data || null;
      },
      error: err => {
        this.loading = false;
        this.dashboard = null;
        const message =
          err?.error?.errors?.[0]?.message ||
          err?.error?.message ||
          err?.message ||
          'Não foi possível carregar o dashboard.';
        this.errorMessage = message;
      }
    });
  }

  private resolvePreferredDatasetId(): string | null {
    if (this.datasets.length === 0) {
      return null;
    }

    const saved = this.readPersistedSelection();
    if (saved && this.datasets.some(item => item.datasetId === saved)) {
      return saved;
    }

    return this.datasets[0].datasetId;
  }

  private get storageKey(): string {
    const userId = this.authService.currentUser?.id || 'anon';
    return `dashboard:lastDataset:${userId}`;
  }

  private persistSelection(datasetId: string): void {
    localStorage.setItem(this.storageKey, datasetId);
  }

  private readPersistedSelection(): string | null {
    return localStorage.getItem(this.storageKey);
  }
}
