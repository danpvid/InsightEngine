import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
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
  ChartPercentilesMeta,
  ChartViewMeta,
  DeepInsightsResponse,
  DeepInsightsRequest,
  EvidenceFact,
  ExplainChartResponse,
  InsightSummary,
  PercentileKind,
  ScenarioDeltaPoint,
  ScenarioFilterRequest,
  ScenarioOperationRequest,
  ScenarioOperationType,
  ScenarioSimulationRequest,
  ScenarioSimulationResponse
} from '../../../../core/models/chart.model';
import { ChartRecommendation } from '../../../../core/models/recommendation.model';
import { ApiError } from '../../../../core/models/api-response.model';
import {
  DataSetSummary,
  DatasetColumnProfile,
  RawDatasetRow,
  RawDatasetRowsResponse,
  RawDistinctValueStat,
  RawFieldStats,
  RawRangeValueStat
} from '../../../../core/models/dataset.model';
import { environment } from '../../../../../environments/environment';

interface FilterRule {
  column: string;
  operator: string;
  value: string;
  logicalOperator: 'And' | 'Or';
}

interface DrilldownTrailItem {
  label: string;
  filtersState: FilterRule[];
  activeFilter: FilterRule;
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
  xColumn?: string;
  yColumn?: string;
  groupBy?: string;
  filters?: string[];
  view?: 'base' | 'percentile';
  percentile?: PercentileKind;
  mode?: 'bucket' | 'overall';
  percentileTarget?: 'y';
}

interface MetricChartResult {
  metric: string;
  option: EChartsOption | null;
}

interface ChartTableRow {
  [key: string]: string | number | null;
}

interface RecommendedChartState {
  aggregation: string;
  timeBin: string;
  metric: string;
  xColumn: string;
}

interface RawFieldMetric {
  name: string;
  inferredType: string;
  distinctCountProfile: number;
  distinctCountPage: number;
  nullCountPage: number;
  nullRatePage: number;
  topValuesProfile: RawDistinctValueStat[];
}

