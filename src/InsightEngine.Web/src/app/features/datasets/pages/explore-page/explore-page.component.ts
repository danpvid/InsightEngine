import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs/operators';
import { NgxEchartsModule } from 'ngx-echarts';
import { EChartsOption } from 'echarts';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { TranslatePipe } from '../../../../core/pipes/translate.pipe';
import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { MetadataIndexApiService } from '../../../../core/services/metadata-index-api.service';
import { DatasetApiService } from '../../../../core/services/dataset-api.service';
import { ToastService } from '../../../../core/services/toast.service';
import { LanguageService } from '../../../../core/services/language.service';
import { HttpErrorUtil } from '../../../../core/util/http-error.util';
import {
  ColumnIndex,
  CorrelationEdge,
  DatasetIndex,
  DatasetIndexStatus,
  InferredType,
  IndexBuildState
} from '../../../../core/models/metadata-index.model';
import { RawDatasetRow } from '../../../../core/models/dataset.model';

interface ExploreSavedView {
  id: string;
  name: string;
  createdAtUtc: string;
  state: {
    selectedTabIndex: number;
    selectedFieldName: string;
    selectedTypeFilters: string[];
    fieldSearch: string;
    activeFilters: string[];
    pinnedFieldNames: string[];
    correlationSearch: string;
    correlationMethodFilter: string;
    correlationStrengthFilter: string;
    selectedDistributionFields: string[];
    selectedDistributionDateField: string;
    gridQuickFilter: string;
    gridHiddenColumns: string[];
    gridSortColumn: string;
    gridSortDirection: 'asc' | 'desc';
    gridBackendFilters: string[];
  };
}

@Component({
  selector: 'app-explore-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    NgxEchartsModule,
    TranslatePipe,
    ...MATERIAL_MODULES,
    PageHeaderComponent
  ],
  templateUrl: './explore-page.component.html',
  styleUrls: ['./explore-page.component.scss']
})
export class ExplorePageComponent implements OnInit {
  datasetId: string = '';
  datasetName: string = '';
  index: DatasetIndex | null = null;
  indexStatus: DatasetIndexStatus | null = null;
  loadingIndex: boolean = false;
  rebuilding: boolean = false;
  statusClass: 'status-ready' | 'status-building' | 'status-failed' | 'status-idle' = 'status-idle';
  selectedTabIndex: number = 0;
  fieldSearch: string = '';
  selectedTypeFilters: string[] = [];
  selectedFieldName: string = '';
  selectedCorrelationKey: string = '';
  correlationSearch: string = '';
  correlationMethodFilter: string = 'All';
  correlationStrengthFilter: string = 'All';
  selectedDistributionFields: string[] = [];
  selectedDistributionDateField: string = '';
  gridLoading: boolean = false;
  gridColumns: string[] = [];
  gridRows: RawDatasetRow[] = [];
  gridQuickFilter: string = '';
  gridHiddenColumns: string[] = [];
  gridSortColumn: string = '';
  gridSortDirection: 'asc' | 'desc' = 'asc';
  gridBackendFilters: string[] = [];
  gridTotalRows: number = 0;
  gridPageSize: number = 200;
  savedViews: ExploreSavedView[] = [];
  selectedSavedViewId: string = '';
  activeFilters: string[] = [];
  pinnedFieldNames: string[] = [];

