import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgxEchartsModule, NgxEchartsDirective } from 'ngx-echarts';
import { DatasetApiService } from '../../../../core/services/dataset-api.service';
import { ToastService } from '../../../../core/services/toast.service';
import { HttpErrorUtil } from '../../../../core/util/http-error.util';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { LoadingBarComponent } from '../../../../shared/components/loading-bar/loading-bar.component';
import { ErrorPanelComponent } from '../../../../shared/components/error-panel/error-panel.component';
import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { ChartMeta } from '../../../../core/models/chart.model';
import { ChartRecommendation } from '../../../../core/models/recommendation.model';
import { ApiError } from '../../../../core/models/api-response.model';
import { EChartsOption, ECharts } from 'echarts';

@Component({
  selector: 'app-chart-viewer-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    NgxEchartsModule,
    ...MATERIAL_MODULES,
    LoadingBarComponent,
    ErrorPanelComponent,
    PageHeaderComponent
  ],
  templateUrl: './chart-viewer-page.component.html',
  styleUrls: ['./chart-viewer-page.component.scss']
})
export class ChartViewerPageComponent implements OnInit {
  datasetId: string = '';
  recommendationId: string = '';
  chartOption: EChartsOption | null = null;
  chartMeta: ChartMeta | null = null;
  loading: boolean = false;
  error: ApiError | null = null;
  
  // ECharts instance for export
  private echartsInstance?: ECharts;

  // Navigation & Recommendations
  recommendations: ChartRecommendation[] = [];
  currentRecommendation: ChartRecommendation | null = null;
  currentIndex: number = -1;
  loadingRecommendations: boolean = false;

  // Chart Controls  
  availableAggregations: string[] = ['Sum', 'Avg', 'Count', 'Min', 'Max'];
  availableTimeBins: string[] = ['Day', 'Week', 'Month', 'Quarter', 'Year'];
  availableMetrics: string[] = [];

  selectedAggregation: string = 'Sum';
  selectedTimeBin: string = 'Month';
  selectedMetric: string = '';

  // Performance badge
  get isCached(): boolean {
    return (this.chartMeta?.executionMs || 0) < 100;
  }

  // Insight Summary (mockado para MVP - futuro: LLM)
  getInsightSummary(): string {
    if (!this.currentRecommendation) {
      return 'Analisando dados...';
    }

    const chartType = this.currentRecommendation.chart?.type || 'Line';
    const metric = this.selectedMetric || this.currentRecommendation.yColumn || 'valor';
    const agg = this.selectedAggregation;  // Use selected values, not original recommendation
    const timeBin = this.selectedTimeBin;  // Use selected values, not original recommendation

    // Mock insights baseados no tipo de gráfico
    const insights: Record<string, string> = {
      'Line': `A ${metric} (${agg}) apresenta tendência ao longo do tempo por ${timeBin}. ` +
              `Identifique padrões sazonais e picos de atividade para melhor planejamento.`,
      'Bar': `Comparação de ${metric} (${agg}) entre diferentes categorias. ` +
             `As barras destacam os maiores e menores valores, facilitando identificação de outliers.`,
      'Scatter': `Visualização de correlação entre variáveis. ` +
                 `Pontos agrupados indicam relacionamento entre ${this.currentRecommendation.xColumn} e ${metric}.`,
      'Histogram': `Distribuição de frequência de ${metric}. ` +
                   `Identifique concentrações de valores e padrões de distribuição nos dados.`,
      'Pie': `Proporção de ${metric} entre categorias. ` +
             `Visualize facilmente a participação relativa de cada segmento no total.`
    };

    return insights[chartType] || `Análise de ${metric} por ${this.currentRecommendation.xColumn}.`;
  }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private datasetApi: DatasetApiService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.datasetId = this.route.snapshot.paramMap.get('datasetId') || '';
    this.recommendationId = this.route.snapshot.paramMap.get('recommendationId') || '';
    
    if (!this.datasetId || !this.recommendationId) {
      this.error = {
        code: 'MISSING_PARAMETERS',
        message: 'Parâmetros necessários não fornecidos.'
      };
      return;
    }

