import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgxEchartsModule } from 'ngx-echarts';
import { ECharts, EChartsOption } from 'echarts';
import { catchError, forkJoin, map, of } from 'rxjs';
import { PageEvent } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { DatasetApiService } from '../../../../core/services/dataset-api.service';
import { ToastService } from '../../../../core/services/toast.service';
import { LanguageService } from '../../../../core/services/language.service';
import { TranslatePipe } from '../../../../core/pipes/translate.pipe';
import { HttpErrorUtil } from '../../../../core/util/http-error.util';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { LoadingBarComponent } from '../../../../shared/components/loading-bar/loading-bar.component';
import { ErrorPanelComponent } from '../../../../shared/components/error-panel/error-panel.component';
import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import {
  AskAnalysisPlanResponse,
  AiGenerationMeta,
  AiInsightSummary,
  ChartMeta,
  ExplainChartResponse,
  InsightSummary,
  ScenarioFilterRequest,
  ScenarioOperationRequest,
  ScenarioOperationType,
  ScenarioSimulationRequest,
  ScenarioSimulationResponse
} from '../../../../core/models/chart.model';
import { ChartRecommendation } from '../../../../core/models/recommendation.model';
import { ApiError } from '../../../../core/models/api-response.model';
import { DataSetSummary, DatasetColumnProfile, RawDatasetRow, RawDatasetRowsResponse } from '../../../../core/models/dataset.model';
import { environment } from '../../../../../environments/environment';

interface FilterRule {
  column: string;
  operator: string;
  value: string;
}

interface SimulationOperationRule {
  type: ScenarioOperationType;
  column: string;
  values: string;
  factor: number | null;
  constant: number | null;
  min: number | null;
  max: number | null;
}

type VisualizationType = 'Line' | 'Bar' | 'Scatter' | 'Histogram';

interface ChartLoadOptions {
  aggregation?: string;
  timeBin?: string;
  metricY?: string;
  yColumn?: string;
  groupBy?: string;
  filters?: string[];
}

interface MetricChartResult {
  metric: string;
  option: EChartsOption | null;
}

interface ChartTableRow {
  [key: string]: string | number | null;
}

@Component({
  selector: 'app-chart-viewer-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    TranslatePipe,
    NgxEchartsModule,
    ...MATERIAL_MODULES,
    LoadingBarComponent,
    ErrorPanelComponent,
    PageHeaderComponent
  ],
  templateUrl: './chart-viewer-page.component.html',
  styleUrls: ['./chart-viewer-page.component.scss']
})
export class ChartViewerPageComponent implements OnInit, OnDestroy {
  datasetId: string = '';
  datasetName: string = '';
  recommendationId: string = '';
  chartOption: EChartsOption | null = null;
  chartMeta: ChartMeta | null = null;
  insightSummary: InsightSummary | null = null;
  aiSummary: AiInsightSummary | null = null;
  aiSummaryMeta: AiGenerationMeta | null = null;
  aiSummaryLoading: boolean = false;
  aiSummaryError: string | null = null;
  explainResult: ExplainChartResponse | null = null;
  explainLoading: boolean = false;
  explainError: string | null = null;
  explainPanelOpen: boolean = false;
  askQuestion: string = '';
  askLoading: boolean = false;
  askError: string | null = null;
  askPlan: AskAnalysisPlanResponse | null = null;
  askReasoningExpanded: boolean = false;
  loading: boolean = false;
  error: ApiError | null = null;

  private echartsInstance?: ECharts;
  private simulationEchartsInstance?: ECharts;
  private filterTimer?: number;
  private rawSearchTimer?: number;
  private zoomTimer?: number;
  private chartLoadVersion: number = 0;

  recommendations: ChartRecommendation[] = [];
  currentRecommendation: ChartRecommendation | null = null;
  currentIndex: number = -1;
  loadingRecommendations: boolean = false;

  navigationSearch: string = '';
  keepNavigationContext: boolean = true;

  availableAggregations: string[] = ['Sum', 'Avg', 'Count', 'Min', 'Max'];
  availableTimeBins: string[] = ['Day', 'Week', 'Month', 'Quarter', 'Year'];
  availableMetrics: string[] = [];
  availableGroupBy: string[] = [];
  profileColumns: DatasetColumnProfile[] = [];

  filterOperators = [
    { value: 'Eq', label: '=' },
    { value: 'NotEq', label: '!=' },
    { value: 'In', label: 'in' },
    { value: 'Between', label: 'between' },
    { value: 'Contains', label: 'contains' }
  ];
  filterRules: FilterRule[] = [];

  selectedAggregation: string = 'Sum';
  selectedTimeBin: string = 'Month';
  selectedMetric: string = '';
  selectedMetricsY: string[] = [];
  metricToAdd: string = '';
  selectedGroupBy: string = '';
  selectedVisualizationType: VisualizationType | '' = '';
  zoomStart: number | null = null;
  zoomEnd: number | null = null;
  pendingDrilldownCategory: string | null = null;
  pointDataColumns: string[] = [];
  pointDataRows: ChartTableRow[] = [];
  pointDataSearch: string = '';
  selectedPoint: ChartTableRow | null = null;
  selectedPointModalOpen: boolean = false;

  rawDataColumns: string[] = [];
  rawDataRows: RawDatasetRow[] = [];
  rawDataSearch: string = '';
  rawDataLoading: boolean = false;
  rawDataError: string | null = null;
  rawTotalRows: number = 0;
  rawTotalPages: number = 0;
  rawPageIndex: number = 0;
  rawPageSize: number = 100;
  readonly rawPageSizeOptions: number[] = [50, 100, 250, 500];
  rawSortColumn: string = '';
  rawSortDirection: 'asc' | 'desc' = 'asc';

  filterPreviewPending: boolean = false;

  controlsOpen: boolean = true;
  isMobile: boolean = false;

  activeTab: number = 0;
  simulationLoading: boolean = false;
  simulationError: string | null = null;
  simulationResult: ScenarioSimulationResponse | null = null;
  simulationChartOption: EChartsOption | null = null;
  simulationTargetMetric: string = '';
  simulationTargetDimension: string = '';
  simulationOperations: SimulationOperationRule[] = [];
  readonly simulationOperationTypes: { value: ScenarioOperationType; label: string }[] = [
    { value: 'MultiplyMetric', label: 'Multiply Metric' },
    { value: 'AddConstant', label: 'Add Constant' },
    { value: 'Clamp', label: 'Clamp' },
    { value: 'RemoveCategory', label: 'Remove Category' },
    { value: 'FilterOut', label: 'Filter Out' }
  ];

  get isCached(): boolean {
    return !!this.chartMeta?.cacheHit;
  }

  get hasPrevious(): boolean {
    return this.currentIndex > 0;
  }

  get hasNext(): boolean {
    return this.currentIndex >= 0 && this.currentIndex < this.recommendations.length - 1;
  }

  get filteredNavigationRecommendations(): ChartRecommendation[] {
    const term = this.navigationSearch.trim().toLowerCase();
    if (!term) {
      return this.recommendations;
    }

    return this.recommendations.filter(rec => {
      const candidate = [
        rec.id,
        rec.title,
        rec.chart?.type || '',
        (rec.score ?? 0).toFixed(2)
      ].join(' ').toLowerCase();
      return candidate.includes(term);
    });
  }

  get canRunSimulation(): boolean {
    return !!this.simulationTargetMetric &&
      !!this.simulationTargetDimension &&
      this.simulationOperations.length > 0 &&
      !this.simulationLoading;
  }

  get currentBaseChartType(): VisualizationType {
    const raw = this.currentRecommendation?.chart?.type || this.chartMeta?.chartType || 'Line';
    return this.normalizeVisualizationType(raw);
  }

  get currentDisplayChartType(): VisualizationType {
    return this.selectedVisualizationType || this.currentBaseChartType;
  }

  get availableVisualizationTypes(): VisualizationType[] {
    if (this.currentBaseChartType === 'Line') {
      return ['Line', 'Bar'];
    }

    if (this.currentBaseChartType === 'Bar') {
      return ['Bar', 'Line'];
    }

    return [this.currentBaseChartType];
  }

  get supportsAggregationControl(): boolean {
    return this.currentBaseChartType === 'Line' || this.currentBaseChartType === 'Bar';
  }

  get supportsTimeBinControl(): boolean {
    return this.currentBaseChartType === 'Line';
  }

  get supportsMetricControl(): boolean {
    return this.currentBaseChartType !== 'Histogram';
  }

