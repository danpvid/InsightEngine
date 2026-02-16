import { Component, HostListener, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgxEchartsModule } from 'ngx-echarts';
import { ECharts, EChartsOption } from 'echarts';
import { DatasetApiService } from '../../../../core/services/dataset-api.service';
import { ToastService } from '../../../../core/services/toast.service';
import { HttpErrorUtil } from '../../../../core/util/http-error.util';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { LoadingBarComponent } from '../../../../shared/components/loading-bar/loading-bar.component';
import { ErrorPanelComponent } from '../../../../shared/components/error-panel/error-panel.component';
import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import {
  ChartMeta,
  InsightSummary,
  ScenarioFilterRequest,
  ScenarioOperationRequest,
  ScenarioOperationType,
  ScenarioSimulationRequest,
  ScenarioSimulationResponse
} from '../../../../core/models/chart.model';
import { ChartRecommendation } from '../../../../core/models/recommendation.model';
import { ApiError } from '../../../../core/models/api-response.model';
import { DatasetColumnProfile } from '../../../../core/models/dataset.model';

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
  insightSummary: InsightSummary | null = null;
  loading: boolean = false;
  error: ApiError | null = null;

  private echartsInstance?: ECharts;
  private simulationEchartsInstance?: ECharts;
  private filterTimer?: number;

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
  selectedGroupBy: string = '';

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
    return (this.chartMeta?.executionMs || 0) < 100;
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

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private datasetApi: DatasetApiService,
    private toast: ToastService
  ) {}

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

    this.loadProfile();
    this.loadRecommendations();

    const queryParams = this.route.snapshot.queryParamMap;
    const aggFromUrl = queryParams.get('aggregation');
    const timeBinFromUrl = queryParams.get('timeBin');
    const yColumnFromUrl = queryParams.get('yColumn');
    const metricYFromUrl = queryParams.get('metricY');
    const groupByFromUrl = queryParams.get('groupBy');
    const filtersFromUrl = queryParams.getAll('filters');

    if (groupByFromUrl) {
      this.selectedGroupBy = groupByFromUrl;
    }

    if (filtersFromUrl.length > 0) {
      this.filterRules = this.parseFiltersFromUrl(filtersFromUrl);
    }

    if (aggFromUrl || timeBinFromUrl || yColumnFromUrl || metricYFromUrl || groupByFromUrl || filtersFromUrl.length > 0) {
      const options: any = {};
      if (aggFromUrl) options.aggregation = aggFromUrl;
      if (timeBinFromUrl) options.timeBin = timeBinFromUrl;
      if (metricYFromUrl) options.metricY = metricYFromUrl;
      if (yColumnFromUrl) options.yColumn = yColumnFromUrl;
      if (groupByFromUrl) options.groupBy = groupByFromUrl;
      if (filtersFromUrl.length > 0) options.filters = filtersFromUrl;
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
          const metricYFromUrl = queryParams.get('metricY');
          const groupByFromUrl = queryParams.get('groupBy');

          this.selectedAggregation = aggFromUrl || this.currentRecommendation.aggregation || 'Sum';
          this.selectedTimeBin = timeBinFromUrl || this.currentRecommendation.timeBin || 'Month';
          this.selectedMetric = metricYFromUrl || yColumnFromUrl || this.currentRecommendation.yColumn || '';
          this.selectedGroupBy = groupByFromUrl || '';
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

  loadChart(options?: {
    aggregation?: string;
    timeBin?: string;
    metricY?: string;
    yColumn?: string;
    groupBy?: string;
    filters?: string[];
  }): void {
    this.loading = true;
    this.error = null;
    this.chartOption = null;
    this.insightSummary = null;

    this.datasetApi.getChart(this.datasetId, this.recommendationId, options).subscribe({
      next: (response) => {
        this.loading = false;

        if (response.success && response.data) {
          this.chartOption = { ...response.data.option };
          this.chartMeta = response.data.meta || null;
          this.insightSummary = response.data.insightSummary || null;

          if (!this.chartOption) {
            this.error = {
              code: 'NO_CHART_DATA',
              message: 'Nenhum dado de grafico retornado.'
            };
          }
          return;
        }

        if (response.error) {
          this.error = response.error;
        }
      },
      error: (err) => {
        this.loading = false;
        const apiError = HttpErrorUtil.extractApiError(err);
        this.error = apiError || {
          code: 'LOAD_ERROR',
          message: HttpErrorUtil.extractErrorMessage(err)
        };
        this.toast.error('Erro ao carregar grafico');
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

  resetToRecommended(): void {
    if (this.currentRecommendation) {
      this.selectedAggregation = this.currentRecommendation.aggregation || 'Sum';
      this.selectedTimeBin = this.currentRecommendation.timeBin || 'Month';
      this.selectedMetric = this.currentRecommendation.yColumn || '';
    }

    this.selectedGroupBy = '';
    this.filterRules = [];

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {},
      replaceUrl: true
    });

    this.loadChart();
  }

  onChartInit(ec: ECharts): void {
    this.echartsInstance = ec;
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

    this.router.navigate(['/datasets', this.datasetId, 'charts', recId], {
      queryParams,
      replaceUrl: true
    }).then(() => {
      this.recommendationId = recId;
      this.currentIndex = this.recommendations.findIndex(r => r.id === recId);
      this.currentRecommendation = this.currentIndex >= 0 ? this.recommendations[this.currentIndex] : null;

      let chartOptions: any = undefined;
      if (!keepContext && this.currentRecommendation) {
        this.selectedAggregation = this.currentRecommendation.aggregation || 'Sum';
        this.selectedTimeBin = this.currentRecommendation.timeBin || 'Month';
        this.selectedMetric = this.currentRecommendation.yColumn || '';
        this.selectedGroupBy = '';
        this.filterRules = [];
      } else if (keepContext) {
        chartOptions = this.buildCurrentQueryParams();
      }

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

  private reloadChartWithCurrentParameters(): void {
    const options = this.buildCurrentQueryParams();

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: options,
      replaceUrl: true
    });

    this.loadChart(options);
  }

  private buildCurrentQueryParams(): any {
    const options: any = {
      aggregation: this.selectedAggregation,
      timeBin: this.selectedTimeBin
    };

    if (this.selectedMetric) {
      options.metricY = this.selectedMetric;
    }

    if (this.selectedGroupBy) {
      options.groupBy = this.selectedGroupBy;
    }

    const filters = this.buildFilterParams();
    if (filters.length > 0) {
      options.filters = filters;
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
    this.reloadChartWithCurrentParameters();
  }

  onFilterRuleChange(): void {
    this.scheduleFiltersRefresh();
  }

  private scheduleFiltersRefresh(): void {
    if (this.filterTimer) {
      window.clearTimeout(this.filterTimer);
    }

    this.filterTimer = window.setTimeout(() => {
      this.reloadChartWithCurrentParameters();
    }, 400);
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
      const option = this.chartOption as any;
      if (option.title && option.title.text) {
        return option.title.text;
      }
    }

    return 'Visualizacao de Grafico';
  }

  formatExecutionTime(ms?: number): string {
    if (!ms) return 'N/A';
    return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(2)}s`;
  }

  copyChartLink(): void {
    const url = window.location.href;
    navigator.clipboard.writeText(url).then(() => {
      this.toast.success('Link copiado. Inclui filtros e parametros ativos.');
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
          this.activeTab = 1;
          return;
        }

        if (response.error) {
          this.simulationError = response.error.message;
          this.toast.error(response.error.message);
        }
      },
      error: (err) => {
        this.simulationLoading = false;
        const apiError = HttpErrorUtil.extractApiError(err);
        this.simulationError = apiError?.message || HttpErrorUtil.extractErrorMessage(err);
        this.toast.error(this.simulationError || 'Erro ao executar simulacao');
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
}