    // Load recommendations (will read query params and apply to controls)
    this.loadRecommendations();

    // Read query params from URL and load chart with them
    const queryParams = this.route.snapshot.queryParamMap;
    const aggFromUrl = queryParams.get('aggregation');
    const timeBinFromUrl = queryParams.get('timeBin');
    const yColumnFromUrl = queryParams.get('yColumn');

    console.log('📍 URL Query Params:', { aggregation: aggFromUrl, timeBin: timeBinFromUrl, yColumn: yColumnFromUrl });

    // Load chart with params from URL if present
    if (aggFromUrl || timeBinFromUrl || yColumnFromUrl) {
      const options: any = {};
      if (aggFromUrl) options.aggregation = aggFromUrl;
      if (timeBinFromUrl) options.timeBin = timeBinFromUrl;
      if (yColumnFromUrl) options.yColumn = yColumnFromUrl;
      this.loadChart(options);
    } else {
      this.loadChart();
    }
  }

  loadRecommendations(): void {
    this.loadingRecommendations = true;

    this.datasetApi.getRecommendations(this.datasetId).subscribe({
      next: (response) => {
        this.loadingRecommendations = false;
        if (response.success && response.data) {
          this.recommendations = Array.isArray(response.data) ? response.data : [];
          this.currentIndex = this.recommendations.findIndex(r => r.id === this.recommendationId);
          this.currentRecommendation = this.recommendations[this.currentIndex] || null;

          // Extract available metrics from current recommendation
          if (this.currentRecommendation) {
            // Check if URL has query params (shared link) - they take precedence
            const queryParams = this.route.snapshot.queryParamMap;
            const aggFromUrl = queryParams.get('aggregation');
            const timeBinFromUrl = queryParams.get('timeBin');
            const yColumnFromUrl = queryParams.get('yColumn');

            // Use URL params if present, otherwise use recommendation defaults
            this.selectedAggregation = aggFromUrl || this.currentRecommendation.aggregation || 'Sum';
            this.selectedTimeBin = timeBinFromUrl || this.currentRecommendation.timeBin || 'Month';
            this.selectedMetric = yColumnFromUrl || this.currentRecommendation.yColumn || '';
          }
        }
      },
      error: (err) => {
        this.loadingRecommendations = false;
        console.error('Error loading recommendations for navigation:', err);
      }
    });
  }

  loadChart(options?: { aggregation?: string; timeBin?: string; yColumn?: string }): void {
    this.loading = true;
    this.error = null;
    
    // Force chart to clear before loading new data
    this.chartOption = null;

    console.log('📊 Loading chart with options:', options);

    this.datasetApi.getChart(this.datasetId, this.recommendationId, options).subscribe({
      next: (response) => {
        this.loading = false;
        
        if (response.success && response.data) {
          // Force new reference to trigger change detection
          this.chartOption = { ...response.data.option };
          this.chartMeta = response.data.meta || null;
          
          console.log('✅ Chart loaded:', {
            rowCountReturned: this.chartMeta?.rowCountReturned,
            executionMs: this.chartMeta?.executionMs,
            chartType: this.chartMeta?.chartType
          });
          
          if (!this.chartOption) {
            this.error = {
              code: 'NO_CHART_DATA',
              message: 'Nenhum dado de gráfico retornado.'
            };
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
        this.toast.error('Erro ao carregar gráfico');
      }
    });
  }

  goBackToRecommendations(): void {
    this.router.navigate(['/datasets', this.datasetId, 'recommendations']);
  }

  refreshChart(): void {
    // Refresh with current controls state
    const options: any = {
      aggregation: this.selectedAggregation,
      timeBin: this.selectedTimeBin
    };
    
    if (this.selectedMetric) {
      options.yColumn = this.selectedMetric;
    }

    this.loadChart(options);
  }

  onChartInit(ec: ECharts): void {
    this.echartsInstance = ec;
  }

  // Navigation methods
  goToPrevious(): void {
    if (this.currentIndex > 0) {
      const prevRec = this.recommendations[this.currentIndex - 1];
      this.navigateToRecommendation(prevRec.id);
    }
  }

  goToNext(): void {
    if (this.currentIndex < this.recommendations.length - 1) {
      const nextRec = this.recommendations[this.currentIndex + 1];
      this.navigateToRecommendation(nextRec.id);
    }
  }

  onRecommendationChange(recommendationId: string): void {
    if (recommendationId && recommendationId !== this.recommendationId) {
      this.navigateToRecommendation(recommendationId);
    }
  }

  navigateToRecommendation(recId: string): void {
    // Navigate to new recommendation and clear query params (reset to defaults)
    this.router.navigate(['/datasets', this.datasetId, 'charts', recId]).then(() => {
      this.recommendationId = recId;
      this.currentIndex = this.recommendations.findIndex(r => r.id === recId);
      this.currentRecommendation = this.recommendations[this.currentIndex] || null;
      
      // Update controls to recommendation defaults
      if (this.currentRecommendation) {
        this.selectedAggregation = this.currentRecommendation.aggregation || 'Sum';
        this.selectedTimeBin = this.currentRecommendation.timeBin || 'Month';
        this.selectedMetric = this.currentRecommendation.yColumn || '';
      }

      this.loadChart();
    });
  }

  get hasPrevious(): boolean {
    return this.currentIndex > 0;
  }

  get hasNext(): boolean {
    return this.currentIndex >= 0 && this.currentIndex < this.recommendations.length - 1;
  }

  // Chart control methods - now with real backend support
  onAggregationChange(): void {
    console.log('🔄 Aggregation changed to:', this.selectedAggregation);
    this.reloadChartWithCurrentParameters();
  }

  onTimeBinChange(): void {
    console.log('🔄 TimeBin changed to:', this.selectedTimeBin);
    this.reloadChartWithCurrentParameters();
  }

  onMetricChange(): void {
    console.log('🔄 Metric changed to:', this.selectedMetric);
    this.reloadChartWithCurrentParameters();
  }

  private reloadChartWithCurrentParameters(): void {
    // Always send ALL current control values, not just what changed
    // Backend always starts from original recommendation, so we need full state
    const options: any = {
      aggregation: this.selectedAggregation,
      timeBin: this.selectedTimeBin
    };
    
    // Only include yColumn if it's set
    if (this.selectedMetric) {
      options.yColumn = this.selectedMetric;
    }

    console.log('🎮 CONTROL VALUES:', {
      selectedAggregation: this.selectedAggregation,
      selectedTimeBin: this.selectedTimeBin,
      selectedMetric: this.selectedMetric
    });
    console.log('📡 Options being sent to backend:', JSON.stringify(options));

    // Update URL with query params (for shareable links)
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: options,
      queryParamsHandling: 'merge', // Keep other params if any
      replaceUrl: true // Don't create new history entry
    });

    this.loadChart(options);
  }

  getChartTitle(): string {
    if (this.chartOption && typeof this.chartOption === 'object') {
      const option = this.chartOption as any;
      if (option.title && option.title.text) {
        return option.title.text;
      }
    }
    return 'Visualização de Gráfico';
  }

  formatExecutionTime(ms?: number): string {
    if (!ms) return 'N/A';
    return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(2)}s`;
  }

  copyChartLink(): void {
    const url = window.location.href;
    navigator.clipboard.writeText(url).then(() => {
      this.toast.success('Link copiado! Inclui controles aplicados.');
    });
  }

  exportChartPNG(): void {
    if (!this.echartsInstance) {
      this.toast.error('Gráfico ainda não foi carregado');
      return;
    }

    try {
      // Get chart as base64 PNG
      const imageDataUrl = this.echartsInstance.getDataURL({
        type: 'png',
        pixelRatio: 2, // Higher quality
        backgroundColor: '#fff'
      });

      // Create download link
      const link = document.createElement('a');
      const fileName = `chart_${this.datasetId}_${this.recommendationId}_${Date.now()}.png`;
      link.download = fileName;
      link.href = imageDataUrl;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);

      this.toast.success('Gráfico exportado com sucesso!');
    } catch (error) {
      console.error('Error exporting chart:', error);
      this.toast.error('Erro ao exportar gráfico');
    }
  }
}