  get supportsMultiMetricControl(): boolean {
    return this.currentBaseChartType === 'Line' || this.currentBaseChartType === 'Bar';
  }

  get supportsGroupByControl(): boolean {
    return this.currentBaseChartType === 'Line' || this.currentBaseChartType === 'Bar';
  }

  get supportsDrilldown(): boolean {
    return this.currentDisplayChartType === 'Bar';
  }

  get canAddMetric(): boolean {
    return !!this.metricToAdd &&
      this.supportsMultiMetricControl &&
      !this.selectedMetricsY.includes(this.metricToAdd) &&
      this.selectedMetricsY.length < 4;
  }

  get filteredPointDataRows(): ChartTableRow[] {
    const term = this.pointDataSearch.trim().toLowerCase();
    if (!term) {
      return this.pointDataRows;
    }

    return this.pointDataRows.filter(row =>
      this.pointDataColumns.some(column => {
        const value = row[column];
        return `${value ?? ''}`.toLowerCase().includes(term);
      }));
  }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private datasetApi: DatasetApiService,
    private toast: ToastService,
    private languageService: LanguageService
  ) {}

  get currentLanguage(): string {
    return this.languageService.currentLanguage;
  }

  get newDatasetLink(): string {
    return `/${this.currentLanguage}/datasets/new`;
  }

  get recommendationsLink(): string {
    return `/${this.currentLanguage}/datasets/${this.datasetId}/recommendations`;
  }

  @HostListener('window:resize')
  onResize(): void {
    this.updateLayout();
  }

  ngOnInit(): void {
    this.updateLayout();
    this.datasetId = this.route.snapshot.paramMap.get('datasetId') || '';
    this.recommendationId = this.route.snapshot.paramMap.get('recommendationId') || '';

    if (!this.datasetId || !this.recommendationId) {
      this.error = {
        code: 'MISSING_PARAMETERS',
        message: 'Parametros necessarios nao fornecidos.'
      };
      return;
    }

    this.loadDatasetName();
    this.loadProfile();
    this.loadRecommendations();

    const queryParams = this.route.snapshot.queryParamMap;
    const aggFromUrl = queryParams.get('aggregation');
    const timeBinFromUrl = queryParams.get('timeBin');
    const yColumnFromUrl = queryParams.get('yColumn');
    const metricYFromUrl = queryParams.getAll('metricY');
    const groupByFromUrl = queryParams.get('groupBy');
    const chartTypeFromUrl = queryParams.get('chartType');
    const filtersFromUrl = queryParams.getAll('filters');
    const zoomStartFromUrl = queryParams.get('zoomStart');
    const zoomEndFromUrl = queryParams.get('zoomEnd');

    if (groupByFromUrl) {
      this.selectedGroupBy = groupByFromUrl;
    }

    if (chartTypeFromUrl) {
      this.selectedVisualizationType = this.normalizeVisualizationType(chartTypeFromUrl);
    }

    const parsedZoomStart = this.tryParsePercentage(zoomStartFromUrl);
    const parsedZoomEnd = this.tryParsePercentage(zoomEndFromUrl);
    if (parsedZoomStart !== null && parsedZoomEnd !== null) {
      this.zoomStart = parsedZoomStart;
      this.zoomEnd = parsedZoomEnd;
    }

    if (filtersFromUrl.length > 0) {
      this.filterRules = this.parseFiltersFromUrl(filtersFromUrl);
    }

    this.loadRawDataRows(true);

    if (metricYFromUrl.length > 0) {
      this.selectedMetricsY = this.distinctValues(metricYFromUrl);
      this.selectedMetric = this.selectedMetricsY[0] || '';
    } else if (yColumnFromUrl) {
      this.selectedMetric = yColumnFromUrl;
      this.selectedMetricsY = [yColumnFromUrl];
    }

    const shouldLoadWithOverrides =
      !!aggFromUrl ||
      !!timeBinFromUrl ||
      !!yColumnFromUrl ||
      metricYFromUrl.length > 0 ||
      !!groupByFromUrl ||
      !!chartTypeFromUrl ||
      filtersFromUrl.length > 0 ||
      (this.zoomStart !== null && this.zoomEnd !== null);

    if (shouldLoadWithOverrides) {
      const options: ChartLoadOptions = {};
      if (aggFromUrl) options.aggregation = aggFromUrl;
      if (timeBinFromUrl) options.timeBin = timeBinFromUrl;
      if (this.selectedMetric) options.metricY = this.selectedMetric;
      if (yColumnFromUrl) options.yColumn = yColumnFromUrl;
      if (groupByFromUrl) options.groupBy = groupByFromUrl;
      if (filtersFromUrl.length > 0) options.filters = filtersFromUrl;
      this.loadChart(options);
    } else {
      this.loadChart();
    }
  }

  ngOnDestroy(): void {
    if (this.filterTimer) {
      window.clearTimeout(this.filterTimer);
    }

    if (this.rawSearchTimer) {
      window.clearTimeout(this.rawSearchTimer);
    }

    if (this.zoomTimer) {
      window.clearTimeout(this.zoomTimer);
    }

    if (this.echartsInstance) {
      this.echartsInstance.off('datazoom');
      this.echartsInstance.off('click');
    }
  }

  loadRecommendations(): void {
    this.loadingRecommendations = true;

    this.datasetApi.getRecommendations(this.datasetId).subscribe({
      next: (response) => {
        this.loadingRecommendations = false;
        if (!response.success || !response.data) {
          return;
        }

        const received = Array.isArray(response.data) ? response.data : [];
        this.recommendations = [...received].sort((left, right) => {
          const byScore = (right.score ?? 0) - (left.score ?? 0);
          if (byScore !== 0) return byScore;

          const byImpact = (right.impactScore ?? 0) - (left.impactScore ?? 0);
          if (byImpact !== 0) return byImpact;

          return left.id.localeCompare(right.id);
        });

        this.currentIndex = this.recommendations.findIndex(r => r.id === this.recommendationId);
        this.currentRecommendation = this.currentIndex >= 0 ? this.recommendations[this.currentIndex] : null;

        if (!this.currentRecommendation && this.recommendations.length > 0) {
          this.currentRecommendation = this.recommendations[0];
          this.currentIndex = 0;
          this.recommendationId = this.currentRecommendation.id;
        }

        if (this.currentRecommendation) {
          const queryParams = this.route.snapshot.queryParamMap;
          const aggFromUrl = queryParams.get('aggregation');
          const timeBinFromUrl = queryParams.get('timeBin');
          const yColumnFromUrl = queryParams.get('yColumn');
          const metricYFromUrl = queryParams.getAll('metricY');
          const groupByFromUrl = queryParams.get('groupBy');

          this.selectedAggregation = aggFromUrl || this.currentRecommendation.aggregation || 'Sum';
          this.selectedTimeBin = timeBinFromUrl || this.currentRecommendation.timeBin || 'Month';
          if (metricYFromUrl.length > 0) {
            this.selectedMetricsY = this.distinctValues(metricYFromUrl);
            this.selectedMetric = this.selectedMetricsY[0] || '';
          } else {
            this.selectedMetric = yColumnFromUrl || this.currentRecommendation.yColumn || '';
            this.selectedMetricsY = this.selectedMetric ? [this.selectedMetric] : [];
          }
          this.selectedGroupBy = groupByFromUrl || '';
          this.selectedVisualizationType = this.ensureAllowedVisualization(this.selectedVisualizationType);
        }

        this.initializeScenarioDefaults();
      },
      error: (err) => {
        this.loadingRecommendations = false;
        console.error('Error loading recommendations for navigation:', err);
      }
    });
  }

  loadProfile(): void {
    this.datasetApi.getProfile(this.datasetId).subscribe({
      next: (response) => {
        if (!response.success || !response.data) {
          return;
        }

        this.profileColumns = response.data.columns || [];
        const maxDistinct = Math.max(20, Math.floor(response.data.sampleSize * 0.05));

        this.availableMetrics = this.profileColumns
          .filter(c => c.inferredType === 'Number')
          .map(c => c.name);

        this.availableGroupBy = this.profileColumns
          .filter(c => c.inferredType !== 'Number' && c.distinctCount <= maxDistinct)
          .map(c => c.name);

        this.syncSelectedMetricsWithAvailability();

        if (this.selectedMetric && !this.availableMetrics.includes(this.selectedMetric)) {
          this.availableMetrics = [this.selectedMetric, ...this.availableMetrics];
        }

        this.initializeScenarioDefaults();
      },
      error: (err) => {
        console.error('Error loading profile:', err);
      }
    });
  }

  loadChart(options?: ChartLoadOptions): void {
    const requestOptions: ChartLoadOptions = options ? { ...options } : {};

    if (this.supportsMetricControl) {
      const primaryMetric = requestOptions.metricY || this.selectedMetric;
      if (primaryMetric) {
        requestOptions.metricY = primaryMetric;
      }
    }

    this.loading = true;
    this.error = null;
    this.pendingDrilldownCategory = null;
    this.aiSummary = null;
    this.aiSummaryMeta = null;
    this.aiSummaryError = null;
    this.explainResult = null;
    this.explainError = null;
    this.explainPanelOpen = false;
    this.askPlan = null;
    this.askError = null;
    this.askReasoningExpanded = false;

    const loadVersion = ++this.chartLoadVersion;
    const startedAt = performance.now();

    this.datasetApi.getChart(this.datasetId, this.recommendationId, requestOptions).subscribe({
      next: (response) => {
        if (loadVersion !== this.chartLoadVersion) {
          return;
        }

        this.loading = false;

        if (response.success && response.data) {
          this.chartMeta = response.data.meta || null;
          this.insightSummary = response.data.insightSummary || null;
          const primaryMetric = requestOptions.metricY || this.selectedMetric;

          this.applyChartOptionWithEnhancements(
            response.data.option,
            primaryMetric,
            requestOptions,
            loadVersion);

          if (!this.chartOption) {
            this.error = {
              code: 'NO_CHART_DATA',
              message: 'Nenhum dado de grafico retornado.'
            };
          }
          this.logDevTiming('chart-load', startedAt, {
            datasetId: this.datasetId,
            recommendationId: this.recommendationId,
            cacheHit: this.chartMeta?.cacheHit || false,
            rowCountReturned: this.chartMeta?.rowCountReturned || 0
          });
          return;
        }

        if (response.errors && response.errors.length > 0) {
          const first = response.errors[0];
          this.error = {
            code: first.code,
            message: first.message,
            target: first.target,
            errors: response.errors,
            traceId: response.traceId
          };
        }

        this.logDevTiming('chart-load-error', startedAt, {
          datasetId: this.datasetId,
          recommendationId: this.recommendationId
        });
      },
      error: (err) => {
        if (loadVersion !== this.chartLoadVersion) {
          return;
        }

        this.loading = false;
        this.error = HttpErrorUtil.extractApiError(err) || {
          code: 'LOAD_ERROR',
          message: HttpErrorUtil.extractErrorMessage(err)
        };
        this.logDevTiming('chart-load-error', startedAt, {
          datasetId: this.datasetId,
          recommendationId: this.recommendationId
        });
      }
    });
  }

  private updateLayout(): void {
    const wasMobile = this.isMobile;
    this.isMobile = window.innerWidth < 1200;

    if (!this.isMobile) {
      this.controlsOpen = true;
      return;
    }

    if (this.isMobile && !wasMobile) {
      this.controlsOpen = false;
    }
  }

  toggleControls(): void {
    this.controlsOpen = !this.controlsOpen;
  }

  refreshChart(): void {
    this.reloadChartWithCurrentParameters();
  }

  generateAiSummary(): void {
    if (this.aiSummaryLoading) {
      return;
    }

    this.aiSummaryLoading = true;
    this.aiSummaryError = null;

    const payload = this.buildAiSummaryPayload();

    this.datasetApi.generateAiSummary(this.datasetId, this.recommendationId, payload).subscribe({
      next: (response) => {
        this.aiSummaryLoading = false;
        if (!response.success || !response.data) {
          this.aiSummaryError = response.errors?.[0]?.message || 'Nao foi possivel gerar o resumo AI.';
          return;
        }

        this.aiSummary = response.data.insightSummary;
        this.aiSummaryMeta = response.data.meta;
      },
      error: (err) => {
        this.aiSummaryLoading = false;
        this.aiSummaryError = HttpErrorUtil.extractErrorMessage(err);
      }
    });
  }

  openExplainChart(): void {
    this.explainPanelOpen = true;
    if (this.explainLoading) {
      return;
    }

    this.explainLoading = true;
    this.explainError = null;
    const payload = this.buildAiSummaryPayload();

    this.datasetApi.explainChart(this.datasetId, this.recommendationId, payload).subscribe({
      next: (response) => {
        this.explainLoading = false;
        if (!response.success || !response.data) {
          this.explainError = response.errors?.[0]?.message || 'Nao foi possivel explicar o grafico.';
          return;
        }

        this.explainResult = response.data;
      },
      error: (err) => {
        this.explainLoading = false;
        this.explainError = HttpErrorUtil.extractErrorMessage(err);
      }
    });
  }

  closeExplainPanel(): void {
    this.explainPanelOpen = false;
  }

  copyExplanationToClipboard(): void {
    if (!this.explainResult) {
      return;
    }

    const payload = this.buildExplanationText(this.explainResult);
    navigator.clipboard.writeText(payload).then(() => {
      this.toast.success('Explicacao copiada.');
    }).catch(() => {
      this.toast.error('Nao foi possivel copiar a explicacao.');
    });
  }

  submitAskQuestion(): void {
    const question = this.askQuestion.trim();
    if (!question) {
      this.toast.info('Digite uma pergunta para gerar um plano.');
      return;
    }

    this.askLoading = true;
    this.askError = null;
    this.askPlan = null;
    this.askReasoningExpanded = false;

    this.datasetApi.askDataset(this.datasetId, question, this.buildCurrentQueryParams()).subscribe({
      next: (response) => {
        this.askLoading = false;
        if (!response.success || !response.data) {
          this.askError = response.errors?.[0]?.message || 'Nao foi possivel analisar a pergunta.';
          return;
        }

        this.askPlan = response.data;
      },
      error: (err) => {
        this.askLoading = false;
        this.askError = HttpErrorUtil.extractErrorMessage(err);
      }
    });
  }

  applyAskPlan(runAfterApply: boolean): void {
    if (!this.askPlan) {
      return;
    }

    const dimensions = this.askPlan.proposedDimensions || {};
    const nextMetric = (dimensions.y || '').trim();
    if (nextMetric) {
      this.selectedMetric = nextMetric;
      this.selectedMetricsY = [nextMetric];
    }

    const nextGroupBy = (dimensions.groupBy || '').trim();
    this.selectedGroupBy = nextGroupBy;

    const chartTypeRaw = (this.askPlan.suggestedChartType || '').trim().toLowerCase();
    if (['line', 'bar', 'scatter', 'histogram'].includes(chartTypeRaw)) {
      const normalized = this.normalizeVisualizationType(chartTypeRaw);
      this.selectedVisualizationType = this.ensureAllowedVisualization(normalized);
    }

    const suggestedFilters = (this.askPlan.suggestedFilters || []).slice(0, 3);
    if (suggestedFilters.length > 0) {
      this.filterRules = suggestedFilters
        .filter(filter => filter.column && filter.values && filter.values.length > 0)
        .map(filter => ({
          column: filter.column,
          operator: filter.operator || 'Eq',
          value: filter.values.join(',')
        }));
    }

    if (runAfterApply) {
      this.reloadChartWithCurrentParameters(true);
      return;
    }

    this.syncUrlWithoutReload();
  }

  private buildAiSummaryPayload(): {
    aggregation?: string;
    timeBin?: string;
    metricY?: string;
    groupBy?: string;
    filters?: string[];
  } {
    const options = this.buildLoadOptionsFromState();
    return {
      aggregation: options.aggregation,
      timeBin: options.timeBin,
      metricY: options.metricY,
      groupBy: options.groupBy,
      filters: options.filters
    };
  }

  private buildExplanationText(explanation: ExplainChartResponse): string {
    return [
      'Explanation',
      ...explanation.explanation,
      '',
      'Key Takeaways',
      ...explanation.keyTakeaways,
      '',
      'Potential Causes',
      ...explanation.potentialCauses,
      '',
      'Caveats',
      ...explanation.caveats,
      '',
      'Suggested Next Steps',
      ...explanation.suggestedNextSteps,
      '',
      'Questions To Ask',
      ...explanation.questionsToAsk
    ].join('\n');
  }

  resetToRecommended(): void {
    if (this.currentRecommendation) {
      this.selectedAggregation = this.currentRecommendation.aggregation || 'Sum';
      this.selectedTimeBin = this.currentRecommendation.timeBin || 'Month';
      this.selectedMetric = this.currentRecommendation.yColumn || '';
      this.selectedMetricsY = this.selectedMetric ? [this.selectedMetric] : [];
    }

    this.selectedGroupBy = '';
    this.selectedVisualizationType = '';
    this.zoomStart = null;
    this.zoomEnd = null;
    this.metricToAdd = '';
    this.filterRules = [];
    this.pendingDrilldownCategory = null;
    this.filterPreviewPending = false;

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {},
      replaceUrl: true
    });

    this.loadChart();
    this.rawPageIndex = 0;
    this.loadRawDataRows(true);
  }

  onChartInit(ec: ECharts): void {
    this.echartsInstance = ec;
    this.echartsInstance.off('datazoom');
    this.echartsInstance.off('click');
    this.echartsInstance.on('datazoom', event => this.onDataZoom(event));
    this.echartsInstance.on('click', event => this.onChartClick(event));
  }

  onSimulationChartInit(ec: ECharts): void {
    this.simulationEchartsInstance = ec;
  }

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
    const keepContext = this.keepNavigationContext;
    const queryParams = keepContext ? this.buildCurrentQueryParams() : {};

    this.router.navigate(['/', this.currentLanguage, 'datasets', this.datasetId, 'charts', recId], {
      queryParams,
      replaceUrl: true
    }).then(() => {
      this.recommendationId = recId;
      this.currentIndex = this.recommendations.findIndex(r => r.id === recId);
      this.currentRecommendation = this.currentIndex >= 0 ? this.recommendations[this.currentIndex] : null;

      let chartOptions: ChartLoadOptions | undefined;
      if (!keepContext && this.currentRecommendation) {
        this.selectedAggregation = this.currentRecommendation.aggregation || 'Sum';
        this.selectedTimeBin = this.currentRecommendation.timeBin || 'Month';
        this.selectedMetric = this.currentRecommendation.yColumn || '';
        this.selectedMetricsY = this.selectedMetric ? [this.selectedMetric] : [];
        this.metricToAdd = '';
        this.selectedGroupBy = '';
        this.filterRules = [];
        this.zoomStart = null;
        this.zoomEnd = null;
        this.selectedVisualizationType = '';
      } else if (keepContext) {
        chartOptions = this.buildLoadOptionsFromState();
      }

      this.selectedVisualizationType = this.ensureAllowedVisualization(this.selectedVisualizationType);
      this.initializeScenarioDefaults();
      this.loadChart(chartOptions);
    });
  }

  getRecommendationIndex(recId: string): number {
    const index = this.recommendations.findIndex(r => r.id === recId);
    return index >= 0 ? index + 1 : 0;
  }

  recommendationDisplay(rec: ChartRecommendation): string {
    const rank = this.getRecommendationIndex(rec.id);
    const score = (rec.score ?? 0).toFixed(2);
    const chartType = rec.chart?.type || 'Line';
    return `${rank}. ${rec.title} (${chartType} | score ${score})`;
  }

  onAggregationChange(): void {
    this.reloadChartWithCurrentParameters();
  }

  onTimeBinChange(): void {
    this.reloadChartWithCurrentParameters();
  }

  onMetricChange(): void {
    if (this.selectedMetric) {
      this.selectedMetricsY = [
        this.selectedMetric,
        ...this.selectedMetricsY.filter(metric => metric !== this.selectedMetric)
      ].slice(0, 4);
    }

    this.reloadChartWithCurrentParameters();
    if (!this.simulationTargetMetric) {
      this.simulationTargetMetric = this.selectedMetric;
    }
  }

  onGroupByChange(): void {
    this.reloadChartWithCurrentParameters();
    if (!this.simulationTargetDimension && this.selectedGroupBy) {
      this.simulationTargetDimension = this.selectedGroupBy;
    }
  }

  onVisualizationTypeChange(): void {
    this.selectedVisualizationType = this.ensureAllowedVisualization(this.selectedVisualizationType);

    if (this.chartOption) {
      const transformed = this.applyPresentationTransforms(this.cloneOption(this.chartOption));
      this.chartOption = transformed;
      this.buildChartDataGrid(transformed);
    }

    this.syncUrlWithoutReload();
  }

  addMetricY(): void {
    if (!this.canAddMetric) {
      return;
    }

    this.selectedMetricsY = [...this.selectedMetricsY, this.metricToAdd];
    this.metricToAdd = '';
    this.reloadChartWithCurrentParameters();
  }

  removeMetricY(metric: string): void {
    if (!this.selectedMetricsY.includes(metric)) {
      return;
    }

    this.selectedMetricsY = this.selectedMetricsY.filter(item => item !== metric);

    if (this.selectedMetric === metric) {
      this.selectedMetric = this.selectedMetricsY[0] || '';
    }

    if (this.selectedMetricsY.length === 0 && this.selectedMetric) {
      this.selectedMetricsY = [this.selectedMetric];
    }

    this.reloadChartWithCurrentParameters();
  }

  applyDrilldown(categoryOverride?: string): void {
    const selectedCategory = categoryOverride ?? this.pendingDrilldownCategory;
    if (!selectedCategory) {
      return;
    }

    const column = this.currentRecommendation?.xColumn || this.readAxisName(this.chartOption, 'xAxis') || '';
    if (!column) {
      this.toast.error('Nao foi possivel identificar a coluna para drilldown.');
      return;
    }

    const existing = this.filterRules.find(rule => rule.column === column && rule.operator === 'Eq');
    if (existing) {
      existing.value = selectedCategory;
    } else if (this.filterRules.length < 3) {
      this.filterRules.push({
        column,
        operator: 'Eq',
        value: selectedCategory
      });
    } else {
      this.filterRules[0] = {
        column,
        operator: 'Eq',
        value: selectedCategory
      };
      this.toast.info('Limite de filtros atingido. O filtro mais antigo foi substituido.');
    }

    this.pendingDrilldownCategory = null;
    this.reloadChartWithCurrentParameters(true);
  }

  clearDrilldownSelection(): void {
    this.pendingDrilldownCategory = null;
  }

  private reloadChartWithCurrentParameters(refreshRawData: boolean = false): void {
    const options = this.buildLoadOptionsFromState();
    this.filterPreviewPending = false;

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: this.buildCurrentQueryParams(),
      replaceUrl: true
    });

    this.loadChart(options);
    if (refreshRawData) {
      this.rawPageIndex = 0;
      this.loadRawDataRows(true);
    }
  }

  private syncUrlWithoutReload(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: this.buildCurrentQueryParams(),
      replaceUrl: true
    });
  }

  private buildLoadOptionsFromState(): ChartLoadOptions {
    const options: ChartLoadOptions = {};

    if (this.supportsAggregationControl && this.selectedAggregation) {
      options.aggregation = this.selectedAggregation;
    }

    if (this.supportsTimeBinControl && this.selectedTimeBin) {
      options.timeBin = this.selectedTimeBin;
    }

    if (this.supportsMetricControl && this.selectedMetric) {
      options.metricY = this.selectedMetric;
    }

    if (this.supportsGroupByControl && this.selectedGroupBy) {
      options.groupBy = this.selectedGroupBy;
    }

    const filters = this.buildFilterParams();
    if (filters.length > 0) {
      options.filters = filters;
    }

    return options;
  }

  private buildCurrentQueryParams(): Record<string, unknown> {
    const options: Record<string, unknown> = {};

    if (this.supportsAggregationControl && this.selectedAggregation) {
      options['aggregation'] = this.selectedAggregation;
    }

    if (this.supportsTimeBinControl && this.selectedTimeBin) {
      options['timeBin'] = this.selectedTimeBin;
    }

    if (this.supportsMetricControl && this.selectedMetricsY.length > 0) {
      options['metricY'] = this.selectedMetricsY;
    } else if (this.supportsMetricControl && this.selectedMetric) {
      options['metricY'] = this.selectedMetric;
    }

    if (this.supportsGroupByControl && this.selectedGroupBy) {
      options['groupBy'] = this.selectedGroupBy;
    }

    const filters = this.buildFilterParams();
    if (filters.length > 0) {
      options['filters'] = filters;
    }

    if (this.selectedVisualizationType && this.selectedVisualizationType !== this.currentBaseChartType) {
      options['chartType'] = this.selectedVisualizationType;
    }

    if (this.zoomStart !== null && this.zoomEnd !== null) {
      options['zoomStart'] = this.zoomStart.toFixed(2);
      options['zoomEnd'] = this.zoomEnd.toFixed(2);
    }

    return options;
  }

  addFilterRule(): void {
    if (this.filterRules.length >= 3) {
      return;
    }

    this.filterRules.push({
      column: '',
      operator: 'Eq',
      value: ''
    });
  }

  removeFilterRule(index: number): void {
    this.filterRules.splice(index, 1);
    this.previewFilters();
  }

  onFilterRuleChange(): void {
    this.scheduleFiltersRefresh();
  }

  applyFilterChanges(): void {
    this.filterPreviewPending = false;
    this.reloadChartWithCurrentParameters(true);
  }

  private scheduleFiltersRefresh(): void {
    if (this.filterTimer) {
      window.clearTimeout(this.filterTimer);
    }

    this.filterTimer = window.setTimeout(() => {
      this.previewFilters();
    }, 400);
  }

  private previewFilters(): void {
    this.filterPreviewPending = true;
    this.loadChart(this.buildLoadOptionsFromState());
    if (this.activeTab === 1) {
      this.rawPageIndex = 0;
      this.loadRawDataRows(true);
    }
  }

  private buildFilterParams(): string[] {
    return this.filterRules
      .filter(rule => rule.column && rule.operator && rule.value)
      .map(rule => `${rule.column}|${rule.operator}|${rule.value}`);
  }

  private parseFiltersFromUrl(filters: string[]): FilterRule[] {
    return filters
      .map(raw => {
        const parts = raw.split('|');
        return {
          column: parts[0] || '',
          operator: parts[1] || 'Eq',
          value: parts.slice(2).join('|') || ''
        };
      })
      .filter(rule => rule.column && rule.value);
  }

  getChartTitle(): string {
    if (this.chartOption && typeof this.chartOption === 'object') {
      const option = this.chartOption as Record<string, unknown>;
      const title = option['title'];
      if (title && typeof title === 'object') {
        const text = (title as Record<string, unknown>)['text'];
        if (typeof text === 'string' && text.trim().length > 0) {
          return text;
        }
      }
    }

    return 'Visualizacao de Grafico';
  }

  getDatasetSubtitle(): string {
    return this.datasetName
      ? `Dataset: ${this.datasetName} (${this.datasetId})`
      : `Dataset: ${this.datasetId}`;
  }

  formatExecutionTime(ms?: number): string {
    if (!ms) return 'N/A';
    return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(2)}s`;
  }

  copyChartLink(): void {
    const url = window.location.href;
    navigator.clipboard.writeText(url).then(() => {
      this.toast.success('Link copiado. Inclui filtros, zoom e parametros ativos.');
    });
  }

  exportChartPNG(): void {
    if (!this.echartsInstance) {
      this.toast.error('Grafico ainda nao foi carregado');
      return;
    }

    try {
      const imageDataUrl = this.echartsInstance.getDataURL({
        type: 'png',
        pixelRatio: 2,
        backgroundColor: '#fff'
      });

      const link = document.createElement('a');
      const fileName = `chart_${this.datasetId}_${this.recommendationId}_${Date.now()}.png`;
      link.download = fileName;
      link.href = imageDataUrl;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);

      this.toast.success('Grafico exportado com sucesso');
    } catch (error) {
      console.error('Error exporting chart:', error);
      this.toast.error('Erro ao exportar grafico');
    }
  }

  initializeScenarioDefaults(): void {
    if (!this.simulationTargetMetric) {
      this.simulationTargetMetric = this.selectedMetric || this.currentRecommendation?.yColumn || this.availableMetrics[0] || '';
    }

    if (!this.simulationTargetDimension) {
      this.simulationTargetDimension = this.selectedGroupBy || this.currentRecommendation?.xColumn || this.availableGroupBy[0] || this.firstNonNumericColumn() || '';
    }

    if (this.simulationOperations.length === 0) {
      this.simulationOperations.push(this.createDefaultSimulationOperation());
    }
  }

  private firstNonNumericColumn(): string {
    const nonNumeric = this.profileColumns.find(c => c.inferredType !== 'Number');
    return nonNumeric?.name || '';
  }

  private createDefaultSimulationOperation(): SimulationOperationRule {
    return {
      type: 'MultiplyMetric',
      column: this.simulationTargetDimension,
      values: '',
      factor: 1.1,
      constant: null,
      min: null,
      max: null
    };
  }

  addSimulationOperation(): void {
    if (this.simulationOperations.length >= 3) {
      this.toast.info('Limite de 3 operacoes por cenario.');
      return;
    }

    this.simulationOperations.push(this.createDefaultSimulationOperation());
  }

  removeSimulationOperation(index: number): void {
    this.simulationOperations.splice(index, 1);
  }

  onSimulationOperationTypeChange(rule: SimulationOperationRule): void {
    rule.values = '';
    rule.factor = null;
    rule.constant = null;
    rule.min = null;
    rule.max = null;
    if (!rule.column) {
      rule.column = this.simulationTargetDimension;
    }

    if (rule.type === 'MultiplyMetric') {
      rule.factor = 1.1;
    }

    if (rule.type === 'AddConstant') {
      rule.constant = 0;
    }
  }

  runSimulation(): void {
    const payload = this.buildSimulationPayload();
    if (!payload) {
      return;
    }

    this.simulationLoading = true;
    this.simulationError = null;
    this.simulationResult = null;
    this.simulationChartOption = null;

    this.datasetApi.simulate(this.datasetId, payload).subscribe({
      next: (response) => {
        this.simulationLoading = false;

        if (response.success && response.data) {
          this.simulationResult = response.data;
          this.simulationChartOption = this.buildSimulationChartOption(response.data);
          this.activeTab = 2;
          return;
        }

        if (response.errors && response.errors.length > 0) {
          this.simulationError = response.errors[0].message;
        }
      },
      error: (err) => {
        this.simulationLoading = false;
        this.simulationError = HttpErrorUtil.extractApiError(err)?.message || HttpErrorUtil.extractErrorMessage(err);
      }
    });
  }

  private buildSimulationPayload(): ScenarioSimulationRequest | null {
    if (!this.simulationTargetMetric || !this.simulationTargetDimension) {
      this.toast.error('Selecione metrica e dimensao para simular.');
      return null;
    }

    if (this.simulationOperations.length === 0) {
      this.toast.error('Adicione ao menos uma operacao.');
      return null;
    }

    const operations: ScenarioOperationRequest[] = [];
    for (const rule of this.simulationOperations) {
      const mapped = this.mapSimulationOperation(rule);
      if (!mapped) {
        return null;
      }
      operations.push(mapped);
    }

    const payload: ScenarioSimulationRequest = {
      targetMetric: this.simulationTargetMetric,
      targetDimension: this.simulationTargetDimension,
      aggregation: this.selectedAggregation,
      operations,
      filters: this.buildSimulationFilters()
    };

    return payload;
  }

  private mapSimulationOperation(rule: SimulationOperationRule): ScenarioOperationRequest | null {
    switch (rule.type) {
      case 'MultiplyMetric':
        if (rule.factor === null || Number.isNaN(rule.factor)) {
          this.toast.error('Multiply Metric requer um factor numerico.');
          return null;
        }
        return { type: rule.type, factor: rule.factor };

      case 'AddConstant':
        if (rule.constant === null || Number.isNaN(rule.constant)) {
          this.toast.error('Add Constant requer um valor numerico.');
          return null;
        }
        return { type: rule.type, constant: rule.constant };

      case 'Clamp':
        if (rule.min === null && rule.max === null) {
          this.toast.error('Clamp requer min, max ou ambos.');
          return null;
        }
        if (rule.min !== null && rule.max !== null && rule.min > rule.max) {
          this.toast.error('Clamp invalido: min nao pode ser maior que max.');
          return null;
        }
        return { type: rule.type, min: rule.min ?? undefined, max: rule.max ?? undefined };

      case 'RemoveCategory':
      case 'FilterOut': {
        const values = this.splitCsvValues(rule.values);
        if (values.length === 0) {
          this.toast.error(`${rule.type} requer pelo menos um valor.`);
          return null;
        }

        const column = rule.column || this.simulationTargetDimension;
        if (!column) {
          this.toast.error(`${rule.type} requer uma coluna alvo.`);
          return null;
        }

        return {
          type: rule.type,
          column,
          values
        };
      }

      default:
        this.toast.error(`Operacao nao suportada: ${rule.type}`);
        return null;
    }
  }

  private buildSimulationFilters(): ScenarioFilterRequest[] {
    return this.filterRules
      .filter(rule => rule.column && rule.operator && rule.value)
      .map(rule => ({
        column: rule.column,
        operator: rule.operator,
        values: this.splitCsvValues(rule.value)
      }))
      .filter(filter => filter.values.length > 0)
      .slice(0, 3);
  }

  private splitCsvValues(raw: string): string[] {
    return raw
      .split(',')
      .map(value => value.trim())
      .filter(value => value.length > 0);
  }

  private buildSimulationChartOption(result: ScenarioSimulationResponse): EChartsOption {
    const dimensions = result.deltaSeries.map(item => item.dimension);
    const baseline = result.deltaSeries.map(item => item.baseline);
    const simulated = result.deltaSeries.map(item => item.simulated);

    const useLine = dimensions.every(dim => /\d{4}[-/]\d{2}[-/]\d{2}/.test(dim) || /^\d{6,8}$/.test(dim));
    const chartType = useLine ? 'line' : 'bar';

    return {
      title: {
        text: 'Baseline vs Simulado',
        subtext: `${result.targetMetric} por ${result.targetDimension}`
      },
      tooltip: {
        trigger: 'axis'
      },
      legend: {
        data: ['Baseline', 'Simulado']
      },
      grid: {
        left: '3%',
        right: '4%',
        bottom: '10%',
        top: '15%',
        containLabel: true
      },
      xAxis: {
        type: 'category',
        data: dimensions,
        axisLabel: {
          rotate: dimensions.length > 12 ? 30 : 0
        }
      },
      yAxis: {
        type: 'value',
        name: result.targetMetric
      },
      series: [
        {
          name: 'Baseline',
          type: chartType,
          data: baseline,
          smooth: chartType === 'line',
          itemStyle: { color: '#64748b' }
        },
        {
          name: 'Simulado',
          type: chartType,
          data: simulated,
          smooth: chartType === 'line',
          itemStyle: { color: '#2563eb' }
        }
      ]
    };
  }

  formatDeltaPercent(value?: number): string {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return 'N/A';
    }

    return `${value >= 0 ? '+' : ''}${value.toFixed(2)}%`;
  }

  formatNumeric(value?: number): string {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '0';
    }

    return value.toLocaleString(undefined, {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2
    });
  }

  simulationOperationDescription(rule: SimulationOperationRule): string {
    switch (rule.type) {
      case 'MultiplyMetric':
        return `x ${rule.factor ?? '?'}`;
      case 'AddConstant':
        return `+ ${rule.constant ?? '?'}`;
      case 'Clamp':
        return `[${rule.min ?? '-inf'}, ${rule.max ?? '+inf'}]`;
      case 'RemoveCategory':
      case 'FilterOut':
        return `${rule.column || this.simulationTargetDimension}: ${rule.values || '-'}`;
      default:
        return '';
    }
  }

  formatGridCell(row: Record<string, unknown>, column: string): string {
    const value = row[column];
    if (value === null || value === undefined) {
      return '';
    }

    if (typeof value === 'number') {
      return value.toLocaleString(undefined, {
        minimumFractionDigits: 0,
        maximumFractionDigits: 4
      });
    }

    return `${value}`;
  }

  openSelectedPointDataModal(): void {
    if (!this.selectedPoint) {
      this.toast.info('Selecione um ponto no grafico para ver os dados correspondentes.');
      return;
    }

    this.selectedPointModalOpen = true;
  }

  closeSelectedPointDataModal(): void {
    this.selectedPointModalOpen = false;
  }

  onRawSearchChange(): void {
    if (this.rawSearchTimer) {
      window.clearTimeout(this.rawSearchTimer);
    }

    this.rawSearchTimer = window.setTimeout(() => {
      this.rawPageIndex = 0;
      this.loadRawDataRows();
    }, 300);
  }

  onRawSortChange(event: Sort): void {
    this.rawSortColumn = event.active || this.rawSortColumn;
    this.rawSortDirection = (event.direction || 'asc') as 'asc' | 'desc';
    this.rawPageIndex = 0;
    this.loadRawDataRows();
  }

  onRawPageChange(event: PageEvent): void {
    this.rawPageIndex = event.pageIndex;
    this.rawPageSize = event.pageSize;
    this.loadRawDataRows();
  }

  private loadDatasetName(): void {
    this.datasetApi.listDatasets().subscribe({
      next: response => {
        if (!response.success || !response.data) {
          return;
        }

        const dataset = (response.data as DataSetSummary[])
          .find(item => item.datasetId.toLowerCase() === this.datasetId.toLowerCase());

        if (dataset?.originalFileName) {
          this.datasetName = dataset.originalFileName;
        }
      },
      error: err => {
        console.error('Error loading dataset list for title:', err);
      }
    });
  }

  private loadRawDataRows(resetPage: boolean = false): void {
    if (resetPage) {
      this.rawPageIndex = 0;
    }

    this.rawDataLoading = true;
    this.rawDataError = null;

    const sort = this.rawSortColumn
      ? [`${this.rawSortColumn}|${this.rawSortDirection}`]
      : [];
    const filters = this.buildFilterParams();
    const search = this.rawDataSearch.trim();

    this.datasetApi.getRawRows(this.datasetId, {
      page: this.rawPageIndex + 1,
      pageSize: this.rawPageSize,
      sort,
      search: search.length > 0 ? search : undefined,
      filters: filters.length > 0 ? filters : undefined
    }).subscribe({
      next: response => {
        this.rawDataLoading = false;

        if (!response.success || !response.data) {
          this.rawDataError = response.errors?.[0]?.message || 'Falha ao carregar dados brutos.';
          return;
        }

        const payload = response.data as RawDatasetRowsResponse;
        this.rawDataColumns = payload.columns || [];
        this.rawDataRows = payload.rows || [];
        this.rawTotalRows = payload.rowCountTotal || 0;
        this.rawTotalPages = payload.totalPages || 0;
        this.rawPageSize = payload.pageSize || this.rawPageSize;
        this.rawPageIndex = Math.max((payload.page || 1) - 1, 0);

        if (!this.rawSortColumn && this.rawDataColumns.length > 0) {
          this.rawSortColumn = this.rawDataColumns[0];
        }

        if (this.rawPageIndex >= this.rawTotalPages && this.rawTotalPages > 0) {
          this.rawPageIndex = this.rawTotalPages - 1;
          this.loadRawDataRows();
          return;
        }

        this.refreshPointDataFromRawRows();
      },
      error: err => {
        this.rawDataLoading = false;
        this.rawDataError = HttpErrorUtil.extractErrorMessage(err);
      }
    });
  }

  private applyChartOptionWithEnhancements(
    baseOption: EChartsOption,
    primaryMetric: string,
    options: ChartLoadOptions,
    loadVersion: number): void {
    const clonedBase = this.cloneOption(baseOption);

    if (this.supportsMultiMetricControl && this.selectedMetricsY.length > 1) {
      const additionalMetrics = this.selectedMetricsY
        .filter(metric => metric !== primaryMetric)
        .slice(0, 3);

      if (additionalMetrics.length > 0) {
        this.loadAdditionalMetrics(additionalMetrics, options, loadVersion).subscribe(results => {
          if (loadVersion !== this.chartLoadVersion) {
            return;
          }

          const merged = this.mergeMetricOptions(clonedBase, primaryMetric, results);
          this.chartOption = this.applyPresentationTransforms(merged);
          this.buildChartDataGrid(this.chartOption);
        });
        return;
      }
    }

    this.chartOption = this.applyPresentationTransforms(clonedBase);
    this.buildChartDataGrid(this.chartOption);
  }

  private loadAdditionalMetrics(
    metrics: string[],
    options: ChartLoadOptions,
    loadVersion: number) {
    const requests = metrics.map(metric => {
      const metricOptions: ChartLoadOptions = {
        ...options,
        metricY: metric
      };

      return this.datasetApi.getChart(this.datasetId, this.recommendationId, metricOptions).pipe(
        map(response => {
          if (loadVersion !== this.chartLoadVersion || !response.success || !response.data) {
            return { metric, option: null } as MetricChartResult;
          }

          return {
            metric,
            option: response.data.option
          } as MetricChartResult;
        }),
        catchError(() => of({ metric, option: null } as MetricChartResult))
      );
    });

    return forkJoin(requests);
  }

  private mergeMetricOptions(
    baseOption: EChartsOption,
    primaryMetric: string,
    additional: MetricChartResult[]): EChartsOption {
    const mergedOption = this.cloneOption(baseOption) as Record<string, unknown>;
    const baseSeries = this.readSeriesList(mergedOption);
    const allSeries: Record<string, unknown>[] = [];
    const metricOrder = this.distinctValues([
      primaryMetric,
      ...additional.map(item => item.metric)
    ]);
    const yAxisByMetric = new Map<string, number>();
    metricOrder.forEach((metric, index) => yAxisByMetric.set(metric, index));

    for (const series of baseSeries) {
      const namedSeries = { ...series };
      const originalName = `${namedSeries['name'] ?? primaryMetric}`;
      namedSeries['name'] = this.selectedMetricsY.length > 1
        ? `${primaryMetric} - ${originalName}`
        : originalName;
      namedSeries['yAxisIndex'] = yAxisByMetric.get(primaryMetric) ?? 0;
      allSeries.push(namedSeries);
    }

    for (const extra of additional) {
      if (!extra.option) {
        continue;
      }

      const optionRecord = extra.option as Record<string, unknown>;
      const extraSeries = this.readSeriesList(optionRecord);
      for (const series of extraSeries) {
        const namedSeries = { ...series };
        const originalName = `${namedSeries['name'] ?? extra.metric}`;
        namedSeries['name'] = `${extra.metric} - ${originalName}`;
        namedSeries['yAxisIndex'] = yAxisByMetric.get(extra.metric) ?? 0;
        allSeries.push(namedSeries);
      }
    }

    mergedOption['series'] = allSeries;
    mergedOption['yAxis'] = this.buildMetricYAxis(mergedOption, metricOrder);
    return mergedOption as EChartsOption;
  }

  private buildMetricYAxis(option: Record<string, unknown>, metrics: string[]): Record<string, unknown>[] {
    const yAxisRaw = option['yAxis'];
    const template = Array.isArray(yAxisRaw)
      ? this.asObject(yAxisRaw[0])
      : this.asObject(yAxisRaw);
    const baseAxis = template || { type: 'value' };

    return metrics.map((metric, index) => {
      const axis = { ...baseAxis };
      const columnPair = Math.floor(index / 2);

      axis['type'] = axis['type'] || 'value';
      axis['name'] = metric;
      axis['position'] = index % 2 === 0 ? 'left' : 'right';
      axis['offset'] = columnPair === 0 ? 0 : columnPair * 56;
      axis['alignTicks'] = true;

      return axis;
    });
  }

  private applyPresentationTransforms(option: EChartsOption): EChartsOption {
    const mutable = option as Record<string, unknown>;

    this.applyChartTypeTransform(mutable);
    this.ensureLegend(mutable);
    this.ensureToolbox(mutable);
    this.ensureZoomWindow(mutable);
    this.ensureGridSpacing(mutable);

    return mutable as EChartsOption;
  }

  private ensureGridSpacing(option: Record<string, unknown>): void {
    const yAxisRaw = option['yAxis'];
    const yAxisCount = Array.isArray(yAxisRaw) ? yAxisRaw.length : (yAxisRaw ? 1 : 0);
    if (yAxisCount <= 1) {
      return;
    }

    const leftColumns = Math.ceil(yAxisCount / 2);
    const rightColumns = Math.floor(yAxisCount / 2);

    const grid = this.asObject(option['grid']) || {};
    grid['containLabel'] = true;
    grid['left'] = `${Math.min(28, 8 + (leftColumns - 1) * 7)}%`;
    grid['right'] = `${Math.min(28, 8 + (rightColumns - 1) * 7)}%`;
    option['grid'] = grid;
  }

  private applyChartTypeTransform(option: Record<string, unknown>): void {
    const targetType = this.currentDisplayChartType;
    const seriesList = this.readSeriesList(option);

    if (targetType !== 'Line' && targetType !== 'Bar') {
      return;
    }

    const mappedType = targetType.toLowerCase();
    for (const series of seriesList) {
      const currentType = `${series['type'] ?? ''}`.toLowerCase();
      if (currentType === 'line' || currentType === 'bar') {
        series['type'] = mappedType;
        if (mappedType === 'line') {
          series['smooth'] = true;
        } else {
          delete series['smooth'];
        }
      }
    }

    const tooltip = this.asObject(option['tooltip']);
    if (tooltip) {
      tooltip['trigger'] = 'axis';
      const axisPointer = this.asObject(tooltip['axisPointer']) || {};
      axisPointer['type'] = mappedType === 'bar' ? 'shadow' : 'cross';
      tooltip['axisPointer'] = axisPointer;
      option['tooltip'] = tooltip;
    }
  }

  private ensureLegend(option: Record<string, unknown>): void {
    const seriesList = this.readSeriesList(option);
    const names = seriesList
      .map(series => `${series['name'] ?? ''}`)
      .filter(name => name.trim().length > 0);

    if (names.length === 0) {
      return;
    }

    const legend = this.asObject(option['legend']) || {};
    legend['show'] = true;
    legend['type'] = names.length > 6 ? 'scroll' : 'plain';
    legend['data'] = this.distinctValues(names);
    option['legend'] = legend;
  }

  private ensureToolbox(option: Record<string, unknown>): void {
    const toolbox = this.asObject(option['toolbox']) || {};
    toolbox['show'] = true;

    const feature = this.asObject(toolbox['feature']) || {};
    feature['saveAsImage'] = this.asObject(feature['saveAsImage']) || { show: true };
    feature['restore'] = this.asObject(feature['restore']) || { show: true };

    if (this.currentDisplayChartType === 'Line' || this.currentDisplayChartType === 'Bar') {
      feature['dataZoom'] = this.asObject(feature['dataZoom']) || {
        show: true,
        yAxisIndex: 'none'
      };
    }

    toolbox['feature'] = feature;
    option['toolbox'] = toolbox;
  }

  private ensureZoomWindow(option: Record<string, unknown>): void {
    const dataZoomItems = this.readDataZoomList(option);
    if (dataZoomItems.length === 0 && (this.currentDisplayChartType === 'Line' || this.currentDisplayChartType === 'Bar')) {
      dataZoomItems.push(
        {
          type: 'slider',
          show: true,
          xAxisIndex: 0,
          start: 0,
          end: 100
        },
        {
          type: 'inside',
          xAxisIndex: 0,
          start: 0,
          end: 100
        }
      );
    }

    if (this.zoomStart !== null && this.zoomEnd !== null) {
      for (const zoomItem of dataZoomItems) {
        zoomItem['start'] = this.zoomStart;
        zoomItem['end'] = this.zoomEnd;
      }
    }

    if (dataZoomItems.length > 0) {
      option['dataZoom'] = dataZoomItems;
    }
  }

  private onDataZoom(event: unknown): void {
    const payload = event as Record<string, unknown>;

    let start: number | null = this.tryParseNumeric(payload['start']);
    let end: number | null = this.tryParseNumeric(payload['end']);

    if ((start === null || end === null) && Array.isArray(payload['batch']) && payload['batch'].length > 0) {
      const first = payload['batch'][0] as Record<string, unknown>;
      start = this.tryParseNumeric(first['start']);
      end = this.tryParseNumeric(first['end']);
    }

    if (start === null || end === null) {
      return;
    }

    this.zoomStart = Math.max(0, Math.min(100, start));
    this.zoomEnd = Math.max(0, Math.min(100, end));

    if (this.zoomTimer) {
      window.clearTimeout(this.zoomTimer);
    }

    this.zoomTimer = window.setTimeout(() => {
      this.syncUrlWithoutReload();
    }, 250);
  }

  private onChartClick(event: unknown): void {
    const payload = event as Record<string, unknown>;
    const componentType = `${payload['componentType'] ?? ''}`;
    if (componentType !== 'series') {
      return;
    }

    const selectedPoint = this.extractPointFromChartEvent(payload);
    if (!selectedPoint) {
      return;
    }

    this.selectedPoint = selectedPoint;
    this.selectedPointModalOpen = false;
    this.refreshPointDataFromRawRows();

    if (this.supportsDrilldown) {
      const category = `${selectedPoint['x'] ?? ''}`.trim();
      if (category) {
        this.pendingDrilldownCategory = category;
        this.applyDrilldown(category);
        return;
      }
    }

    this.toast.info('Ponto selecionado. Use "Dados do ponto" para inspecionar os registros.');
  }

  private extractPointFromChartEvent(payload: Record<string, unknown>): ChartTableRow | null {
    const seriesName = `${payload['seriesName'] ?? ''}`.trim() || 'Serie';
    const pointName = `${payload['name'] ?? ''}`.trim();
    const rawValue = payload['value'];

    if (Array.isArray(rawValue)) {
      if (rawValue.length >= 2) {
        return {
          series: seriesName,
          x: this.toScalar(rawValue[0]),
          y: this.toScalar(rawValue[1])
        };
      }

      if (rawValue.length === 1) {
        return {
          series: seriesName,
          x: pointName || null,
          y: this.toScalar(rawValue[0])
        };
      }
    }

    return {
      series: seriesName,
      x: pointName || null,
      y: this.toScalar(rawValue)
    };
  }

  private refreshPointDataFromRawRows(): void {
    if (!this.selectedPoint) {
      return;
    }

    const xColumn = this.currentRecommendation?.xColumn || this.readAxisName(this.chartOption, 'xAxis');
    if (!xColumn || this.rawDataRows.length === 0) {
      return;
    }

    const target = `${this.selectedPoint['x'] ?? ''}`.trim();
    if (!target) {
      return;
    }

    const matchByExact = this.rawDataRows.filter(row => `${row[xColumn] ?? ''}`.trim() === target);
    const matchByContains = matchByExact.length > 0
      ? matchByExact
      : this.rawDataRows.filter(row => `${row[xColumn] ?? ''}`.toLowerCase().includes(target.toLowerCase()));

    const limitedRows = matchByContains.slice(0, 200);
    this.pointDataColumns = this.rawDataColumns.length > 0 ? this.rawDataColumns : this.pointDataColumns;
    this.pointDataRows = limitedRows.map(row => {
      const mapped: ChartTableRow = {};
      for (const column of this.pointDataColumns) {
        mapped[column] = row[column] ?? null;
      }

      return mapped;
    });
  }

  private buildChartDataGrid(option: EChartsOption | null): void {
    if (!option) {
      this.pointDataColumns = [];
      this.pointDataRows = [];
      return;
    }

    const optionRecord = option as Record<string, unknown>;
    const seriesList = this.readSeriesList(optionRecord);
    const xAxisData = this.readAxisData(optionRecord, 'xAxis');

    const rows: ChartTableRow[] = [];
    for (const series of seriesList) {
      const seriesName = `${series['name'] ?? 'Serie'}`;
      const seriesData = Array.isArray(series['data']) ? series['data'] : [];

      for (let index = 0; index < seriesData.length; index++) {
        const item = seriesData[index];
        const resolved = this.resolveDataPoint(item, xAxisData, index);

        rows.push({
          series: seriesName,
          x: resolved.x,
          y: resolved.y
        });
      }
    }

    this.pointDataColumns = ['series', 'x', 'y'];
    this.pointDataRows = rows;
    this.refreshPointDataFromRawRows();
  }

  private resolveDataPoint(
    item: unknown,
    xAxisData: unknown[],
    index: number): { x: string | number | null; y: string | number | null } {
    if (Array.isArray(item)) {
      if (item.length >= 2) {
        return {
          x: this.toScalar(item[0]),
          y: this.toScalar(item[1])
        };
      }

      if (item.length === 1) {
        return {
          x: this.toScalar(xAxisData[index]),
          y: this.toScalar(item[0])
        };
      }
    }

    if (item && typeof item === 'object') {
      const asRecord = item as Record<string, unknown>;
      const value = asRecord['value'];
      if (Array.isArray(value) && value.length >= 2) {
        return {
          x: this.toScalar(value[0]),
          y: this.toScalar(value[1])
        };
      }

      return {
        x: this.toScalar(xAxisData[index]),
        y: this.toScalar(value)
      };
    }

    return {
      x: this.toScalar(xAxisData[index]),
      y: this.toScalar(item)
    };
  }

  private toScalar(value: unknown): string | number | null {
    if (value === null || value === undefined) {
      return null;
    }

    if (typeof value === 'number' || typeof value === 'string') {
      return value;
    }

    if (value instanceof Date) {
      return value.toISOString();
    }

    return `${value}`;
  }

  private readAxisData(option: Record<string, unknown>, axisKey: 'xAxis' | 'yAxis'): unknown[] {
    const axisRaw = option[axisKey];
    if (Array.isArray(axisRaw) && axisRaw.length > 0) {
      const firstAxis = this.asObject(axisRaw[0]);
      const data = firstAxis?.['data'];
      return Array.isArray(data) ? data : [];
    }

    const axis = this.asObject(axisRaw);
    const data = axis?.['data'];
    return Array.isArray(data) ? data : [];
  }

  private readAxisName(option: EChartsOption | null, axisKey: 'xAxis' | 'yAxis'): string | null {
    if (!option) {
      return null;
    }

    const record = option as Record<string, unknown>;
    const axisRaw = record[axisKey];

    if (Array.isArray(axisRaw) && axisRaw.length > 0) {
      const first = this.asObject(axisRaw[0]);
      const name = first?.['name'];
      return typeof name === 'string' ? name : null;
    }

    const axis = this.asObject(axisRaw);
    const name = axis?.['name'];
    return typeof name === 'string' ? name : null;
  }

  private readSeriesList(option: Record<string, unknown>): Record<string, unknown>[] {
    const raw = option['series'];
    if (Array.isArray(raw)) {
      return raw
        .filter(item => item && typeof item === 'object')
        .map(item => item as Record<string, unknown>);
    }

    if (raw && typeof raw === 'object') {
      return [raw as Record<string, unknown>];
    }

    return [];
  }

  private readDataZoomList(option: Record<string, unknown>): Record<string, unknown>[] {
    const raw = option['dataZoom'];
    if (Array.isArray(raw)) {
      return raw
        .filter(item => item && typeof item === 'object')
        .map(item => ({ ...(item as Record<string, unknown>) }));
    }

    if (raw && typeof raw === 'object') {
      return [{ ...(raw as Record<string, unknown>) }];
    }

    return [];
  }

  private asObject(value: unknown): Record<string, unknown> | null {
    if (value && typeof value === 'object' && !Array.isArray(value)) {
      return { ...(value as Record<string, unknown>) };
    }

    return null;
  }

  private tryParsePercentage(raw: string | null): number | null {
    if (!raw) {
      return null;
    }

    const parsed = Number(raw);
    if (Number.isNaN(parsed)) {
      return null;
    }

    if (parsed < 0 || parsed > 100) {
      return null;
    }

    return parsed;
  }

  private tryParseNumeric(value: unknown): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    if (typeof value === 'string') {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : null;
    }

    return null;
  }

  private normalizeVisualizationType(raw: string): VisualizationType {
    const normalized = raw.trim().toLowerCase();

    switch (normalized) {
      case 'line':
        return 'Line';
      case 'bar':
        return 'Bar';
      case 'scatter':
        return 'Scatter';
      case 'histogram':
        return 'Histogram';
      default:
        return 'Line';
    }
  }

  private ensureAllowedVisualization(value: VisualizationType | ''): VisualizationType | '' {
    if (!value) {
      return '';
    }

    return this.availableVisualizationTypes.includes(value) ? value : '';
  }

  private syncSelectedMetricsWithAvailability(): void {
    if (this.selectedMetricsY.length === 0 && this.selectedMetric) {
      this.selectedMetricsY = [this.selectedMetric];
    }

    const allowed = new Set(this.availableMetrics);
    const preserved = this.selectedMetricsY.filter(metric => allowed.has(metric));

    if (preserved.length > 0) {
      this.selectedMetricsY = preserved;
      this.selectedMetric = preserved[0];
      return;
    }

    if (this.selectedMetric && allowed.has(this.selectedMetric)) {
      this.selectedMetricsY = [this.selectedMetric];
      return;
    }

    if (!this.selectedMetric && this.availableMetrics.length > 0) {
      this.selectedMetric = this.availableMetrics[0];
      this.selectedMetricsY = [this.selectedMetric];
    }
  }

  private distinctValues(values: string[]): string[] {
    const seen = new Set<string>();
    const result: string[] = [];

    for (const value of values) {
      const trimmed = value.trim();
      if (!trimmed || seen.has(trimmed)) {
        continue;
      }

      seen.add(trimmed);
      result.push(trimmed);
    }

    return result;
  }

  private cloneOption(option: EChartsOption): EChartsOption {
    const safeOption = option as Record<string, unknown>;
    return JSON.parse(JSON.stringify(safeOption)) as EChartsOption;
  }

  private logDevTiming(event: string, startedAt: number, extra?: Record<string, unknown>): void {
    if (environment.production) {
      return;
    }

    console.info(`[timing] ${event}`, {
      durationMs: Math.round(performance.now() - startedAt),
      ...extra
    });
  }
}