  readonly availableTypeFilters: string[] = ['Number', 'Date', 'String', 'Category', 'Boolean'];
  readonly correlationMethods: string[] = ['All', 'Pearson', 'Spearman', 'CramersV', 'EtaSquared', 'MutualInformation'];
  readonly correlationStrengths: string[] = ['All', 'High', 'Medium', 'Low'];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private metadataIndexApi: MetadataIndexApiService,
    private datasetApi: DatasetApiService,
    private toast: ToastService,
    private languageService: LanguageService
  ) {}

  ngOnInit(): void {
    this.datasetId = this.route.snapshot.paramMap.get('datasetId') || '';
    this.loadDatasetName();
    this.loadSavedViews();
    this.loadIndex();
  }

  get recommendationsLink(): string[] {
    return ['../recommendations'];
  }

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

  get displayedFields(): ColumnIndex[] {
    if (!this.index) {
      return [];
    }

    const term = this.fieldSearch.trim().toLowerCase();

    return this.index.columns.filter(column => {
      const typeMatch = this.selectedTypeFilters.length === 0 || this.selectedTypeFilters.includes(column.inferredType);
      const termMatch = term.length === 0
        || column.name.toLowerCase().includes(term)
        || column.semanticTags.some(tag => tag.toLowerCase().includes(term));

      return typeMatch && termMatch;
    });
  }

  get selectedField(): ColumnIndex | null {
    if (!this.index || !this.selectedFieldName) {
      return null;
    }

    return this.index.columns.find(column => column.name === this.selectedFieldName) || null;
  }

  get pinnedFields(): ColumnIndex[] {
    if (!this.index || this.pinnedFieldNames.length === 0) {
      return [];
    }

    return this.pinnedFieldNames
      .map(name => this.index!.columns.find(column => column.name === name) || null)
      .filter((column): column is ColumnIndex => column !== null);
  }

  get filteredCorrelationEdges(): CorrelationEdge[] {
    if (!this.index) {
      return [];
    }

    const search = this.correlationSearch.trim().toLowerCase();
    return this.index.correlations.edges.filter(edge => {
      const methodMatch = this.correlationMethodFilter === 'All' || edge.method === this.correlationMethodFilter;
      const strengthMatch = this.correlationStrengthFilter === 'All' || edge.strength === this.correlationStrengthFilter;
      const searchMatch = search.length === 0
        || edge.leftColumn.toLowerCase().includes(search)
        || edge.rightColumn.toLowerCase().includes(search);

      return methodMatch && strengthMatch && searchMatch;
    });
  }

  get numericFields(): ColumnIndex[] {
    return (this.index?.columns || []).filter(column => column.inferredType === 'Number' && !!column.numericStats);
  }

  get dateFields(): ColumnIndex[] {
    return (this.index?.columns || []).filter(column => column.inferredType === 'Date' && !!column.dateStats);
  }

  get distributionSeries(): Array<{ field: ColumnIndex; option: EChartsOption }> {
    if (!this.index) {
      return [];
    }

    return this.selectedDistributionFields
      .map(fieldName => this.index!.columns.find(column => column.name === fieldName) || null)
      .filter((field): field is ColumnIndex => !!field?.numericStats?.histogram?.length)
      .map(field => ({
        field,
        option: this.buildDistributionOption(field)
      }));
  }

  get selectedDateDistributionOption(): EChartsOption | null {
    if (!this.index || !this.selectedDistributionDateField) {
      return null;
    }

    const field = this.index.columns.find(column => column.name === this.selectedDistributionDateField);
    if (!field?.dateStats?.coverage || field.dateStats.coverage.length === 0) {
      return null;
    }

    return {
      tooltip: {
        trigger: 'axis'
      },
      grid: {
        left: 40,
        right: 14,
        top: 18,
        bottom: 34
      },
      xAxis: {
        type: 'category',
        axisLabel: { rotate: 30 },
        data: field.dateStats.coverage.map(item => new Date(item.start).toLocaleDateString())
      },
      yAxis: {
        type: 'value'
      },
      series: [
        {
          type: 'line',
          data: field.dateStats.coverage.map(item => item.count),
          smooth: true,
          areaStyle: {
            color: 'rgba(59, 130, 246, 0.12)'
          },
          lineStyle: {
            color: '#2563eb'
          },
          itemStyle: {
            color: '#2563eb'
          }
        }
      ]
    };
  }

  get visibleGridColumns(): string[] {
    return this.gridColumns.filter(column => !this.gridHiddenColumns.includes(column));
  }

  get displayedGridRows(): RawDatasetRow[] {
    let rows = [...this.gridRows];

    const quickFilter = this.gridQuickFilter.trim().toLowerCase();
    if (quickFilter.length > 0) {
      rows = rows.filter(row => this.visibleGridColumns.some(column => {
        const value = row[column];
        return (value || '').toString().toLowerCase().includes(quickFilter);
      }));
    }

    if (this.gridSortColumn) {
      rows.sort((left, right) => {
        const leftValue = (left[this.gridSortColumn] || '').toString();
        const rightValue = (right[this.gridSortColumn] || '').toString();

        const comparison = leftValue.localeCompare(rightValue, undefined, { numeric: true, sensitivity: 'base' });
        return this.gridSortDirection === 'asc' ? comparison : -comparison;
      });
    }

    return rows;
  }

  get selectedCorrelation(): CorrelationEdge | null {
    if (!this.selectedCorrelationKey) {
      return null;
    }

    return this.filteredCorrelationEdges.find(edge => this.buildCorrelationKey(edge) === this.selectedCorrelationKey) || null;
  }

  get matrixColumns(): string[] {
    const ordered = this.filteredCorrelationEdges
      .sort((left, right) => Math.abs(right.score) - Math.abs(left.score))
      .slice(0, 60)
      .flatMap(edge => [edge.leftColumn, edge.rightColumn]);

    return [...new Set(ordered)].slice(0, 12);
  }

  get fieldHistogramOption(): EChartsOption | null {
    const field = this.selectedField;
    const histogram = field?.numericStats?.histogram ?? [];
    if (!field || histogram.length === 0) {
      return null;
    }

    return {
      tooltip: {
        trigger: 'axis'
      },
      grid: {
        left: 40,
        right: 14,
        top: 18,
        bottom: 28
      },
      xAxis: {
        type: 'category',
        axisLabel: { show: false },
        data: histogram.map((_, index) => `B${index + 1}`)
      },
      yAxis: {
        type: 'value'
      },
      series: [
        {
          type: 'bar',
          data: histogram.map(bin => bin.count),
          itemStyle: {
            color: '#2563eb'
          }
        }
      ]
    };
  }

  get dateCoverageOption(): EChartsOption | null {
    const field = this.selectedField;
    const coverage = field?.dateStats?.coverage ?? [];
    if (!field || coverage.length === 0) {
      return null;
    }

    return {
      tooltip: {
        trigger: 'axis'
      },
      grid: {
        left: 40,
        right: 14,
        top: 18,
        bottom: 34
      },
      xAxis: {
        type: 'category',
        axisLabel: { rotate: 30 },
        data: coverage.map(bin => new Date(bin.start).toLocaleDateString())
      },
      yAxis: {
        type: 'value'
      },
      series: [
        {
          type: 'line',
          data: coverage.map(bin => bin.count),
          smooth: true,
          symbolSize: 6,
          lineStyle: { width: 2, color: '#0d9488' },
          itemStyle: { color: '#0d9488' }
        }
      ]
    };
  }

  get statusLabelKey(): string {
    const status = this.normalizeStatus(this.indexStatus?.status);
    if (status === 'ready') return 'explore.statusReady';
    if (status === 'building') return 'explore.statusBuilding';
    if (status === 'failed') return 'explore.statusFailed';
    return 'explore.statusNotBuilt';
  }

  get hasIndexData(): boolean {
    return !!this.index && this.index.columns.length > 0;
  }

  get topNullRateFields(): ColumnIndex[] {
    if (!this.index) {
      return [];
    }

    return [...this.index.columns]
      .sort((left, right) => right.nullRate - left.nullRate)
      .slice(0, 5);
  }

  get topVarianceFields(): ColumnIndex[] {
    if (!this.index) {
      return [];
    }

    return this.index.columns
      .filter(column => column.numericStats?.stdDev != null)
      .sort((left, right) => (right.numericStats?.stdDev || 0) - (left.numericStats?.stdDev || 0))
      .slice(0, 5);
  }

  get topCardinalityFields(): ColumnIndex[] {
    if (!this.index) {
      return [];
    }

    return this.index.columns
      .filter(column => column.inferredType === 'Category' || column.inferredType === 'String')
      .sort((left, right) => right.distinctCount - left.distinctCount)
      .slice(0, 5);
  }

  get suggestedNextSteps(): string[] {
    if (!this.index) {
      return [];
    }

    const suggestions: string[] = [];
    const hasStrongCorrelations = this.index.correlations.edges.some(edge => Math.abs(edge.score) >= 0.7);
    const hasHighNullRate = this.topNullRateFields.some(column => column.nullRate >= 0.2);
    const hasDateTag = this.index.tags.some(tag => tag.name.toLowerCase().includes('time'));
    const hasPotentialKey = this.index.candidateKeys.some(candidate => candidate.uniquenessRatio >= 0.9);

    if (hasStrongCorrelations) {
      suggestions.push('explore.nextStepCorrelation');
    }

    if (hasDateTag) {
      suggestions.push('explore.nextStepTimeSeries');
    }

    if (hasHighNullRate) {
      suggestions.push('explore.nextStepMissing');
    }

    if (hasPotentialKey) {
      suggestions.push('explore.nextStepKeys');
    }

    if (suggestions.length === 0) {
      suggestions.push('explore.nextStepFallback');
    }

    return suggestions;
  }

  onToggleTypeFilter(type: string): void {
    if (this.selectedTypeFilters.includes(type)) {
      this.selectedTypeFilters = this.selectedTypeFilters.filter(item => item !== type);
      return;
    }

    this.selectedTypeFilters = [...this.selectedTypeFilters, type];
  }

  onSelectField(column: ColumnIndex): void {
    this.selectedFieldName = column.name;
    if (this.selectedTabIndex !== 1) {
      this.selectedTabIndex = 1;
    }
  }

  onSelectCorrelation(edge: CorrelationEdge): void {
    this.selectedCorrelationKey = this.buildCorrelationKey(edge);
  }

  onToggleDistributionField(fieldName: string, checked: boolean): void {
    if (!checked) {
      this.selectedDistributionFields = this.selectedDistributionFields.filter(name => name !== fieldName);
      return;
    }

    if (this.selectedDistributionFields.length >= 4) {
      this.toast.info('You can select up to 4 numeric fields.');
      return;
    }

    this.selectedDistributionFields = [...this.selectedDistributionFields, fieldName];
  }

  onTabChanged(index: number): void {
    this.selectedTabIndex = index;
    if (index === 4 && this.gridRows.length === 0 && !this.gridLoading) {
      this.loadGridData();
    }
  }

  loadGridData(): void {
    if (!this.datasetId) {
      return;
    }

    this.gridLoading = true;
    this.datasetApi.getRawRows(this.datasetId, {
      page: 1,
      pageSize: this.gridPageSize,
      filters: this.gridBackendFilters
    }).pipe(
      finalize(() => {
        this.gridLoading = false;
      })
    ).subscribe({
      next: response => {
        if (!response.success || !response.data) {
          this.gridRows = [];
          this.gridColumns = [];
          this.gridTotalRows = 0;
          return;
        }

        this.gridRows = response.data.rows || [];
        this.gridColumns = response.data.columns || [];
        this.gridTotalRows = response.data.rowCountTotal || 0;
      },
      error: err => {
        this.toast.error(HttpErrorUtil.extractErrorMessage(err));
      }
    });
  }

  toggleGridColumn(column: string): void {
    if (this.gridHiddenColumns.includes(column)) {
      this.gridHiddenColumns = this.gridHiddenColumns.filter(item => item !== column);
      return;
    }

    this.gridHiddenColumns = [...this.gridHiddenColumns, column];
  }

  sortGridBy(column: string): void {
    if (this.gridSortColumn === column) {
      this.gridSortDirection = this.gridSortDirection === 'asc' ? 'desc' : 'asc';
      return;
    }

    this.gridSortColumn = column;
    this.gridSortDirection = 'asc';
  }

  addGridCellFilter(column: string, value: string | null, exclude: boolean): void {
    if (!value) {
      return;
    }

    const op = exclude ? 'neq' : 'eq';
    const token = `${column}|${op}|${value}`;
    if (this.gridBackendFilters.includes(token)) {
      return;
    }

    this.gridBackendFilters = [...this.gridBackendFilters, token];
    this.loadGridData();
  }

  removeGridFilter(token: string): void {
    this.gridBackendFilters = this.gridBackendFilters.filter(item => item !== token);
    this.loadGridData();
  }

  onSavedViewSelected(): void {
    const view = this.savedViews.find(item => item.id === this.selectedSavedViewId);
    if (!view) {
      return;
    }

    this.applySavedView(view);
    this.toast.success('Saved view loaded.');
  }

  saveCurrentView(): void {
    const defaultName = `View ${this.savedViews.length + 1}`;
    const typedName = (window.prompt('Saved view name', defaultName) || '').trim();
    if (!typedName) {
      return;
    }

    const savedView: ExploreSavedView = {
      id: crypto.randomUUID(),
      name: typedName,
      createdAtUtc: new Date().toISOString(),
      state: this.captureCurrentState()
    };

    this.savedViews = [savedView, ...this.savedViews];
    this.selectedSavedViewId = savedView.id;
    this.persistSavedViews();
    this.toast.success('Saved view created.');
  }

  renameSelectedView(): void {
    const view = this.savedViews.find(item => item.id === this.selectedSavedViewId);
    if (!view) {
      return;
    }

    const typedName = (window.prompt('Rename saved view', view.name) || '').trim();
    if (!typedName) {
      return;
    }

    this.savedViews = this.savedViews.map(item => item.id === view.id ? { ...item, name: typedName } : item);
    this.persistSavedViews();
    this.toast.success('Saved view renamed.');
  }

  deleteSelectedView(): void {
    const view = this.savedViews.find(item => item.id === this.selectedSavedViewId);
    if (!view) {
      return;
    }

    if (!window.confirm(`Delete saved view "${view.name}"?`)) {
      return;
    }

    this.savedViews = this.savedViews.filter(item => item.id !== view.id);
    this.selectedSavedViewId = '';
    this.persistSavedViews();
    this.toast.success('Saved view deleted.');
  }

  useCorrelationInChart(): void {
    const correlation = this.selectedCorrelation;
    if (!correlation) {
      return;
    }

    this.router.navigate(
      ['/', this.currentLanguage, 'datasets', this.datasetId, 'recommendations'],
      { queryParams: { correlationPair: `${correlation.leftColumn},${correlation.rightColumn}` } }
    );
  }

  addCorrelationComparison(): void {
    const correlation = this.selectedCorrelation;
    if (!correlation) {
      return;
    }

    this.toast.info(`Comparison queued: ${correlation.leftColumn} vs ${correlation.rightColumn}`);
  }

  pinSelectedField(): void {
    const field = this.selectedField;
    if (!field) {
      return;
    }

    if (this.pinnedFieldNames.includes(field.name)) {
      this.toast.info('Field already pinned.');
      return;
    }

    this.pinnedFieldNames = [...this.pinnedFieldNames, field.name];
    this.toast.success('Field pinned to quick access.');
  }

  unpinField(fieldName: string): void {
    this.pinnedFieldNames = this.pinnedFieldNames.filter(name => name !== fieldName);
  }

  addFieldFilter(): void {
    const field = this.selectedField;
    if (!field) {
      return;
    }

    const firstValue = field.topValues[0];
    const filter = firstValue
      ? `${field.name} = ${firstValue}`
      : `${field.name} IS NOT NULL`;

    if (this.activeFilters.includes(filter)) {
      this.toast.info('Filter already present.');
      return;
    }

    this.activeFilters = [...this.activeFilters, filter];
    this.toast.success('Filter added to query bar.');
  }

  removeFilter(filter: string): void {
    this.activeFilters = this.activeFilters.filter(item => item !== filter);
  }

  useFieldInChart(): void {
    const field = this.selectedField;
    if (!field) {
      return;
    }

    this.router.navigate(
      ['/', this.currentLanguage, 'datasets', this.datasetId, 'recommendations'],
      { queryParams: { focusField: field.name } }
    );
  }

  rebuildIndex(): void {
    if (!this.datasetId || this.rebuilding) {
      return;
    }

    this.rebuilding = true;
    this.metadataIndexApi.buildIndex(this.datasetId).pipe(
      finalize(() => {
        this.rebuilding = false;
      })
    ).subscribe({
      next: () => {
        this.toast.success('Metadata index rebuild started.');
        this.loadIndex(true);
      },
      error: (err) => {
        this.toast.error(HttpErrorUtil.extractErrorMessage(err));
      }
    });
  }

  exportIndexJson(): void {
    if (!this.index) {
      return;
    }

    const fileName = `${this.datasetId}-index.json`;
    const blob = new Blob([JSON.stringify(this.index, null, 2)], { type: 'application/json;charset=utf-8' });
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(objectUrl);
  }

  getNullRateWidth(column: ColumnIndex): string {
    const width = Math.max(0, Math.min(100, Math.round(column.nullRate * 100)));
    return `${width}%`;
  }

  formatPercent(value: number): string {
    return `${(value * 100).toFixed(2)}%`;
  }

  formatCorrelationScore(value: number): string {
    return value.toFixed(3);
  }

  getTagReason(tag: string): string {
    const normalizedTag = tag.toLowerCase();
    if (normalizedTag === 'identifier') return 'explore.tagReasonIdentifier';
    if (normalizedTag === 'timestamp') return 'explore.tagReasonTimestamp';
    if (normalizedTag === 'amount') return 'explore.tagReasonAmount';
    if (normalizedTag === 'rate') return 'explore.tagReasonRate';
    if (normalizedTag === 'category') return 'explore.tagReasonCategory';
    if (normalizedTag === 'freetext') return 'explore.tagReasonFreeText';
    return 'explore.tagReasonDefault';
  }

  getMatrixScore(row: string, column: string): number | null {
    if (row === column) {
      return 1;
    }

    const edge = this.filteredCorrelationEdges.find(item =>
      (item.leftColumn === row && item.rightColumn === column)
      || (item.leftColumn === column && item.rightColumn === row)
    );

    return edge ? edge.score : null;
  }

  getMatrixCellClass(score: number | null): string {
    if (score == null) {
      return 'matrix-empty';
    }

    const abs = Math.abs(score);
    if (abs >= 0.7) {
      return score >= 0 ? 'matrix-strong-positive' : 'matrix-strong-negative';
    }

    if (abs >= 0.3) {
      return score >= 0 ? 'matrix-medium-positive' : 'matrix-medium-negative';
    }

    return 'matrix-weak';
  }

  getInferredTypeIcon(type: InferredType): string {
    switch (type) {
      case 'Number':
        return 'tag';
      case 'Date':
        return 'event';
      case 'Boolean':
        return 'toggle_on';
      case 'Category':
        return 'category';
      default:
        return 'text_fields';
    }
  }

  private loadDatasetName(): void {
    if (!this.datasetId) {
      return;
    }

    this.datasetApi.listDatasets().subscribe({
      next: response => {
        if (!response.success || !response.data) {
          return;
        }

        const dataset = response.data.find(item => item.datasetId.toLowerCase() === this.datasetId.toLowerCase());
        this.datasetName = dataset?.originalFileName || '';
      }
    });
  }

  private loadIndex(forceRefresh: boolean = false): void {
    if (!this.datasetId) {
      return;
    }

    this.loadingIndex = true;
    this.metadataIndexApi.getIndexStatus(this.datasetId, forceRefresh).subscribe({
      next: response => {
        if (response.success && response.data) {
          this.indexStatus = response.data;
          this.syncStatusClass(response.data.status);
        }
      }
    });

    this.metadataIndexApi.getIndex(this.datasetId, forceRefresh).pipe(
      finalize(() => {
        this.loadingIndex = false;
      })
    ).subscribe({
      next: response => {
        if (!response.success || !response.data) {
          this.index = null;
          return;
        }

        this.index = response.data;
        this.ensureFieldSelection();
        if (this.selectedTabIndex === 4 && this.gridRows.length === 0) {
          this.loadGridData();
        }
      },
      error: () => {
        this.index = null;
      }
    });
  }

  private ensureFieldSelection(): void {
    if (!this.index) {
      this.selectedFieldName = '';
      return;
    }

    if (!this.selectedFieldName || !this.index.columns.some(column => column.name === this.selectedFieldName)) {
      this.selectedFieldName = this.index.columns[0]?.name ?? '';
    }
  }

  private syncStatusClass(status: IndexBuildState): void {
    switch (this.normalizeStatus(status)) {
      case 'ready':
        this.statusClass = 'status-ready';
        return;
      case 'building':
        this.statusClass = 'status-building';
        return;
      case 'failed':
        this.statusClass = 'status-failed';
        return;
      default:
        this.statusClass = 'status-idle';
    }
  }

  private normalizeStatus(status: string | undefined | null): string {
    return (status || '').toLowerCase();
  }

  private loadSavedViews(): void {
    if (!this.datasetId) {
      return;
    }

    try {
      const raw = localStorage.getItem(this.buildSavedViewsStorageKey());
      if (!raw) {
        this.savedViews = [];
        return;
      }

      const parsed = JSON.parse(raw) as ExploreSavedView[];
      this.savedViews = Array.isArray(parsed) ? parsed : [];
    } catch {
      this.savedViews = [];
    }
  }

  private persistSavedViews(): void {
    if (!this.datasetId) {
      return;
    }

    localStorage.setItem(this.buildSavedViewsStorageKey(), JSON.stringify(this.savedViews));
  }

  private buildSavedViewsStorageKey(): string {
    return `insightengine:explore:saved-views:${this.datasetId}`;
  }

  private captureCurrentState(): ExploreSavedView['state'] {
    return {
      selectedTabIndex: this.selectedTabIndex,
      selectedFieldName: this.selectedFieldName,
      selectedTypeFilters: [...this.selectedTypeFilters],
      fieldSearch: this.fieldSearch,
      activeFilters: [...this.activeFilters],
      pinnedFieldNames: [...this.pinnedFieldNames],
      correlationSearch: this.correlationSearch,
      correlationMethodFilter: this.correlationMethodFilter,
      correlationStrengthFilter: this.correlationStrengthFilter,
      selectedDistributionFields: [...this.selectedDistributionFields],
      selectedDistributionDateField: this.selectedDistributionDateField,
      gridQuickFilter: this.gridQuickFilter,
      gridHiddenColumns: [...this.gridHiddenColumns],
      gridSortColumn: this.gridSortColumn,
      gridSortDirection: this.gridSortDirection,
      gridBackendFilters: [...this.gridBackendFilters]
    };
  }

  private applySavedView(view: ExploreSavedView): void {
    const state = view.state;
    this.selectedTabIndex = state.selectedTabIndex;
    this.selectedFieldName = state.selectedFieldName;
    this.selectedTypeFilters = [...state.selectedTypeFilters];
    this.fieldSearch = state.fieldSearch;
    this.activeFilters = [...state.activeFilters];
    this.pinnedFieldNames = [...state.pinnedFieldNames];
    this.correlationSearch = state.correlationSearch;
    this.correlationMethodFilter = state.correlationMethodFilter;
    this.correlationStrengthFilter = state.correlationStrengthFilter;
    this.selectedDistributionFields = [...state.selectedDistributionFields];
    this.selectedDistributionDateField = state.selectedDistributionDateField;
    this.gridQuickFilter = state.gridQuickFilter;
    this.gridHiddenColumns = [...state.gridHiddenColumns];
    this.gridSortColumn = state.gridSortColumn;
    this.gridSortDirection = state.gridSortDirection;
    this.gridBackendFilters = [...state.gridBackendFilters];

    if (this.selectedTabIndex === 4) {
      this.loadGridData();
    }
  }

  buildCorrelationKey(edge: CorrelationEdge): string {
    return `${edge.leftColumn}|${edge.rightColumn}|${edge.method}`;
  }

  private buildDistributionOption(field: ColumnIndex): EChartsOption {
    const histogram = field.numericStats?.histogram ?? [];
    const stats = field.numericStats;

    return {
      tooltip: {
        trigger: 'axis'
      },
      grid: {
        left: 40,
        right: 14,
        top: 18,
        bottom: 28
      },
      xAxis: {
        type: 'category',
        axisLabel: { show: false },
        data: histogram.map((_, index) => `${index + 1}`)
      },
      yAxis: {
        type: 'value'
      },
      series: [
        {
          type: 'bar',
          data: histogram.map(bin => bin.count),
          itemStyle: {
            color: '#1d4ed8'
          },
          markLine: {
            symbol: 'none',
            label: { show: false },
            data: [
              { xAxis: stats?.p5 != null ? this.findHistogramIndex(histogram, stats.p5) : undefined },
              { xAxis: stats?.p10 != null ? this.findHistogramIndex(histogram, stats.p10) : undefined },
              { xAxis: stats?.p50 != null ? this.findHistogramIndex(histogram, stats.p50) : undefined },
              { xAxis: stats?.p90 != null ? this.findHistogramIndex(histogram, stats.p90) : undefined },
              { xAxis: stats?.p95 != null ? this.findHistogramIndex(histogram, stats.p95) : undefined }
            ].filter(item => item.xAxis != null)
          }
        }
      ]
    };
  }

  private findHistogramIndex(
    histogram: Array<{ lowerBound: number; upperBound: number }>,
    value: number
  ): number {
    const index = histogram.findIndex(bin => value >= bin.lowerBound && value <= bin.upperBound);
    return index >= 0 ? index : 0;
  }
}
