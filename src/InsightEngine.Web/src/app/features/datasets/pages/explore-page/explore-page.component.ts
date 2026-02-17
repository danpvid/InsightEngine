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

  buildCorrelationKey(edge: CorrelationEdge): string {
    return `${edge.leftColumn}|${edge.rightColumn}|${edge.method}`;
  }
}