@Component({
  selector: 'app-chart-viewer-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
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
  readonly rawTopOthersToken: string = '__RAW_TOP_OTHERS__';
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
  deepInsights: DeepInsightsResponse | null = null;
  deepInsightsLoading: boolean = false;
  deepInsightsError: string | null = null;
  deepInsightsHorizon: number = 30;
  deepInsightsSensitiveMode: boolean = false;
  deepInsightsUseScenario: boolean = false;
  deepInsightsShowEvidence: boolean = false;
  llmUseScenarioContext: boolean = false;
  askQuestion: string = '';
  askLoading: boolean = false;
  askError: string | null = null;
  askPlan: AskAnalysisPlanResponse | null = null;
  askReasoningExpanded: boolean = false;
  loading: boolean = false;
  chartRefreshing: boolean = false;
  error: ApiError | null = null;

  private echartsInstance?: ECharts;
  private simulationEchartsInstance?: ECharts;
  private lastSeriesDrilldownAt: number = 0;
  private pendingDrilldownRollback: {
    filters: FilterRule[];
    trail: DrilldownTrailItem[];
    baseFilters: FilterRule[] | null;
  } | null = null;
  private filterTimer?: number;
  private rawSearchTimer?: number;
  private zoomTimer?: number;
  private chartLoadVersion: number = 0;
  private rawDataLoadVersion: number = 0;
  private baseChartOptionSnapshot: EChartsOption | null = null;

  recommendations: ChartRecommendation[] = [];
  currentRecommendation: ChartRecommendation | null = null;
  currentIndex: number = -1;
  loadingRecommendations: boolean = false;
  recommendedState: RecommendedChartState | null = null;

  navigationSearch: string = '';
  keepNavigationContext: boolean = true;

  availableAggregations: string[] = ['Sum', 'Avg', 'Count', 'Min', 'Max'];
  availableTimeBins: string[] = ['Day', 'Week', 'Month', 'Quarter', 'Year'];
  availableMetrics: string[] = [];
  availableGroupBy: string[] = [];
  profileColumns: DatasetColumnProfile[] = [];
  readonly maxGroupByDistinct: number = 50;

  filterOperators = [
    { value: 'Eq', label: '=' },
    { value: 'NotEq', label: '!=' },
    { value: 'Gt', label: '>' },
    { value: 'Gte', label: '>=' },
    { value: 'Lt', label: '<' },
    { value: 'Lte', label: '<=' },
    { value: 'In', label: 'in' },
    { value: 'Between', label: 'between' },
    { value: 'Contains', label: 'contains' }
  ];
  readonly filterLogicalOperators: Array<{ value: 'And' | 'Or'; label: string }> = [
    { value: 'And', label: 'AND' },
    { value: 'Or', label: 'OR' }
  ];
  filterRules: FilterRule[] = [];
  drilldownTrail: DrilldownTrailItem[] = [];
  private drilldownBaseFilters: FilterRule[] | null = null;

  selectedAggregation: string = 'Sum';
  selectedTimeBin: string = 'Month';
  selectedMetric: string = '';
  selectedMetricsY: string[] = [];
  metricToAdd: string = '';
  selectedXAxis: string = '';
  selectedGroupBy: string = '';
  selectedVisualizationType: VisualizationType | '' = '';
  zoomStart: number | null = null;
  zoomEnd: number | null = null;
  pendingDrilldownCategory: string | null = null;
  drilldownLoading: boolean = false;
  percentileView: 'base' | 'percentile' = 'base';
  selectedPercentile: PercentileKind | null = null;
  selectedPercentileMode: 'bucket' | 'overall' | '' = '';
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
  rawColumnWidthMode: 'compact' | 'balanced' | 'expanded' = 'balanced';
  rawFieldMetrics: RawFieldMetric[] = [];
  rawFieldStats: RawFieldStats | null = null;
  rawTopValueStats: RawFieldStats | null = null;
  rawTopValueStatsTotalRows: number = 0;
  rawFacetDistinctCounts: Record<string, number> = {};
  rawFacetDistinctCountsKey: string = '';
  rawFieldSearch: string = '';
  selectedRawFieldName: string = '';

  filterPreviewPending: boolean = false;

  controlsOpen: boolean = true;
  isMobile: boolean = false;
  showRawQuickFiltersAside: boolean = false;

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

  get percentilesMeta(): ChartPercentilesMeta | null {
    return this.chartMeta?.percentiles || null;
  }

  get chartViewMeta(): ChartViewMeta | null {
    return this.chartMeta?.view || null;
  }

  get canUsePercentiles(): boolean {
    const meta = this.percentilesMeta;
    return !!meta?.supported && meta.mode !== 'NotApplicable';
  }

  get percentileKinds(): PercentileKind[] {
    const available = this.percentilesMeta?.available;
    if (available && available.length > 0) {
      return available;
    }

    return ['P5', 'P10', 'P90', 'P95'];
  }

  get activePercentile(): PercentileKind | null {
    return this.chartViewMeta?.kind === 'Percentile'
      ? (this.chartViewMeta.percentileKind || this.selectedPercentile)
      : null;
  }

  get isPercentileView(): boolean {
    return this.chartViewMeta?.kind === 'Percentile';
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
    return this.currentBaseChartType === 'Line' || this.currentBaseChartType === 'Bar' || this.currentBaseChartType === 'Scatter';
  }

  get supportsScatterXAxisControl(): boolean {
    return this.currentBaseChartType === 'Scatter';
  }

  get supportsMetricCheckboxControl(): boolean {
    return this.currentBaseChartType === 'Scatter';
  }

  get supportsMetricBuilderControl(): boolean {
    return this.currentBaseChartType === 'Line' || this.currentBaseChartType === 'Bar';
  }

  get supportsGroupByControl(): boolean {
    return this.currentBaseChartType === 'Line' || this.currentBaseChartType === 'Bar';
  }

  get supportsDrilldown(): boolean {
    return this.currentDisplayChartType === 'Bar' || this.currentDisplayChartType === 'Histogram';
  }

  get visibleDrilldownTrail(): DrilldownTrailItem[] {
    if (!this.isDrilldownTrailInSync()) {
      return [];
    }

    return this.drilldownTrail;
  }

  get canZoomOutDrilldown(): boolean {
    return this.visibleDrilldownTrail.length > 0;
  }

  get activeDrilldownFilterLabel(): string | null {
    const trail = this.visibleDrilldownTrail;
    if (trail.length === 0) {
      return null;
    }

    const activeFilter = trail[trail.length - 1].activeFilter;
    if (!activeFilter) {
      return null;
    }

    return `${activeFilter.column} ${this.formatFilterOperator(activeFilter.operator)} ${activeFilter.value}`;
  }

  get canAddMetric(): boolean {
    return !!this.metricToAdd &&
      this.supportsMetricBuilderControl &&
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

  get deepInsightsEvidenceFacts(): EvidenceFact[] {
    return this.deepInsights?.evidencePack?.facts || [];
  }

  get filteredRawFieldMetrics(): RawFieldMetric[] {
    const term = this.rawFieldSearch.trim().toLowerCase();
    if (!term) {
      return this.rawFieldMetrics;
    }

    return this.rawFieldMetrics.filter(field => {
      const candidate = `${field.name} ${field.inferredType}`.toLowerCase();
      return candidate.includes(term);
    });
  }

  get selectedRawFieldMetric(): RawFieldMetric | null {
    if (!this.selectedRawFieldName) {
      return this.rawFieldMetrics[0] || null;
    }

    return this.rawFieldMetrics.find(field => field.name === this.selectedRawFieldName) || this.rawFieldMetrics[0] || null;
  }

  get selectedRawTopValues(): RawDistinctValueStat[] {
    const selectedField = this.selectedRawFieldMetric;
    if (!selectedField) {
      return [];
    }

    const column = selectedField.name;
    const scopedStats =
      this.rawTopValueStats && this.rawTopValueStats.column === column
        ? this.rawTopValueStats
        : (this.rawFieldStats && this.rawFieldStats.column === column ? this.rawFieldStats : null);
    const scopedTotalRows =
      this.rawTopValueStats && this.rawTopValueStats.column === column
        ? this.rawTopValueStatsTotalRows
        : this.rawTotalRows;

    const profileTopValues = selectedField.topValuesProfile || [];
    const statsTopValues = scopedStats?.topValues || [];
    const baseTopValues = statsTopValues.length > 0 ? statsTopValues : profileTopValues;
    const topValues = this.withRawTopValueComplements(baseTopValues, scopedTotalRows);
    const activeRules = this.getRawTopValueRules(column);
    if (activeRules.length === 0) {
      return topValues;
    }

    const selectedValues = new Set(
      activeRules.flatMap(rule =>
        rule.operator === 'Eq'
          ? [rule.value]
          : this.splitCsvValues(rule.value)));

    if (selectedValues.size === 0) {
      return topValues;
    }

    const knownValues = new Set(topValues.map(item => item.value));
    const missingSelected = Array.from(selectedValues.values())
      .filter(value => !knownValues.has(value))
      .map(value => ({ value, count: 0 }));

    return [...missingSelected, ...topValues];
  }

  get selectedRawTopRanges(): RawRangeValueStat[] {
    const selectedField = this.selectedRawFieldMetric;
    if (!selectedField) {
      return [];
    }

    const column = selectedField.name;
    const scopedStats =
      this.rawTopValueStats && this.rawTopValueStats.column === column
        ? this.rawTopValueStats
        : (this.rawFieldStats && this.rawFieldStats.column === column ? this.rawFieldStats : null);

    const topRanges = scopedStats?.topRanges || [];
    const activeRangeRules = this.filterRules.filter(rule =>
      rule.column === column &&
      rule.operator === 'Between' &&
      !!rule.value);

    if (activeRangeRules.length === 0) {
      return topRanges;
    }

    const selectedPairs = new Set(
      activeRangeRules.flatMap(rule => this.parseRangeFilterPairs(rule.value)));

    if (selectedPairs.size === 0) {
      return topRanges;
    }

    const knownPairs = new Set(topRanges.map(range => `${range.from},${range.to}`));
    const missingSelected = Array.from(selectedPairs.values())
      .filter(pair => !knownPairs.has(pair))
      .map(pair => {
        const [from, to] = pair.split(',', 2);
        const safeFrom = `${from ?? ''}`.trim();
        const safeTo = `${to ?? ''}`.trim();
        return {
          label: `${safeFrom} - ${safeTo}`,
          from: safeFrom,
          to: safeTo,
          count: 0
        } as RawRangeValueStat;
      });

    if (missingSelected.length === 0) {
      return topRanges;
    }

    return [...missingSelected, ...topRanges];
  }

  get topSimulationDeltas(): ScenarioDeltaPoint[] {
    if (!this.simulationResult?.deltaSeries?.length) {
      return [];
    }

    return [...this.simulationResult.deltaSeries]
      .sort((left, right) =>
        Math.abs((right.deltaPercent ?? right.delta) || 0) - Math.abs((left.deltaPercent ?? left.delta) || 0))
      .slice(0, 5);
  }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private datasetApi: DatasetApiService,
    private toast: ToastService,
    private languageService: LanguageService
  ) {}

  get currentLanguage(): string {
    const firstSegment = this.router.url
      .split('?')[0]
      .split('#')[0]
      .split('/')
      .filter(segment => segment.length > 0)[0];

    if (this.languageService.isSupportedLanguage(firstSegment)) {
      return firstSegment;
    }

    return this.languageService.currentLanguage;
  }

  get newDatasetLink(): string[] {
    return ['/', this.currentLanguage, 'datasets', 'new'];
  }

  get recommendationsLink(): string[] {
    return ['/', this.currentLanguage, 'datasets', this.datasetId, 'recommendations'];
  }

  get newDatasetHref(): string {
    return `/${this.currentLanguage}/datasets/new`;
  }

  get recommendationsHref(): string {
    return `/${this.currentLanguage}/datasets/${this.datasetId}/recommendations`;
  }

  setRawColumnWidthMode(mode: 'compact' | 'balanced' | 'expanded'): void {
    this.rawColumnWidthMode = mode;
  }

  isRawColumnWidthMode(mode: 'compact' | 'balanced' | 'expanded'): boolean {
    return this.rawColumnWidthMode === mode;
  }

  get rawColumnMaxWidthPx(): number {
    switch (this.rawColumnWidthMode) {
      case 'compact':
        return 120;
      case 'expanded':
        return 280;
      case 'balanced':
      default:
        return 180;
    }
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
        message: 'Parâmetros necessários não fornecidos.'
      };
      return;
    }

    this.loadDatasetName();
    this.loadProfile();
    this.loadRecommendations();

    const queryParams = this.route.snapshot.queryParamMap;
    const aggFromUrl = queryParams.get('aggregation');
    const timeBinFromUrl = queryParams.get('timeBin');
    const xColumnFromUrl = queryParams.get('xColumn');
    const yColumnFromUrl = queryParams.get('yColumn');
    const metricYFromUrl = queryParams.getAll('metricY');
    const groupByFromUrl = queryParams.get('groupBy');
    const chartTypeFromUrl = queryParams.get('chartType');
    const filtersFromUrl = queryParams.getAll('filters');
    const zoomStartFromUrl = queryParams.get('zoomStart');
    const zoomEndFromUrl = queryParams.get('zoomEnd');
    const viewFromUrl = (queryParams.get('view') || '').toLowerCase();
    const percentileFromUrl = (queryParams.get('percentile') || '').toUpperCase();
    const modeFromUrl = (queryParams.get('mode') || '').toLowerCase();

    if (groupByFromUrl) {
      this.selectedGroupBy = groupByFromUrl;
    }

    if (xColumnFromUrl) {
      this.selectedXAxis = xColumnFromUrl;
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

    if (viewFromUrl === 'percentile') {
      this.percentileView = 'percentile';
    } else {
      this.percentileView = 'base';
    }

    if (percentileFromUrl === 'P5' || percentileFromUrl === 'P10' || percentileFromUrl === 'P90' || percentileFromUrl === 'P95') {
      this.selectedPercentile = percentileFromUrl as PercentileKind;
    }

    if (modeFromUrl === 'bucket' || modeFromUrl === 'overall') {
      this.selectedPercentileMode = modeFromUrl;
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
      !!xColumnFromUrl ||
      !!yColumnFromUrl ||
      metricYFromUrl.length > 0 ||
      !!groupByFromUrl ||
      !!chartTypeFromUrl ||
      filtersFromUrl.length > 0 ||
      viewFromUrl === 'percentile' ||
      !!percentileFromUrl ||
      !!modeFromUrl ||
      (this.zoomStart !== null && this.zoomEnd !== null);

    if (shouldLoadWithOverrides) {
      const options: ChartLoadOptions = {};
      if (aggFromUrl) options.aggregation = aggFromUrl;
      if (timeBinFromUrl) options.timeBin = timeBinFromUrl;
      if (xColumnFromUrl) options.xColumn = xColumnFromUrl;
      if (this.selectedMetric) options.metricY = this.selectedMetric;
      if (yColumnFromUrl) options.yColumn = yColumnFromUrl;
      if (groupByFromUrl) options.groupBy = groupByFromUrl;
      if (filtersFromUrl.length > 0) options.filters = filtersFromUrl;
      if (this.percentileView === 'percentile' && this.selectedPercentile) {
        options.view = 'percentile';
        options.percentile = this.selectedPercentile;
      }
      if (this.selectedPercentileMode) {
        options.mode = this.selectedPercentileMode;
      }
      this.loadChart(options);
    } else {
      this.loadChart();
    }
  }

  ngOnDestroy(): void {
    this.chartLoadVersion++;
    this.rawDataLoadVersion++;

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
      this.echartsInstance.getZr().off('click');
    }

    if (this.simulationEchartsInstance) {
      this.simulationEchartsInstance.off('click');
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
          this.recommendedState = this.buildRecommendedState(this.currentRecommendation);
          const queryParams = this.route.snapshot.queryParamMap;
          const aggFromUrl = queryParams.get('aggregation');
          const timeBinFromUrl = queryParams.get('timeBin');
          const xColumnFromUrl = queryParams.get('xColumn');
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
          this.selectedXAxis = xColumnFromUrl || this.currentRecommendation.xColumn || '';
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

        this.availableMetrics = this.profileColumns
          .filter(c => c.inferredType === 'Number')
          .map(c => c.name);

        this.availableGroupBy = this.profileColumns
          .filter(c => c.inferredType !== 'Number' && c.distinctCount <= this.maxGroupByDistinct)
          .map(c => c.name);

        const hadInvalidGroupBy = !!this.selectedGroupBy && !this.isGroupByAllowed(this.selectedGroupBy);
        if (hadInvalidGroupBy) {
          this.toast.info(`Não é possível agrupar por '${this.selectedGroupBy}'. O limite máximo é ${this.maxGroupByDistinct} grupos.`);
          this.selectedGroupBy = '';
          this.syncUrlWithoutReload();
          this.loadChart(this.buildLoadOptionsFromState());
        }

        this.syncSelectedMetricsWithAvailability();
        this.syncSelectedXAxisWithAvailability();
        this.rebuildRawFieldMetrics();

        if (this.selectedMetric && !this.availableMetrics.includes(this.selectedMetric)) {
          this.availableMetrics = [this.selectedMetric, ...this.availableMetrics];
        }

        if (this.selectedXAxis && !this.availableMetrics.includes(this.selectedXAxis)) {
          this.availableMetrics = [this.selectedXAxis, ...this.availableMetrics];
        }

        this.initializeScenarioDefaults();
      },
      error: (err) => {
        console.error('Error loading profile:', err);
      }
    });
  }

  loadChart(
    options?: ChartLoadOptions,
    chartOnlyUpdate: boolean = false,
    onSuccess?: () => void,
    onFailure?: () => void): void {
    const requestOptions: ChartLoadOptions = options ? { ...options } : {};

    if (this.supportsMetricControl) {
      const primaryMetric = requestOptions.metricY || this.selectedMetric;
      if (primaryMetric) {
        requestOptions.metricY = primaryMetric;
      }
    }

    if (this.supportsScatterXAxisControl) {
      const xColumn = requestOptions.xColumn || this.selectedXAxis;
      if (xColumn) {
        requestOptions.xColumn = xColumn;
      }
    }

    const isInitialLoad = !this.chartOption;
    this.loading = isInitialLoad;
    this.chartRefreshing = !isInitialLoad;
    this.error = null;
    this.pendingDrilldownCategory = null;
    if (!chartOnlyUpdate) {
      this.aiSummary = null;
      this.aiSummaryMeta = null;
      this.aiSummaryError = null;
      this.explainResult = null;
      this.explainError = null;
      this.explainPanelOpen = false;
      this.deepInsights = null;
      this.deepInsightsError = null;
      this.deepInsightsShowEvidence = false;
      this.askPlan = null;
      this.askError = null;
      this.askReasoningExpanded = false;
      this.llmUseScenarioContext = false;
    }

    const loadVersion = ++this.chartLoadVersion;
    const startedAt = performance.now();

    this.datasetApi.getChart(this.datasetId, this.recommendationId, requestOptions).subscribe({
      next: (response) => {
        if (loadVersion !== this.chartLoadVersion) {
          return;
        }

        this.loading = false;
        this.chartRefreshing = false;
        this.drilldownLoading = false;

        if (response.success && response.data) {
          this.chartMeta = response.data.meta || null;
          this.insightSummary = response.data.insightSummary || null;
          const viewMeta = this.chartMeta?.view;
          if (viewMeta?.kind === 'Percentile') {
            this.percentileView = 'percentile';
            if (viewMeta.percentileKind) {
              this.selectedPercentile = viewMeta.percentileKind;
            }
            if (viewMeta.percentileMode === 'Bucket') {
              this.selectedPercentileMode = 'bucket';
            } else if (viewMeta.percentileMode === 'Overall') {
              this.selectedPercentileMode = 'overall';
            }
          } else {
            this.percentileView = 'base';
            const percentileMode = this.chartMeta?.percentiles?.mode;
            if (percentileMode === 'Bucket') {
              this.selectedPercentileMode = 'bucket';
            } else if (percentileMode === 'Overall') {
              this.selectedPercentileMode = 'overall';
            }
          }
          const primaryMetric = requestOptions.metricY || this.selectedMetric;

          this.applyChartOptionWithEnhancements(
            response.data.option,
            primaryMetric,
            requestOptions,
            loadVersion);

          if (!this.chartOption) {
            this.error = {
              code: 'NO_CHART_DATA',
              message: 'Nenhum dado de gráfico retornado.'
            };
            if (this.pendingDrilldownRollback) {
              this.toast.error(this.error.message || 'Não foi possível aplicar o drilldown.');
              onFailure?.();
            }
          } else {
            this.pendingDrilldownRollback = null;
            onSuccess?.();
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

        if (this.pendingDrilldownRollback) {
          this.toast.error(this.error?.message || 'Não foi possível aplicar o drilldown.');
          onFailure?.();
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
        this.chartRefreshing = false;
        this.drilldownLoading = false;
        this.error = HttpErrorUtil.extractApiError(err) || {
          code: 'LOAD_ERROR',
          message: HttpErrorUtil.extractErrorMessage(err)
        };
        if (this.pendingDrilldownRollback) {
          this.toast.error(this.error.message || 'Não foi possível aplicar o drilldown.');
          onFailure?.();
        }
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
    this.showRawQuickFiltersAside = window.innerWidth >= 1280;

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

  onActiveTabChange(index: number): void {
    this.activeTab = index;
    if (index !== 0) {
      this.controlsOpen = false;
      return;
    }

    this.controlsOpen = !this.isMobile;
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
          this.aiSummaryError = response.errors?.[0]?.message || 'Não foi possível gerar o resumo de IA.';
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

  generateDeepInsights(): void {
    if (this.deepInsightsLoading) {
      return;
    }

    this.deepInsightsLoading = true;
    this.deepInsightsError = null;
    this.deepInsightsShowEvidence = false;

    const payload = this.buildDeepInsightsPayload();
    const startedAt = performance.now();

    this.datasetApi.generateDeepInsights(this.datasetId, this.recommendationId, payload).subscribe({
      next: response => {
        this.deepInsightsLoading = false;

        if (!response.success || !response.data) {
          this.deepInsightsError = response.errors?.[0]?.message || 'Unable to generate deep insights.';
          return;
        }

        this.deepInsights = response.data;
        this.logDevTiming('deep-insights', startedAt, {
          datasetId: this.datasetId,
          recommendationId: this.recommendationId,
          cacheHit: this.deepInsights.meta?.cacheHit || false,
          fallback: this.deepInsights.meta?.fallbackUsed || false
        });
      },
      error: err => {
        this.deepInsightsLoading = false;
        this.deepInsightsError = HttpErrorUtil.extractErrorMessage(err);
        this.logDevTiming('deep-insights-error', startedAt, {
          datasetId: this.datasetId,
          recommendationId: this.recommendationId
        });
      }
    });
  }

  copyDeepInsightsReport(): void {
    if (!this.deepInsights?.report) {
      return;
    }

    const report = this.deepInsights.report;
    const lines: string[] = [];
    lines.push(report.headline);
    lines.push('');
    lines.push(report.executiveSummary);
    lines.push('');
    lines.push('Key Findings');
    report.keyFindings.forEach(item => lines.push(`- ${item.title}: ${item.narrative}`));
    lines.push('');
    lines.push('Recommended Actions');
    report.recommendedActions.forEach(item => lines.push(`- ${item.action} (${item.effort})`));
    lines.push('');
    lines.push('Next Questions');
    report.nextQuestions.forEach(item => lines.push(`- ${item}`));

    navigator.clipboard.writeText(lines.join('\n')).then(() => {
      this.toast.success('Deep insights report copied.');
    }).catch(() => {
      this.toast.error('Could not copy deep insights report.');
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
          this.explainError = response.errors?.[0]?.message || 'Não foi possível explicar o gráfico.';
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
      this.toast.success('Explicação copiada.');
    }).catch(() => {
      this.toast.error('Não foi possível copiar a explicação.');
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
          this.askError = response.errors?.[0]?.message || 'Não foi possível analisar a pergunta.';
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
    if (nextGroupBy && !this.isGroupByAllowed(nextGroupBy)) {
      this.toast.info(`Não é possível agrupar por '${nextGroupBy}'. O limite máximo é ${this.maxGroupByDistinct} grupos.`);
      this.selectedGroupBy = '';
    } else {
      this.selectedGroupBy = nextGroupBy;
    }

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
          value: filter.values.join(','),
          logicalOperator: 'And'
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
    scenarioMeta?: Record<string, unknown>;
  } {
    const options = this.buildLoadOptionsFromState();
    const scenarioMeta = this.buildScenarioMetaForLlm();
    return {
      aggregation: options.aggregation,
      timeBin: options.timeBin,
      metricY: options.metricY,
      groupBy: options.groupBy,
      filters: options.filters,
      scenarioMeta
    };
  }

  private buildDeepInsightsPayload(): DeepInsightsRequest {
    const basePayload = this.buildAiSummaryPayload();
    const payload: DeepInsightsRequest = {
      ...basePayload,
      horizon: this.deepInsightsHorizon,
      sensitiveMode: this.deepInsightsSensitiveMode,
      includeEvidence: true
    };

    if (this.deepInsightsUseScenario) {
      const scenario = this.buildSimulationPayload();
      if (scenario) {
        payload.scenario = scenario;
      }
    }

    return payload;
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
    const baseline = this.recommendedState || (this.currentRecommendation ? this.buildRecommendedState(this.currentRecommendation) : null);
    this.applyRecommendedState(baseline);
    this.selectedGroupBy = '';
    this.selectedVisualizationType = '';
    this.zoomStart = null;
    this.zoomEnd = null;
    this.metricToAdd = '';
    this.filterRules = [];
    this.rawDataSearch = '';
    this.rawSortColumn = '';
    this.rawSortDirection = 'asc';
    this.rawPageIndex = 0;
    this.rawFieldSearch = '';
    this.selectedRawFieldName = '';
    this.pendingDrilldownCategory = null;
    this.drilldownLoading = false;
    this.clearDrilldownTrail();
    this.percentileView = 'base';
    this.selectedPercentile = null;
    this.selectedPercentileMode = '';
    this.baseChartOptionSnapshot = null;
    this.filterPreviewPending = false;
    this.llmUseScenarioContext = false;
    this.selectedPoint = null;
    this.selectedPointModalOpen = false;
    this.pointDataSearch = '';
    this.activeTab = 0;

    this.simulationError = null;
    this.simulationResult = null;
    this.simulationChartOption = null;
    this.simulationOperations = [];
    this.initializeScenarioDefaults();

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {},
      replaceUrl: true
    }).then(() => {
      this.loadChart(this.buildLoadOptionsFromState());
      this.loadRawDataRows(true);
    });
  }

  onChartInit(ec: ECharts): void {
    this.echartsInstance = ec;
    const zr = this.echartsInstance.getZr();
    zr.off('click');
    zr.on('click', event => this.onChartCanvasClick(event));
  }

  onSimulationChartInit(ec: ECharts): void {
    this.simulationEchartsInstance = ec;
    this.simulationEchartsInstance.off('click');
    this.simulationEchartsInstance.on('click', event => this.onSimulationChartClick(event));
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
      this.clearDrilldownTrail();
      this.drilldownLoading = false;
      this.currentIndex = this.recommendations.findIndex(r => r.id === recId);
      this.currentRecommendation = this.currentIndex >= 0 ? this.recommendations[this.currentIndex] : null;
      this.recommendedState = this.currentRecommendation
        ? this.buildRecommendedState(this.currentRecommendation)
        : null;

      let chartOptions: ChartLoadOptions | undefined;
      if (!keepContext && this.currentRecommendation) {
        this.selectedAggregation = this.currentRecommendation.aggregation || 'Sum';
        this.selectedTimeBin = this.currentRecommendation.timeBin || 'Month';
        this.selectedXAxis = this.currentRecommendation.xColumn || '';
        this.selectedMetric = this.currentRecommendation.yColumn || '';
        this.selectedMetricsY = this.selectedMetric ? [this.selectedMetric] : [];
        this.metricToAdd = '';
        this.selectedGroupBy = '';
        this.filterRules = [];
        this.clearDrilldownTrail();
        this.percentileView = 'base';
        this.selectedPercentile = null;
        this.selectedPercentileMode = '';
        this.baseChartOptionSnapshot = null;
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

  onScatterXAxisChange(): void {
    this.reloadChartWithCurrentParameters();
  }

  isScatterMetricSelected(metric: string): boolean {
    return this.selectedMetricsY.includes(metric);
  }

  onScatterMetricToggle(metric: string, checked: boolean): void {
    if (!metric) {
      return;
    }

    if (checked) {
      if (!this.selectedMetricsY.includes(metric)) {
        this.selectedMetricsY = [...this.selectedMetricsY, metric];
      }
    } else {
      if (!this.selectedMetricsY.includes(metric) || this.selectedMetricsY.length <= 1) {
        return;
      }

      this.selectedMetricsY = this.selectedMetricsY.filter(item => item !== metric);
    }

    this.selectedMetric = this.selectedMetricsY[0] || metric;
    this.reloadChartWithCurrentParameters();
  }

  onGroupByChange(): void {
    if (this.selectedGroupBy && !this.isGroupByAllowed(this.selectedGroupBy)) {
      this.toast.info(`Não é possível agrupar por '${this.selectedGroupBy}'. O limite máximo é ${this.maxGroupByDistinct} grupos.`);
      this.selectedGroupBy = '';
      this.syncUrlWithoutReload();
      return;
    }

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

  applyPercentileView(kind: PercentileKind): void {
    if (!this.canUsePercentiles) {
      return;
    }

    this.percentileView = 'percentile';
    this.selectedPercentile = kind;
    this.reloadChartWithCurrentParameters(false, true);
  }

  backToBaseView(): void {
    this.percentileView = 'base';

    if (this.baseChartOptionSnapshot) {
      this.chartOption = this.cloneOption(this.baseChartOptionSnapshot);
      this.buildChartDataGrid(this.chartOption);
      if (this.chartMeta) {
        this.chartMeta = {
          ...this.chartMeta,
          view: {
            kind: 'Base'
          }
        };
      }
      this.syncUrlWithoutReload();
      return;
    }

    this.reloadChartWithCurrentParameters(false, true);
  }

  applyDrilldown(categoryOverride?: string): void {
    const selectedCategory = categoryOverride ?? this.pendingDrilldownCategory;
    if (!selectedCategory) {
      return;
    }

    const column = this.currentRecommendation?.xColumn || this.readAxisName(this.chartOption, 'xAxis') || '';
    if (!column) {
      this.toast.error('Não foi possível identificar a coluna para drilldown.');
      return;
    }

    if (this.drilldownTrail.length > 0 && !this.isDrilldownTrailInSync()) {
      this.clearDrilldownTrail();
    }

    const drilldownSnapshot = this.snapshotDrilldownState();
    const filtersBefore = this.cloneFilterRules(this.filterRules);
    const normalizedCategory = selectedCategory.trim();
    const isHistogramDrilldown =
      this.currentBaseChartType === 'Histogram' || this.currentDisplayChartType === 'Histogram';
    const parsedRange = this.parseHistogramRangeLabel(normalizedCategory);
    const shouldUseRange = !!parsedRange && isHistogramDrilldown;

    if (isHistogramDrilldown && !shouldUseRange) {
      this.toast.error('Não foi possível interpretar a faixa do histograma para drilldown.');
      return;
    }

    const drilldownOperator = shouldUseRange ? 'Between' : 'Eq';
    const drilldownValue = shouldUseRange
      ? `${this.toInvariantNumber(parsedRange!.min)},${this.toInvariantNumber(parsedRange!.max)}`
      : normalizedCategory;

    const existingIndex = this.filterRules.findIndex(rule =>
      rule.column === column &&
      rule.operator === drilldownOperator &&
      rule.value === drilldownValue);

    if (existingIndex >= 0) {
      this.filterRules.splice(existingIndex, 1);
      this.trimDrilldownTrailToCurrentFilters();
      this.pendingDrilldownCategory = null;
      this.drilldownLoading = true;
      this.pendingDrilldownRollback = drilldownSnapshot;
      this.reloadChartWithCurrentParameters(true, true, true);
      return;
    }

    this.filterRules = this.filterRules.filter(rule => rule.column !== column);
    const upsertResult = this.upsertFilterRule(column, drilldownOperator, drilldownValue);
    if (upsertResult === 'replaced') {
      this.toast.info('Limite de filtros atingido. O filtro mais antigo foi substituído.');
    }

    this.pushDrilldownTrail(normalizedCategory, filtersBefore, {
      column,
      operator: drilldownOperator,
      value: drilldownValue,
      logicalOperator: 'And'
    });
    this.pendingDrilldownCategory = null;
    this.drilldownLoading = true;
    this.pendingDrilldownRollback = drilldownSnapshot;
    this.reloadChartWithCurrentParameters(true, true, true);
  }

  clearDrilldownSelection(): void {
    this.pendingDrilldownCategory = null;
  }

  zoomOutDrilldown(): void {
    if (!this.canZoomOutDrilldown) {
      return;
    }

    this.zoomToDrilldownLevel(this.visibleDrilldownTrail.length - 2);
  }

  resetDrilldown(): void {
    if (this.visibleDrilldownTrail.length === 0) {
      return;
    }

    this.zoomToDrilldownLevel(-1);
  }

  zoomToDrilldownLevel(levelIndex: number): void {
    if (!this.isDrilldownTrailInSync()) {
      this.clearDrilldownTrail();
      return;
    }

    const drilldownSnapshot = this.snapshotDrilldownState();

    if (levelIndex < 0) {
      this.filterRules = this.cloneFilterRules(this.drilldownBaseFilters || []);
      this.clearDrilldownTrail();
      this.pendingDrilldownCategory = null;
      this.drilldownLoading = true;
      this.pendingDrilldownRollback = drilldownSnapshot;
      this.reloadChartWithCurrentParameters(true, true, true);
      return;
    }

    const targetLevel = this.drilldownTrail[levelIndex];
    if (!targetLevel) {
      return;
    }

    this.filterRules = this.cloneFilterRules(targetLevel.filtersState);
    this.drilldownTrail = this.drilldownTrail.slice(0, levelIndex + 1);
    this.pendingDrilldownCategory = null;
    this.drilldownLoading = true;
    this.pendingDrilldownRollback = drilldownSnapshot;
    this.reloadChartWithCurrentParameters(true, true, true);
  }

  private reloadChartWithCurrentParameters(
    refreshRawData: boolean = false,
    chartOnlyUpdate: boolean = false,
    deferUrlUpdate: boolean = false): void {
    const options = this.buildLoadOptionsFromState();
    this.filterPreviewPending = false;

    if (deferUrlUpdate) {
      this.loadChart(
        options,
        chartOnlyUpdate,
        () => {
          this.syncUrlWithoutReload();
          if (refreshRawData) {
            this.rawPageIndex = 0;
            this.loadRawDataRows(true);
          }
        },
        () => {
          this.restorePendingDrilldownState();
        });
      return;
    }

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: this.buildCurrentQueryParams(),
      replaceUrl: true
    });

    this.loadChart(options, chartOnlyUpdate);
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

    if (this.supportsScatterXAxisControl && this.selectedXAxis) {
      options.xColumn = this.selectedXAxis;
    }

    if (this.supportsMetricControl && this.selectedMetric) {
      options.metricY = this.selectedMetric;
    }

    if (this.supportsGroupByControl && this.selectedGroupBy && this.isGroupByAllowed(this.selectedGroupBy)) {
      options.groupBy = this.selectedGroupBy;
    }

    const filters = this.buildFilterParams();
    if (filters.length > 0) {
      options.filters = filters;
    }

    if (this.percentileView === 'percentile' && this.selectedPercentile) {
      options.view = 'percentile';
      options.percentile = this.selectedPercentile;
      options.percentileTarget = 'y';
      if (this.selectedPercentileMode) {
        options.mode = this.selectedPercentileMode;
      }
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

    if (this.supportsScatterXAxisControl && this.selectedXAxis) {
      options['xColumn'] = this.selectedXAxis;
    }

    if (this.supportsMetricControl && this.selectedMetricsY.length > 0) {
      options['metricY'] = this.selectedMetricsY;
    } else if (this.supportsMetricControl && this.selectedMetric) {
      options['metricY'] = this.selectedMetric;
    }

    if (this.supportsGroupByControl && this.selectedGroupBy && this.isGroupByAllowed(this.selectedGroupBy)) {
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

    if (this.percentileView === 'percentile' && this.selectedPercentile) {
      options['view'] = 'percentile';
      options['percentile'] = this.selectedPercentile;
      if (this.selectedPercentileMode) {
        options['mode'] = this.selectedPercentileMode;
      }
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
      value: '',
      logicalOperator: 'And'
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

  private buildFilterParams(excludeColumn?: string): string[] {
    const normalizedExclude = (excludeColumn || '').trim().toLowerCase();
    const validRules = this.filterRules
      .filter(rule => rule.column && rule.operator && rule.value)
      .filter(rule => !normalizedExclude || rule.column.trim().toLowerCase() !== normalizedExclude);

    return validRules.map((rule, index) => {
      const logicalOperator = index === 0 ? 'And' : (rule.logicalOperator || 'And');
      return `${rule.column}|${rule.operator}|${rule.value}|${logicalOperator}`;
    });
  }

  private parseFiltersFromUrl(filters: string[]): FilterRule[] {
    return filters
      .map(raw => {
        const parts = raw.split('|');
        const lastPart = parts.length > 3 ? parts[parts.length - 1] : '';
        const hasLogicalOperator = lastPart === 'And' || lastPart === 'Or';
        const valueEndIndex = hasLogicalOperator ? parts.length - 1 : parts.length;
        return {
          column: parts[0] || '',
          operator: parts[1] || 'Eq',
          value: parts.slice(2, valueEndIndex).join('|') || '',
          logicalOperator: hasLogicalOperator ? (lastPart as 'And' | 'Or') : 'And'
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

    return 'Visualização de Gráfico';
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
      this.toast.success('Link copiado. Inclui filtros, zoom e parâmetros ativos.');
    });
  }

  exportChartPNG(): void {
    if (!this.echartsInstance) {
      this.toast.error('Gráfico ainda não foi carregado.');
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

      this.toast.success('Gráfico exportado com sucesso.');
    } catch (error) {
      console.error('Error exporting chart:', error);
      this.toast.error('Erro ao exportar gráfico.');
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
      this.toast.info('Limite de 3 operações por cenário.');
      return;
    }

    this.simulationOperations.push(this.createDefaultSimulationOperation());
  }

  applySimulationPreset(preset: 'increase10' | 'decrease10' | 'capP95'): void {
    this.initializeScenarioDefaults();
    switch (preset) {
      case 'increase10':
        this.simulationOperations = [{
          type: 'MultiplyMetric',
          column: this.simulationTargetDimension,
          values: '',
          factor: 1.1,
          constant: null,
          min: null,
          max: null
        }];
        break;
      case 'decrease10':
        this.simulationOperations = [{
          type: 'MultiplyMetric',
          column: this.simulationTargetDimension,
          values: '',
          factor: 0.9,
          constant: null,
          min: null,
          max: null
        }];
        break;
      case 'capP95':
        this.simulationOperations = [{
          type: 'Clamp',
          column: this.simulationTargetDimension,
          values: '',
          factor: null,
          constant: null,
          min: null,
          max: this.estimateMetricP95()
        }];
        break;
    }
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

  runSimulation(generateDeepInsightsAfter: boolean = false, onSuccess?: () => void): void {
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
          if (generateDeepInsightsAfter) {
            this.deepInsightsUseScenario = true;
            this.generateDeepInsights();
          }
          onSuccess?.();
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

  runSimulationAndGenerateDeepInsights(): void {
    this.runSimulation(true);
  }

  runSimulationAndGenerateAiSummary(): void {
    this.runSimulation(false, () => {
      this.llmUseScenarioContext = true;
      this.generateAiSummary();
    });
  }

  runAskFromSuggestion(question: string): void {
    const trimmed = question.trim();
    if (!trimmed) {
      return;
    }

    this.askQuestion = trimmed;
    this.submitAskQuestion();
  }

  drillSimulationDimension(dimension: string): void {
    const trimmedDimension = `${dimension ?? ''}`.trim();
    if (!trimmedDimension) {
      return;
    }

    const column = this.simulationTargetDimension || this.selectedGroupBy || this.currentRecommendation?.xColumn || '';
    if (!column) {
      this.toast.error('Could not determine the target column for simulation drilldown.');
      return;
    }

    const upsertResult = this.upsertFilterRule(column, 'Eq', trimmedDimension);
    if (upsertResult === 'replaced') {
      this.toast.info('Limite de filtros atingido. O filtro mais antigo foi substituído.');
    }

    this.activeTab = 0;
    this.reloadChartWithCurrentParameters(true);
    this.toast.success(`Drilldown applied for ${column} = ${trimmedDimension}.`);
  }

  drillLargestSimulationDelta(): void {
    const top = this.topSimulationDeltas[0];
    if (!top) {
      this.toast.info('Run a simulation first to enable drilldown.');
      return;
    }

    this.drillSimulationDimension(top.dimension);
  }

  private buildSimulationPayload(): ScenarioSimulationRequest | null {
    if (!this.simulationTargetMetric || !this.simulationTargetDimension) {
      this.toast.error('Selecione métrica e dimensão para simular.');
      return null;
    }

    if (this.simulationOperations.length === 0) {
      this.toast.error('Adicione ao menos uma operação.');
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
          this.toast.error('Multiply Metric requer um fator numérico.');
          return null;
        }
        return { type: rule.type, factor: rule.factor };

      case 'AddConstant':
        if (rule.constant === null || Number.isNaN(rule.constant)) {
          this.toast.error('Add Constant requer um valor numérico.');
          return null;
        }
        return { type: rule.type, constant: rule.constant };

      case 'Clamp':
        if (rule.min === null && rule.max === null) {
          this.toast.error('Clamp requer min, max ou ambos.');
          return null;
        }
        if (rule.min !== null && rule.max !== null && rule.min > rule.max) {
          this.toast.error('Clamp inválido: min não pode ser maior que max.');
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
        this.toast.error(`Operação não suportada: ${rule.type}`);
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

  private parseRangeFilterPairs(raw: string): string[] {
    const values = this.splitCsvValues(raw);
    const pairs: string[] = [];
    for (let index = 0; index + 1 < values.length; index += 2) {
      pairs.push(`${values[index]},${values[index + 1]}`);
    }

    return pairs;
  }

  private rangeListContains(raw: string, pair: string): boolean {
    return this.parseRangeFilterPairs(raw).some(item => item === pair);
  }

  private estimateMetricP95(): number {
    const values = this.pointDataRows
      .map(row => Number(row['y']))
      .filter(value => Number.isFinite(value))
      .sort((left, right) => left - right);

    if (values.length === 0) {
      return 0;
    }

    const index = Math.floor((values.length - 1) * 0.95);
    return Number(values[index].toFixed(2));
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

  private onSimulationChartClick(event: unknown): void {
    const payload = event as Record<string, unknown>;
    const componentType = `${payload['componentType'] ?? ''}`;
    if (componentType !== 'series') {
      return;
    }

    const category = `${payload['name'] ?? ''}`.trim();
    if (!category) {
      return;
    }

    this.drillSimulationDimension(category);
  }

  private buildScenarioMetaForLlm(): Record<string, unknown> | undefined {
    if (!this.llmUseScenarioContext || !this.simulationResult) {
      return undefined;
    }

    return {
      queryHash: this.simulationResult.queryHash,
      targetMetric: this.simulationResult.targetMetric,
      targetDimension: this.simulationResult.targetDimension,
      aggregation: this.selectedAggregation,
      deltaSummary: this.simulationResult.deltaSummary,
      topDeltas: this.topSimulationDeltas.map(item => ({
        dimension: item.dimension,
        delta: item.delta,
        deltaPercent: item.deltaPercent ?? null
      })),
      operations: this.simulationOperations.slice(0, 3).map(item => ({
        type: item.type,
        column: item.column,
        values: this.splitCsvValues(item.values).slice(0, 3),
        factor: item.factor,
        constant: item.constant,
        min: item.min,
        max: item.max
      }))
    };
  }

  formatDeltaPercent(value?: number): string {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return 'N/A';
    }

    return `${value >= 0 ? '+' : ''}${value.toFixed(2)}%`;
  }

  severityChipColor(severity: string): 'primary' | 'accent' | 'warn' {
    switch ((severity || '').toLowerCase()) {
      case 'high':
        return 'warn';
      case 'low':
        return 'primary';
      default:
        return 'accent';
    }
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
      this.toast.info('Selecione um ponto no gráfico para ver os dados correspondentes.');
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


  selectRawField(fieldName: string): void {
    if (this.selectedRawFieldName === fieldName) {
      return;
    }

    this.selectedRawFieldName = fieldName;
    this.loadRawDataRows();
  }

  isRawTopValueSelected(column: string, value: string): boolean {
    const normalizedValue = `${value ?? ''}`.trim();
    if (!column || !this.canToggleRawTopValue(normalizedValue)) {
      return false;
    }

    return this.getRawTopValueRules(column).some(rule =>
      rule.operator === 'Eq'
        ? rule.value === normalizedValue
        : this.splitCsvValues(rule.value).includes(normalizedValue));
  }

  toggleRawTopValueFilter(column: string, value: string): void {
    const normalizedValue = `${value ?? ''}`.trim();
    if (!column || !this.canToggleRawTopValue(normalizedValue)) {
      return;
    }

    const rulesForColumn = this.getRawTopValueRules(column);
    const targetRule = rulesForColumn[0];
    const values = rulesForColumn.flatMap(rule =>
      rule.operator === 'Eq'
        ? [rule.value]
        : this.splitCsvValues(rule.value));
    const selected = new Set(values);

    if (selected.has(normalizedValue)) {
      selected.delete(normalizedValue);
    } else {
      selected.add(normalizedValue);
    }

    const updatedValues = Array.from(selected.values());
    if (updatedValues.length === 0) {
      this.filterRules = this.filterRules.filter(rule => !rulesForColumn.includes(rule));
    } else if (!targetRule) {
      this.filterRules.push({
        column,
        operator: updatedValues.length > 1 ? 'In' : 'Eq',
        value: updatedValues.length > 1 ? updatedValues.join(',') : updatedValues[0],
        logicalOperator: 'And'
      });
    } else if (updatedValues.length === 1) {
      targetRule.operator = 'Eq';
      targetRule.value = updatedValues[0];
      this.filterRules = this.filterRules.filter(rule => rule === targetRule || !rulesForColumn.includes(rule));
    } else {
      targetRule.operator = 'In';
      targetRule.value = updatedValues.join(',');
      this.filterRules = this.filterRules.filter(rule => rule === targetRule || !rulesForColumn.includes(rule));
    }

    this.activeTab = 1;
    this.filterPreviewPending = false;
    this.reloadChartWithCurrentParameters(true);
  }

  private getRawTopValueRules(column: string): FilterRule[] {
    return this.filterRules.filter(rule =>
      rule.column === column &&
      (rule.operator === 'Eq' || rule.operator === 'In'));
  }

  isFieldFiltered(fieldName: string): boolean {
    const normalizedField = `${fieldName ?? ''}`.trim().toLowerCase();
    if (!normalizedField) {
      return false;
    }

    return this.filterRules.some(rule =>
      !!rule.column &&
      !!rule.operator &&
      !!rule.value &&
      rule.column.trim().toLowerCase() === normalizedField);
  }

  getFieldDistinctDisplayCount(field: RawFieldMetric): number {
    const facetDistinct = this.rawFacetDistinctCounts[field.name.trim().toLowerCase()];
    if (typeof facetDistinct === 'number') {
      return facetDistinct;
    }

    if (this.rawFieldStats && this.rawFieldStats.column === field.name) {
      return this.rawFieldStats.distinctCount;
    }

    return field.distinctCountProfile || field.distinctCountPage;
  }

  formatRawTopValueLabel(value: string): string {
    if (value === this.rawTopOthersToken) {
      return '(outros)';
    }

    return `${value ?? ''}`.trim() || '(vazio)';
  }

  canToggleRawTopValue(value: string): boolean {
    const normalizedValue = `${value ?? ''}`.trim();
    return normalizedValue.length > 0 && normalizedValue !== this.rawTopOthersToken;
  }

  private withRawTopValueComplements(
    values: RawDistinctValueStat[],
    totalRowsInput: number): RawDistinctValueStat[] {
    const totalRows = Math.max(totalRowsInput || 0, 0);
    if (totalRows === 0) {
      return values;
    }

    const accountedRows = values.reduce((total, item) => total + Math.max(item.count || 0, 0), 0);
    const remainingRows = Math.max(totalRows - accountedRows, 0);
    if (remainingRows === 0) {
      return values;
    }

    return [
      ...values,
      {
        value: this.rawTopOthersToken,
        count: remainingRows
      }
    ];
  }

  private refreshRawTopValueStats(
    column: string | undefined,
    search: string,
    requestVersion: number,
    payload: RawDatasetRowsResponse): void {
    if (!column) {
      return;
    }

    const normalizedColumn = column.trim().toLowerCase();
    const hasFilterOnSameColumn = this.filterRules.some(rule =>
      rule.column.trim().toLowerCase() === normalizedColumn &&
      !!rule.operator &&
      !!rule.value);

    if (!hasFilterOnSameColumn) {
      this.rawTopValueStats = payload.fieldStats || null;
      this.rawTopValueStatsTotalRows = payload.rowCountTotal || 0;
      return;
    }

    const filtersWithoutCurrentColumn = this.buildFilterParams(column);
    this.datasetApi.getRawRows(this.datasetId, {
      page: 1,
      pageSize: 1,
      search: search.length > 0 ? search : undefined,
      filters: filtersWithoutCurrentColumn.length > 0 ? filtersWithoutCurrentColumn : undefined,
      fieldStatsColumn: column
    }).subscribe({
      next: response => {
        if (requestVersion !== this.rawDataLoadVersion) {
          return;
        }

        if (!response.success || !response.data) {
          return;
        }

        const scoped = response.data as RawDatasetRowsResponse;
        this.rawTopValueStats = scoped.fieldStats || null;
        this.rawTopValueStatsTotalRows = scoped.rowCountTotal || 0;
      },
      error: err => {
        if (requestVersion !== this.rawDataLoadVersion) {
          return;
        }

        if (HttpErrorUtil.isRequestAbort(err)) {
          return;
        }
      }
    });
  }

  private refreshRawFacetDistinctCounts(
    filters: string[],
    requestVersion: number,
    payload: RawDatasetRowsResponse): void {
    const columns = payload.columns || [];
    if (columns.length === 0) {
      this.rawFacetDistinctCounts = {};
      this.rawFacetDistinctCountsKey = '';
      return;
    }

    const cacheKey = this.buildRawFacetDistinctCountsKey(filters);
    if (this.rawFacetDistinctCountsKey === cacheKey && Object.keys(this.rawFacetDistinctCounts).length > 0) {
      return;
    }

    this.rawFacetDistinctCountsKey = cacheKey;

    const facetRequests = columns.map(column => {
      const facetFilters = this.buildFilterParams(column);
      return this.datasetApi.getRawRows(this.datasetId, {
        page: 1,
        pageSize: 1,
        search: undefined,
        filters: facetFilters.length > 0 ? facetFilters : undefined,
        fieldStatsColumn: column
      }).pipe(
        map(response => ({
          column,
          distinctCount: response.success && response.data?.fieldStats
            ? response.data.fieldStats.distinctCount
            : null
        })),
        catchError(() => of({ column, distinctCount: null }))
      );
    });

    forkJoin(facetRequests).subscribe(results => {
      if (requestVersion !== this.rawDataLoadVersion) {
        return;
      }

      const nextCounts: Record<string, number> = {};
      for (const item of results) {
        if (typeof item.distinctCount === 'number') {
          nextCounts[item.column.trim().toLowerCase()] = item.distinctCount;
        }
      }

      this.rawFacetDistinctCounts = nextCounts;
    });
  }

  private buildRawFacetDistinctCountsKey(filters: string[]): string {
    const normalizedFilters = [...filters].sort();
    return `${this.datasetId}|${normalizedFilters.join('||')}`;
  }

  isRawTopRangeSelected(column: string, range: RawRangeValueStat): boolean {
    if (!column || !range?.from || !range?.to) {
      return false;
    }

    const normalizedPair = `${range.from},${range.to}`;
    return this.filterRules.some(rule =>
      rule.column === column &&
      rule.operator === 'Between' &&
      this.rangeListContains(rule.value, normalizedPair));
  }

  toggleRawTopRangeFilter(column: string, range: RawRangeValueStat): void {
    if (!column || !range?.from || !range?.to) {
      return;
    }

    const normalizedPair = `${range.from},${range.to}`;
    const betweenRule = this.filterRules.find(rule =>
      rule.column === column &&
      rule.operator === 'Between');

    if (!betweenRule) {
      this.filterRules.push({
        column,
        operator: 'Between',
        value: normalizedPair,
        logicalOperator: 'And'
      });
      this.activeTab = 1;
      this.filterPreviewPending = false;
      this.reloadChartWithCurrentParameters(true);
      return;
    }

    const rangePairs = this.parseRangeFilterPairs(betweenRule.value);
    const updatedPairs = rangePairs.filter(pair => pair !== normalizedPair);
    if (updatedPairs.length === rangePairs.length) {
      updatedPairs.push(normalizedPair);
    }

    if (updatedPairs.length === 0) {
      this.filterRules = this.filterRules.filter(rule => rule !== betweenRule);
    } else {
      betweenRule.value = updatedPairs.join(',');
    }

    this.activeTab = 1;
    this.filterPreviewPending = false;
    this.reloadChartWithCurrentParameters(true);
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
    const requestVersion = ++this.rawDataLoadVersion;

    const sort = this.rawSortColumn
      ? [`${this.rawSortColumn}|${this.rawSortDirection}`]
      : [];
    const filters = this.buildFilterParams();
    const search = this.rawDataSearch.trim();
    const requestedFieldStatsColumn =
      this.selectedRawFieldName ||
      this.rawSortColumn ||
      this.rawDataColumns[0] ||
      this.profileColumns[0]?.name;

    this.datasetApi.getRawRows(this.datasetId, {
      page: this.rawPageIndex + 1,
      pageSize: this.rawPageSize,
      sort,
      search: search.length > 0 ? search : undefined,
      filters: filters.length > 0 ? filters : undefined,
      fieldStatsColumn: requestedFieldStatsColumn
    }).subscribe({
      next: response => {
        if (requestVersion !== this.rawDataLoadVersion) {
          return;
        }

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
        this.rawFieldStats = payload.fieldStats || null;

        if (!this.rawSortColumn && this.rawDataColumns.length > 0) {
          this.rawSortColumn = this.rawDataColumns[0];
        }

        if (this.rawPageIndex >= this.rawTotalPages && this.rawTotalPages > 0) {
          this.rawPageIndex = this.rawTotalPages - 1;
          this.loadRawDataRows();
          return;
        }

        this.rebuildRawFieldMetrics();
        if (!this.selectedRawFieldName && this.rawFieldMetrics.length > 0) {
          this.selectedRawFieldName = this.rawFieldMetrics[0].name;
        }

        if (
          !this.rawFieldStats &&
          this.selectedRawFieldName &&
          requestedFieldStatsColumn !== this.selectedRawFieldName) {
          this.loadRawDataRows();
          return;
        }

        this.refreshRawTopValueStats(
          requestedFieldStatsColumn,
          search,
          requestVersion,
          payload);
        this.refreshRawFacetDistinctCounts(
          filters,
          requestVersion,
          payload);

        this.refreshPointDataFromRawRows();
      },
      error: err => {
        if (requestVersion !== this.rawDataLoadVersion) {
          return;
        }

        this.rawDataLoading = false;
        if (HttpErrorUtil.isRequestAbort(err)) {
          return;
        }

        this.rawDataError = HttpErrorUtil.extractErrorMessage(err);
      }
    });
  }

  private rebuildRawFieldMetrics(): void {
    if (this.rawDataColumns.length === 0) {
      this.rawFieldMetrics = [];
      this.selectedRawFieldName = '';
      this.rawFieldStats = null;
      this.rawTopValueStats = null;
      this.rawTopValueStatsTotalRows = 0;
      this.rawFacetDistinctCounts = {};
      this.rawFacetDistinctCountsKey = '';
      return;
    }

    const profileLookup = new Map<string, DatasetColumnProfile>();
    for (const profile of this.profileColumns) {
      profileLookup.set(profile.name.toLowerCase(), profile);
    }

    const metrics = this.rawDataColumns.map(columnName => {
      const distinctMap = new Map<string, number>();
      let nullCount = 0;

      for (const row of this.rawDataRows) {
        const rawValue = `${row[columnName] ?? ''}`.trim();
        if (!rawValue) {
          nullCount++;
          continue;
        }

        distinctMap.set(rawValue, (distinctMap.get(rawValue) || 0) + 1);
      }

      const profile = profileLookup.get(columnName.toLowerCase());
      const totalRows = Math.max(this.rawDataRows.length, 1);
      const topValuesProfile = (profile?.topValueStats || [])
        .map(item => ({
          value: item.value,
          count: item.count
        }));

      if (topValuesProfile.length === 0) {
        for (const value of profile?.topValues || []) {
          topValuesProfile.push({
            value,
            count: 0
          });
        }
      }

      return {
        name: columnName,
        inferredType: profile?.inferredType || 'Unknown',
        distinctCountProfile: profile?.distinctCount || 0,
        distinctCountPage: distinctMap.size,
        nullCountPage: nullCount,
        nullRatePage: nullCount / totalRows,
        topValuesProfile
      } as RawFieldMetric;
    });

    this.rawFieldMetrics = metrics;
    const selectedExists = this.rawFieldMetrics.some(field => field.name === this.selectedRawFieldName);
    if (!selectedExists) {
      this.selectedRawFieldName = this.rawFieldMetrics[0]?.name || '';
    }
  }
  private getGroupByDistinctCount(columnName: string): number | null {
    if (!columnName) {
      return null;
    }

    const profile = this.profileColumns.find(column =>
      column.name.toLowerCase() === columnName.toLowerCase());

    if (!profile) {
      return null;
    }

    return profile.distinctCount;
  }

  private isGroupByAllowed(columnName: string): boolean {
    if (!columnName) {
      return true;
    }

    if (this.profileColumns.length === 0) {
      return true;
    }

    const distinctCount = this.getGroupByDistinctCount(columnName);
    if (distinctCount === null) {
      return false;
    }

    return distinctCount <= this.maxGroupByDistinct;
  }

  private buildRecommendedState(recommendation: ChartRecommendation): RecommendedChartState {
    return {
      aggregation: recommendation.aggregation || 'Sum',
      timeBin: recommendation.timeBin || 'Month',
      metric: recommendation.yColumn || '',
      xColumn: recommendation.xColumn || ''
    };
  }

  private applyRecommendedState(state: RecommendedChartState | null): void {
    this.selectedAggregation = state?.aggregation || 'Sum';
    this.selectedTimeBin = state?.timeBin || 'Month';
    this.selectedXAxis = state?.xColumn || '';
    this.selectedMetric = state?.metric || '';
    this.selectedMetricsY = this.selectedMetric ? [this.selectedMetric] : [];
  }

  private upsertFilterRule(
    column: string,
    operator: string,
    value: string,
    logicalOperator: 'And' | 'Or' = 'And'): 'updated' | 'added' | 'replaced' {
    const existing = this.filterRules.find(rule =>
      rule.column === column &&
      rule.operator === operator);

    if (existing) {
      existing.value = value;
      existing.logicalOperator = logicalOperator;
      return 'updated';
    }

    if (this.filterRules.length < 3) {
      this.filterRules.push({ column, operator, value, logicalOperator });
      return 'added';
    }

    this.filterRules[0] = { column, operator, value, logicalOperator };
    return 'replaced';
  }

  private pushDrilldownTrail(label: string, baseFilters: FilterRule[], activeFilter: FilterRule): void {
    if (!label) {
      return;
    }

    if (this.drilldownTrail.length === 0) {
      this.drilldownBaseFilters = this.cloneFilterRules(baseFilters);
    }

    const currentState = this.cloneFilterRules(this.filterRules);
    const last = this.drilldownTrail[this.drilldownTrail.length - 1];
    if (last && this.areFilterRulesEqual(last.filtersState, currentState)) {
      return;
    }

    this.drilldownTrail.push({
      label,
      filtersState: currentState,
      activeFilter: { ...activeFilter }
    });
  }

  private trimDrilldownTrailToCurrentFilters(): void {
    if (this.drilldownTrail.length === 0) {
      return;
    }

    const currentState = this.cloneFilterRules(this.filterRules);
    let keepCount = 0;
    for (let i = 0; i < this.drilldownTrail.length; i++) {
      if (this.areFilterRulesEqual(this.drilldownTrail[i].filtersState, currentState)) {
        keepCount = i + 1;
      }
    }

    this.drilldownTrail = keepCount > 0 ? this.drilldownTrail.slice(0, keepCount) : [];
    if (this.drilldownTrail.length === 0) {
      this.drilldownBaseFilters = null;
    }
  }

  private clearDrilldownTrail(): void {
    this.drilldownTrail = [];
    this.drilldownBaseFilters = null;
    this.pendingDrilldownRollback = null;
  }

  private snapshotDrilldownState(): {
    filters: FilterRule[];
    trail: DrilldownTrailItem[];
    baseFilters: FilterRule[] | null;
  } {
    return {
      filters: this.cloneFilterRules(this.filterRules),
      trail: this.cloneDrilldownTrail(this.drilldownTrail),
      baseFilters: this.drilldownBaseFilters ? this.cloneFilterRules(this.drilldownBaseFilters) : null
    };
  }

  private restorePendingDrilldownState(): void {
    if (!this.pendingDrilldownRollback) {
      return;
    }

    this.filterRules = this.cloneFilterRules(this.pendingDrilldownRollback.filters);
    this.drilldownTrail = this.cloneDrilldownTrail(this.pendingDrilldownRollback.trail);
    this.drilldownBaseFilters = this.pendingDrilldownRollback.baseFilters
      ? this.cloneFilterRules(this.pendingDrilldownRollback.baseFilters)
      : null;
    this.pendingDrilldownCategory = null;
    this.drilldownLoading = false;
    this.pendingDrilldownRollback = null;
  }

  private isDrilldownTrailInSync(): boolean {
    if (this.drilldownTrail.length === 0) {
      return false;
    }

    const lastState = this.drilldownTrail[this.drilldownTrail.length - 1].filtersState;
    return this.areFilterRulesEqual(lastState, this.filterRules);
  }

  private cloneFilterRules(source: FilterRule[]): FilterRule[] {
    return source.map(rule => ({
      column: rule.column,
      operator: rule.operator,
      value: rule.value,
      logicalOperator: rule.logicalOperator || 'And'
    }));
  }

  private cloneDrilldownTrail(source: DrilldownTrailItem[]): DrilldownTrailItem[] {
    return source.map(item => ({
      label: item.label,
      filtersState: this.cloneFilterRules(item.filtersState),
      activeFilter: { ...item.activeFilter }
    }));
  }

  private areFilterRulesEqual(left: FilterRule[], right: FilterRule[]): boolean {
    if (left.length !== right.length) {
      return false;
    }

    for (let i = 0; i < left.length; i++) {
      const l = left[i];
      const r = right[i];
      if (
        l.column !== r.column ||
        l.operator !== r.operator ||
        l.value !== r.value ||
        (l.logicalOperator || 'And') !== (r.logicalOperator || 'And')) {
        return false;
      }
    }

    return true;
  }

  private formatFilterOperator(operator: string): string {
    switch (operator) {
      case 'Eq':
        return '=';
      case 'NotEq':
        return '!=';
      case 'Gt':
        return '>';
      case 'Gte':
        return '>=';
      case 'Lt':
        return '<';
      case 'Lte':
        return '<=';
      case 'In':
        return 'in';
      case 'Between':
        return 'between';
      case 'Contains':
        return 'contains';
      default:
        return operator;
    }
  }

  private applyChartOptionWithEnhancements(
    baseOption: EChartsOption,
    primaryMetric: string,
    options: ChartLoadOptions,
    loadVersion: number): void {
    const clonedBase = this.cloneOption(baseOption);

    if (this.percentileView !== 'percentile' && this.supportsMultiMetricControl && this.selectedMetricsY.length > 1) {
      const baseAdditional = this.selectedMetricsY.filter(metric => metric !== primaryMetric);
      const additionalMetrics = this.currentBaseChartType === 'Scatter'
        ? baseAdditional
        : baseAdditional.slice(0, 3);

      if (additionalMetrics.length > 0) {
        this.loadAdditionalMetrics(additionalMetrics, options, loadVersion).subscribe(results => {
          if (loadVersion !== this.chartLoadVersion) {
            return;
          }

          const merged = this.mergeMetricOptions(clonedBase, primaryMetric, results);
          this.chartOption = this.applyPresentationTransforms(merged);
          this.buildChartDataGrid(this.chartOption);
          if (this.percentileView === 'base' && this.chartOption) {
            this.baseChartOptionSnapshot = this.cloneOption(this.chartOption);
          }
        });
        return;
      }
    }

    this.chartOption = this.applyPresentationTransforms(clonedBase);
    this.buildChartDataGrid(this.chartOption);
    if (this.percentileView === 'base' && this.chartOption) {
      this.baseChartOptionSnapshot = this.cloneOption(this.chartOption);
    }
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
    this.normalizeTitleAndAxisLayout(mutable);
    this.ensureHistogramInteractivity(mutable);
    this.ensureLegend(mutable);
    this.ensureToolbox(mutable);
    this.ensureZoomWindow(mutable);
    this.ensureGridSpacing(mutable);

    return mutable as EChartsOption;
  }

  private normalizeTitleAndAxisLayout(option: Record<string, unknown>): void {
    const titleMeta = this.normalizeChartTitle(option);
    this.normalizeYAxisNames(option);
    this.normalizeXAxisLabels(option);

    const grid = this.asObject(option['grid']) || {};
    const minimumTopPercent = titleMeta.hasSubtext
      ? 20
      : titleMeta.hasTitle
        ? 16
        : 12;

    const currentTopPercent = this.parsePercentValue(grid['top']);
    if (currentTopPercent === null || currentTopPercent < minimumTopPercent) {
      grid['top'] = `${minimumTopPercent}%`;
    }

    option['grid'] = grid;
  }

  private normalizeChartTitle(option: Record<string, unknown>): { hasTitle: boolean; hasSubtext: boolean } {
    const titleRaw = option['title'];
    const titleItems = Array.isArray(titleRaw)
      ? titleRaw.map(item => this.asObject(item)).filter((item): item is Record<string, unknown> => item !== null)
      : (() => {
          const single = this.asObject(titleRaw);
          return single ? [single] : [];
        })();

    if (titleItems.length === 0) {
      return { hasTitle: false, hasSubtext: false };
    }

    const first = titleItems[0];
    const text = `${first['text'] ?? ''}`.trim();
    const subtext = `${first['subtext'] ?? ''}`.trim();
    const hasTitle = text.length > 0;
    const hasSubtext = subtext.length > 0;

    if (hasTitle || hasSubtext) {
      first['left'] = first['left'] || 'left';
      first['top'] = 6;

      const textStyle = this.asObject(first['textStyle']) || {};
      textStyle['fontSize'] = hasSubtext ? 17 : 16;
      textStyle['fontWeight'] = 600;
      first['textStyle'] = textStyle;

      const subtextStyle = this.asObject(first['subtextStyle']) || {};
      subtextStyle['fontSize'] = 12;
      subtextStyle['lineHeight'] = 16;
      subtextStyle['color'] = '#64748b';
      first['subtextStyle'] = subtextStyle;
    }

    if (Array.isArray(titleRaw)) {
      const result = [...titleRaw];
      result[0] = first;
      option['title'] = result;
    } else {
      option['title'] = first;
    }

    return { hasTitle, hasSubtext };
  }

  private normalizeYAxisNames(option: Record<string, unknown>): void {
    const yAxisRaw = option['yAxis'];
    const normalizeAxis = (axis: Record<string, unknown>, axisCount: number): Record<string, unknown> => {
      if (!axis['name']) {
        return axis;
      }

      const normalizedName = this.normalizeAxisNameLabel(`${axis['name']}`);
      const axisPosition = `${axis['position'] ?? 'left'}`.toLowerCase() === 'right' ? 'right' : 'left';
      const isSingleAxis = axisCount <= 1;

      axis['name'] = isSingleAxis
        ? this.truncateAxisName(normalizedName, 24)
        : this.truncateAxisName(normalizedName, this.isMobile ? 12 : 20);

      if (isSingleAxis) {
        axis['nameLocation'] = 'end';
        axis['nameRotate'] = 0;
        axis['nameGap'] = 12;
      } else {
        // Horizontal names on top avoid overlap with tick labels on dual-axis charts.
        axis['nameLocation'] = 'end';
        axis['nameRotate'] = 0;
        axis['nameGap'] = this.isMobile ? 16 : 18;
      }

      const nameTextStyle = this.asObject(axis['nameTextStyle']) || {};
      nameTextStyle['fontSize'] = 11;
      nameTextStyle['color'] = '#475569';
      if (isSingleAxis) {
        nameTextStyle['align'] = 'right';
        nameTextStyle['verticalAlign'] = 'middle';
      } else {
        nameTextStyle['align'] = axisPosition === 'right' ? 'left' : 'right';
        nameTextStyle['verticalAlign'] = 'bottom';
      }
      axis['nameTextStyle'] = nameTextStyle;

      const axisLabel = this.asObject(axis['axisLabel']) || {};
      axisLabel['hideOverlap'] = true;
      axisLabel['fontSize'] = 11;
       axisLabel['margin'] = Math.max(10, this.tryParseNumeric(axisLabel['margin']) ?? 0);
      axis['axisLabel'] = axisLabel;

      return axis;
    };

    if (Array.isArray(yAxisRaw)) {
      const axisCount = yAxisRaw.length;
      option['yAxis'] = yAxisRaw.map(item => {
        const axis = this.asObject(item);
        if (!axis) {
          return item;
        }

        return normalizeAxis(axis, axisCount);
      });

      return;
    }

    const yAxis = this.asObject(yAxisRaw);
    if (!yAxis) {
      return;
    }

    option['yAxis'] = normalizeAxis(yAxis, 1);
  }

  private ensureGridSpacing(option: Record<string, unknown>): void {
    const yAxisRaw = option['yAxis'];
    const yAxes = Array.isArray(yAxisRaw)
      ? yAxisRaw.map(item => this.asObject(item)).filter((item): item is Record<string, unknown> => item !== null)
      : (() => {
          const single = this.asObject(yAxisRaw);
          return single ? [single] : [];
        })();

    const yAxisCount = yAxes.length;
    const leftColumns = Math.max(1, yAxes.filter(axis => `${axis['position'] ?? 'left'}` !== 'right').length);
    const rightColumns = yAxes.filter(axis => `${axis['position'] ?? 'left'}` === 'right').length;
    const longestLeftName = yAxes
      .filter(axis => `${axis['position'] ?? 'left'}` !== 'right')
      .map(axis => `${axis['name'] ?? ''}`.length)
      .reduce((max, length) => Math.max(max, length), 0);
    const longestRightName = yAxes
      .filter(axis => `${axis['position'] ?? 'left'}` === 'right')
      .map(axis => `${axis['name'] ?? ''}`.length)
      .reduce((max, length) => Math.max(max, length), 0);

    const legendItems = this.readLegendList(option);
    const hasVerticalLegendBelow = legendItems.some(item =>
      `${item['orient'] ?? ''}` === 'vertical' && !!item['bottom']);
    const hasHorizontalLegendBelow = legendItems.some(item =>
      `${item['orient'] ?? ''}` === 'horizontal' && !!item['bottom']);

    const grid = this.asObject(option['grid']) || {};
    grid['containLabel'] = true;
    const leftBase = this.isMobile ? 18 : 15;
    const rightBase = this.isMobile ? 16 : 14;
    const leftNameExtra = yAxisCount > 1 ? Math.min(5, Math.floor(longestLeftName / 6)) : 0;
    const rightNameExtra = yAxisCount > 1 ? Math.min(6, Math.floor(longestRightName / 6)) : 0;

    grid['left'] = `${Math.min(34, leftBase + (leftColumns - 1) * 7 + leftNameExtra)}%`;
    grid['right'] = `${Math.min(38, rightBase + (rightColumns - 1) * 7 + rightNameExtra)}%`;
    grid['top'] = grid['top'] || '12%';
    grid['bottom'] = hasVerticalLegendBelow
      ? '46%'
      : hasHorizontalLegendBelow
        ? '24%'
        : (grid['bottom'] || '16%');
    option['grid'] = grid;
  }

  private normalizeXAxisLabels(option: Record<string, unknown>): void {
    const xAxisRaw = option['xAxis'];
    const normalizeAxis = (axis: Record<string, unknown>): Record<string, unknown> => {
      const axisType = `${axis['type'] ?? ''}`.toLowerCase();
      const axisData = Array.isArray(axis['data']) ? axis['data'] : [];
      const denseLabels = axisData.length > (this.isMobile ? 8 : 14);
      const axisName = `${axis['name'] ?? ''}`.trim();

      const axisLabel = this.asObject(axis['axisLabel']) || {};
      axisLabel['fontSize'] = 11;
      axisLabel['hideOverlap'] = true;
      axisLabel['margin'] = Math.max(10, this.tryParseNumeric(axisLabel['margin']) ?? 0);
      axisLabel['overflow'] = 'truncate';
      axisLabel['ellipsis'] = '…';
      axisLabel['width'] = this.isMobile ? 58 : 82;

      // Keep labels readable when there are many categories.
      if (axisType === 'category' || axisType === '' || axisData.length > 0) {
        axisLabel['rotate'] = denseLabels ? 20 : 0;
        axisLabel['showMaxLabel'] = false;
      }

      axis['axisLabel'] = axisLabel;

      if (axisName.length > 0) {
        axis['name'] = this.normalizeAxisNameLabel(axisName);
        axis['nameLocation'] = 'middle';
        axis['nameGap'] = denseLabels
          ? (this.isMobile ? 38 : 44)
          : (this.isMobile ? 30 : 34);

        const nameTextStyle = this.asObject(axis['nameTextStyle']) || {};
        nameTextStyle['fontSize'] = 11;
        nameTextStyle['color'] = '#475569';
        nameTextStyle['align'] = 'center';
        nameTextStyle['verticalAlign'] = 'top';
        axis['nameTextStyle'] = nameTextStyle;
      }

      const axisTick = this.asObject(axis['axisTick']) || {};
      axisTick['alignWithLabel'] = true;
      axis['axisTick'] = axisTick;

      return axis;
    };

    if (Array.isArray(xAxisRaw)) {
      option['xAxis'] = xAxisRaw.map(item => {
        const axis = this.asObject(item);
        if (!axis) {
          return item;
        }

        return normalizeAxis(axis);
      });
      return;
    }

    const xAxis = this.asObject(xAxisRaw);
    if (!xAxis) {
      return;
    }

    option['xAxis'] = normalizeAxis(xAxis);
  }

  private normalizeAxisNameLabel(rawName: string): string {
    return rawName
      .replace(/_/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  private truncateAxisName(name: string, maxLength: number): string {
    if (name.length <= maxLength) {
      return name;
    }

    return `${name.slice(0, Math.max(0, maxLength - 1)).trimEnd()}…`;
  }

  private parsePercentValue(value: unknown): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    if (typeof value !== 'string') {
      return null;
    }

    const normalized = value.trim().replace('%', '');
    const parsed = Number(normalized);
    return Number.isFinite(parsed) ? parsed : null;
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
          delete series['barMinHeight'];
        } else {
          delete series['smooth'];
          // Keep tiny bars clickable for low-frequency buckets.
          series['barMinHeight'] = 3;
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

  private ensureHistogramInteractivity(option: Record<string, unknown>): void {
    if (this.currentDisplayChartType !== 'Histogram' && this.currentBaseChartType !== 'Histogram') {
      return;
    }

    const seriesList = this.readSeriesList(option);
    for (const series of seriesList) {
      const currentType = `${series['type'] ?? ''}`.toLowerCase();
      if (currentType === 'bar') {
        series['barMinHeight'] = 3;
      }
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

    const orderedNames = this.distinctValues(names).sort((left, right) =>
      left.localeCompare(right, undefined, { sensitivity: 'base' }));

    const minVisibleGroups = 15;
    if (orderedNames.length > minVisibleGroups) {
      const maxColumns = 4;
      const suggestedColumns = Math.ceil(orderedNames.length / minVisibleGroups);
      const columns = Math.max(2, Math.min(maxColumns, suggestedColumns));
      const chunkSize = Math.ceil(orderedNames.length / columns);
      const totalWidthPercent = 90;
      const leftStartPercent = 5;
      const columnWidth = totalWidthPercent / columns;

      const legends: Record<string, unknown>[] = [];
      for (let i = 0; i < columns; i++) {
        const chunk = orderedNames.slice(i * chunkSize, (i + 1) * chunkSize);
        if (chunk.length === 0) {
          continue;
        }

        legends.push({
          show: true,
          type: 'plain',
          orient: 'vertical',
          align: 'left',
          itemWidth: 10,
          itemHeight: 10,
          itemGap: 6,
          left: `${(leftStartPercent + (i * columnWidth)).toFixed(2)}%`,
          width: `${Math.max(12, columnWidth - 1).toFixed(2)}%`,
          top: '64%',
          bottom: '8%',
          textStyle: {
            color: '#334155',
            fontSize: 11
          },
          data: chunk
        });
      }

      option['legend'] = legends;
      return;
    }

    const legend = this.asObject(option['legend']) || {};
    legend['show'] = true;
    legend['type'] = orderedNames.length > 6 ? 'scroll' : 'plain';
    legend['data'] = orderedNames;
    legend['orient'] = 'horizontal';
    legend['left'] = 'center';
    legend['bottom'] = 8;
    legend['itemGap'] = 12;
    legend['pageIconColor'] = '#64748b';
    legend['pageIconInactiveColor'] = '#cbd5e1';
    legend['pageTextStyle'] = { color: '#475569', fontSize: 11 };
    delete legend['top'];
    delete legend['right'];
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

    const legendItems = this.readLegendList(option);
    const hasVerticalLegendBelow = legendItems.some(item =>
      `${item['orient'] ?? ''}` === 'vertical' && !!item['bottom']);
    const hasHorizontalLegendBelow = legendItems.some(item =>
      `${item['orient'] ?? ''}` === 'horizontal' && !!item['bottom']);

    if (hasVerticalLegendBelow || hasHorizontalLegendBelow) {
      for (const zoomItem of dataZoomItems) {
        if (`${zoomItem['type'] ?? ''}`.toLowerCase() === 'slider') {
          zoomItem['bottom'] = hasVerticalLegendBelow ? '38%' : '16%';
        }
      }
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

  onDataZoom(event: unknown): void {
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

  onChartClick(event: unknown): void {
    const payload = event as Record<string, unknown>;
    const componentType = `${payload['componentType'] ?? ''}`;
    if (componentType !== 'series' && componentType !== 'xAxis') {
      return;
    }

    if (componentType === 'xAxis' && this.supportsDrilldown) {
      const category = `${payload['value'] ?? payload['name'] ?? ''}`.trim();
      if (category) {
        this.lastSeriesDrilldownAt = Date.now();
        this.pendingDrilldownCategory = category;
        this.applyDrilldown(category);
      }
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
        this.lastSeriesDrilldownAt = Date.now();
        this.pendingDrilldownCategory = category;
        this.applyDrilldown(category);
        return;
      }
    }

    this.toast.info('Ponto selecionado. Use "Dados do ponto" para inspecionar os registros.');
  }

  private onChartCanvasClick(event: unknown): void {
    if (!this.supportsDrilldown || !this.echartsInstance || !this.chartOption || this.drilldownLoading) {
      return;
    }

    // Skip if this click was already handled by the series click handler.
    if (Date.now() - this.lastSeriesDrilldownAt < 180) {
      return;
    }

    const payload = event as Record<string, unknown>;
    const offsetX = this.tryParseNumeric(payload['offsetX']) ??
      this.tryParseNumeric((payload['event'] as Record<string, unknown> | undefined)?.['offsetX']);
    const offsetY = this.tryParseNumeric(payload['offsetY']) ??
      this.tryParseNumeric((payload['event'] as Record<string, unknown> | undefined)?.['offsetY']);

    if (offsetX === null || offsetY === null) {
      return;
    }

    const instance = this.echartsInstance as unknown as {
      containPixel?: (finder: Record<string, unknown>, value: [number, number]) => boolean;
      convertFromPixel?: (finder: Record<string, unknown>, value: [number, number]) => unknown;
    };

    if (instance.containPixel && !instance.containPixel({ gridIndex: 0 }, [offsetX, offsetY])) {
      return;
    }

    let axisValue: unknown = null;
    try {
      axisValue = instance.convertFromPixel
        ? instance.convertFromPixel({ xAxisIndex: 0 }, [offsetX, offsetY])
        : null;
    } catch {
      axisValue = null;
    }

    const categories = this.readAxisData(this.chartOption as Record<string, unknown>, 'xAxis');
    if (categories.length === 0) {
      return;
    }

    const axisCandidate = Array.isArray(axisValue) ? axisValue[0] : axisValue;

    let category = '';
    if (typeof axisCandidate === 'string') {
      category = axisCandidate.trim();
    } else {
      const rawIndex = this.tryParseNumeric(axisCandidate);
      if (rawIndex === null) {
        return;
      }

      const index = Math.round(rawIndex);
      if (index < 0 || index >= categories.length) {
        return;
      }

      category = `${this.toScalar(categories[index]) ?? ''}`.trim();
    }

    if (!category) {
      return;
    }

    this.pendingDrilldownCategory = category;
    this.applyDrilldown(category);
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

  private readLegendList(option: Record<string, unknown>): Record<string, unknown>[] {
    const raw = option['legend'];
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

  private parseHistogramRangeLabel(label: string): { min: number; max: number } | null {
    if (!label) {
      return null;
    }

    const normalized = label
      .replace('[', '')
      .replace(']', '')
      .replace('(', '')
      .replace(')', '')
      .trim();

    if (!normalized) {
      return null;
    }

    // Histogram labels come as "[start, end)".
    // We split by comma+space to avoid collisions with decimal/group separators.
    let parts = normalized.split(', ');
    if (parts.length !== 2) {
      parts = normalized.split(' - ');
    }
    if (parts.length !== 2) {
      return null;
    }

    const first = this.parseFlexibleNumber(parts[0]);
    const second = this.parseFlexibleNumber(parts[1]);

    if (first === null || second === null) {
      return null;
    }

    const min = Math.min(first, second);
    const max = Math.max(first, second);
    return { min, max };
  }

  private parseFlexibleNumber(raw: string): number | null {
    const compact = raw.trim().replace(/\s+/g, '');
    if (!compact) {
      return null;
    }

    let normalized = compact;
    const hasComma = compact.includes(',');
    const hasDot = compact.includes('.');

    if (hasComma && hasDot) {
      const lastComma = compact.lastIndexOf(',');
      const lastDot = compact.lastIndexOf('.');

      // 1.234,56 -> decimal comma
      if (lastComma > lastDot) {
        normalized = compact.replace(/\./g, '').replace(',', '.');
      } else {
        // 1,234.56 -> decimal dot
        normalized = compact.replace(/,/g, '');
      }
    } else if (hasComma) {
      // 123,45 -> decimal comma
      normalized = compact.replace(',', '.');
    } else {
      // 1234.56 -> decimal dot
      normalized = compact;
    }

    const parsed = Number(normalized);
    return Number.isFinite(parsed) ? parsed : null;
  }

  private toInvariantNumber(value: number): string {
    return Number(value.toFixed(8)).toString();
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

  private syncSelectedXAxisWithAvailability(): void {
    if (!this.supportsScatterXAxisControl) {
      return;
    }

    const allowed = new Set(this.availableMetrics);
    if (this.selectedXAxis && allowed.has(this.selectedXAxis)) {
      return;
    }

    const recommendedX = this.currentRecommendation?.xColumn || '';
    if (recommendedX && allowed.has(recommendedX)) {
      this.selectedXAxis = recommendedX;
      return;
    }

    if (this.availableMetrics.length > 0) {
      this.selectedXAxis = this.availableMetrics[0];
    }
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
