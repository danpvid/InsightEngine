import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatasetApiService } from '../../../../core/services/dataset-api.service';
import { ToastService } from '../../../../core/services/toast.service';
import { HttpErrorUtil } from '../../../../core/util/http-error.util';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { LoadingBarComponent } from '../../../../shared/components/loading-bar/loading-bar.component';
import { ErrorPanelComponent } from '../../../../shared/components/error-panel/error-panel.component';
import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { SkeletonCardComponent } from '../../../../shared/components/skeleton-card/skeleton-card.component';
import { ChartRecommendation } from '../../../../core/models/recommendation.model';
import { ApiError } from '../../../../core/models/api-response.model';

@Component({
  selector: 'app-recommendations-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    ...MATERIAL_MODULES,
    LoadingBarComponent,
    ErrorPanelComponent,
    PageHeaderComponent,
    SkeletonCardComponent
  ],
  templateUrl: './recommendations-page.component.html',
  styleUrls: ['./recommendations-page.component.scss']
})
export class RecommendationsPageComponent implements OnInit {
  datasetId: string = '';
  datasetName: string = '';
  recommendations: ChartRecommendation[] = [];
  filteredRecommendations: ChartRecommendation[] = [];
  loading: boolean = false;
  error: ApiError | null = null;

  selectedChartType: string = 'All';
  sortBy: string = 'score';

  chartTypes: string[] = ['All', 'Line', 'Bar', 'Scatter', 'Histogram'];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private datasetApi: DatasetApiService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.datasetId = this.route.snapshot.paramMap.get('datasetId') || '';

    if (!this.datasetId) {
      this.error = {
        code: 'MISSING_DATASET_ID',
        message: 'ID do dataset nao fornecido.'
      };
      return;
    }

    this.loadDatasetName();
    this.loadRecommendations();
  }

  private loadDatasetName(): void {
    this.datasetApi.listDatasets().subscribe({
      next: (response) => {
        if (!response.success || !response.data) {
          return;
        }

        const dataset = response.data.find(item => item.datasetId.toLowerCase() === this.datasetId.toLowerCase());
        this.datasetName = dataset?.originalFileName || '';
      },
      error: (err) => {
        console.error('Error loading dataset name:', err);
      }
    });
  }

  loadRecommendations(): void {
    this.loading = true;
    this.error = null;

    this.datasetApi.getRecommendations(this.datasetId).subscribe({
      next: (response) => {
        this.loading = false;

        if (response.success && response.data) {
          this.recommendations = Array.isArray(response.data) ? response.data : [];
          this.applyFilters();

          if (this.recommendations.length === 0) {
            this.toast.info('Nenhuma recomendacao encontrada para este dataset.');
          }
        } else if (response.error) {
          this.error = response.error;
        }
      },
      error: (err) => {
        this.loading = false;
        const apiError = HttpErrorUtil.extractApiError(err);
        if (apiError) {
          this.error = apiError;
        } else {
          this.error = {
            code: 'LOAD_ERROR',
            message: HttpErrorUtil.extractErrorMessage(err)
          };
        }
        this.toast.error('Erro ao carregar recomendacoes');
      }
    });
  }

  viewChart(recommendation: ChartRecommendation): void {
    this.router.navigate(['/datasets', this.datasetId, 'charts', recommendation.id]);
  }

  getChartTypeColor(chartType: string): string {
    const colors: Record<string, string> = {
      Line: 'primary',
      Bar: 'accent',
      Scatter: 'warn',
      Histogram: 'primary'
    };
    return colors[chartType] || 'primary';
  }

  getChartTypeIcon(chartType: string): string {
    const icons: Record<string, string> = {
      Line: 'show_chart',
      Bar: 'bar_chart',
      Scatter: 'scatter_plot',
      Histogram: 'equalizer'
    };
    return icons[chartType] || 'insert_chart';
  }

  getChartType(rec: ChartRecommendation): string {
    return rec.chart?.type || 'Line';
  }

  getScore(rec: ChartRecommendation): number {
    return rec.score ?? 0;
  }

  getImpactScore(rec: ChartRecommendation): number {
    return rec.impactScore ?? 0;
  }

  formatScore(score?: number): string {
    const safeScore = score ?? 0;
    return safeScore.toFixed(2);
  }

  applyFilters(): void {
    let filtered = [...this.recommendations];

    if (this.selectedChartType !== 'All') {
      filtered = filtered.filter(rec => this.getChartType(rec) === this.selectedChartType);
    }

    const decorated = filtered.map((rec, index) => ({ rec, index }));

    decorated.sort((left, right) => {
      if (this.sortBy === 'score') {
        return this.compareStable(
          right.rec.score ?? 0,
          left.rec.score ?? 0,
          right.rec.impactScore ?? 0,
          left.rec.impactScore ?? 0,
          left.index,
          right.index);
      }

      if (this.sortBy === 'impact') {
        return this.compareStable(
          right.rec.impactScore ?? 0,
          left.rec.impactScore ?? 0,
          right.rec.score ?? 0,
          left.rec.score ?? 0,
          left.index,
          right.index);
      }

      if (this.sortBy === 'type') {
        const typeCompare = this.getChartType(left.rec).localeCompare(this.getChartType(right.rec));
        if (typeCompare !== 0) {
          return typeCompare;
        }

        return this.compareStable(
          right.rec.score ?? 0,
          left.rec.score ?? 0,
          right.rec.impactScore ?? 0,
          left.rec.impactScore ?? 0,
          left.index,
          right.index);
      }

      if (this.sortBy === 'title') {
        const titleCompare = left.rec.title.localeCompare(right.rec.title);
        if (titleCompare !== 0) {
          return titleCompare;
        }

        return left.index - right.index;
      }

      return left.index - right.index;
    });

    this.filteredRecommendations = decorated.map(item => item.rec);
  }

  private compareStable(
    primaryLeft: number,
    primaryRight: number,
    secondaryLeft: number,
    secondaryRight: number,
    leftIndex: number,
    rightIndex: number): number {
    const primary = primaryLeft - primaryRight;
    if (primary !== 0) {
      return primary;
    }

    const secondary = secondaryLeft - secondaryRight;
    if (secondary !== 0) {
      return secondary;
    }

    return leftIndex - rightIndex;
  }

  onChartTypeChange(): void {
    this.applyFilters();
  }

  onSortChange(): void {
    this.applyFilters();
  }

  copyDatasetId(): void {
    navigator.clipboard.writeText(this.datasetId).then(() => {
      this.toast.success('ID copiado para a area de transferencia');
    });
  }
}
