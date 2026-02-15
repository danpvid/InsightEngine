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
  recommendations: ChartRecommendation[] = [];
  filteredRecommendations: ChartRecommendation[] = [];
  loading: boolean = false;
  error: ApiError | null = null;

  // Filters
  selectedChartType: string = 'All';
  sortBy: string = 'default';

  chartTypes: string[] = ['All', 'Line', 'Bar', 'Scatter', 'Histogram', 'Pie'];

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
        message: 'ID do dataset não fornecido.'
      };
      return;
    }

    this.loadRecommendations();
  }

  loadRecommendations(): void {
    this.loading = true;
    this.error = null;

    this.datasetApi.getRecommendations(this.datasetId).subscribe({
      next: (response) => {
        this.loading = false;
        
        if (response.success && response.data) {
          // Backend retorna array direto no data
          this.recommendations = Array.isArray(response.data) ? response.data : [];
          this.applyFilters();
          
          if (this.recommendations.length === 0) {
            this.toast.info('Nenhuma recomendação encontrada para este dataset.');
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
        this.toast.error('Erro ao carregar recomendações');
      }
    });
  }

  viewChart(recommendation: ChartRecommendation): void {
    this.router.navigate(['/datasets', this.datasetId, 'charts', recommendation.id]);
  }

  getChartTypeColor(chartType: string): string {
    const colors: Record<string, string> = {
      'Line': 'primary',
      'Bar': 'accent',
      'Scatter': 'warn',
      'Histogram': 'primary',
      'Pie': 'accent'
    };
    return colors[chartType] || 'primary';
  }

  getChartTypeIcon(chartType: string): string {
    const icons: Record<string, string> = {
      'Line': 'show_chart',
      'Bar': 'bar_chart',
      'Scatter': 'scatter_plot',
      'Histogram': 'equalizer',
      'Pie': 'pie_chart'
    };
    return icons[chartType] || 'insert_chart';
  }

  // Helper para pegar o tipo do chart do objeto aninhado
  getChartType(rec: ChartRecommendation): string {
    return rec.chart?.type || 'Line';
  }

  // Filter & Sort methods
  applyFilters(): void {
    let filtered = [...this.recommendations];

    // Filter by chart type
    if (this.selectedChartType !== 'All') {
      filtered = filtered.filter(rec => this.getChartType(rec) === this.selectedChartType);
    }

    // Sort
    if (this.sortBy === 'type') {
      filtered.sort((a, b) => this.getChartType(a).localeCompare(this.getChartType(b)));
    } else if (this.sortBy === 'title') {
      filtered.sort((a, b) => a.title.localeCompare(b.title));
    }

    this.filteredRecommendations = filtered;
  }

  onChartTypeChange(): void {
    this.applyFilters();
  }

  onSortChange(): void {
    this.applyFilters();
  }

  copyDatasetId(): void {
    navigator.clipboard.writeText(this.datasetId).then(() => {
      this.toast.success('ID copiado para área de transferência');
    });
  }
}
