import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NgxEchartsModule } from 'ngx-echarts';
import { MatCardModule } from '@angular/material/card';
import { ChartRecommendation } from '../../core/models/recommendation.model';
import { DatasetApiService } from '../../core/services/dataset-api.service';
import { formatCompactNumber } from '../../shared/format/compact-number';

type ChartLayout = 'hero' | 'secondary';

interface ChartCardState {
  id: string;
  title: string;
  reason?: string;
  option: any | null;
  loading: boolean;
  error: string | null;
  layout: ChartLayout;
}

@Component({
  selector: 'app-charts-grid',
  standalone: true,
  imports: [CommonModule, RouterLink, NgxEchartsModule, MatCardModule],
  templateUrl: './charts-grid.component.html',
  styleUrls: ['./charts-grid.component.scss']
})
export class ChartsGridComponent implements OnChanges {
  @Input() datasetId: string | null = null;
  @Input() lang = 'pt-br';
  @Input() heroChart: ChartRecommendation | null = null;
  @Input() secondaryCharts: ChartRecommendation[] | null = [];
  @Input() charts: ChartRecommendation[] | null = [];
  @Input() maxSeries = 3;
  @Input() maxLegendItems = 5;

  cards: ChartCardState[] = [];

  constructor(private readonly datasetApi: DatasetApiService) {}

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['datasetId'] || changes['heroChart'] || changes['secondaryCharts'] || changes['charts']) && this.datasetId) {
      this.loadCharts();
    }
  }

  private loadCharts(): void {
    const charts = this.charts ?? [];
    const secondaryCharts = this.secondaryCharts ?? [];
    const selectedHero = this.heroChart ?? charts[0] ?? null;
    const selectedSecondary =
      secondaryCharts.length > 0
        ? secondaryCharts.slice(0, 8)
        : charts.slice(selectedHero ? 1 : 0, 9);

    const selected: Array<{ item: ChartRecommendation; layout: ChartLayout }> = [];
    if (selectedHero) {
      selected.push({ item: selectedHero, layout: 'hero' });
    }
    selectedSecondary.forEach(item => selected.push({ item, layout: 'secondary' }));

    this.cards = selected.map(({ item, layout }) => ({
      id: item.id,
      title: item.title,
      reason: item.reason,
      option: null,
      loading: true,
      error: null,
      layout
    }));

    for (const card of this.cards) {
      this.datasetApi.getChart(this.datasetId!, card.id).subscribe({
        next: response => {
          card.loading = false;
          const rawOption = response.data?.option ?? null;
          card.option = rawOption ? this.applyDashboardPolicy(rawOption) : null;
        },
        error: () => {
          card.loading = false;
          card.error = 'Falha ao carregar gráfico';
        }
      });
    }
  }

  chartLink(recommendationId: string): string[] {
    if (!this.datasetId) {
      return ['/', this.lang, 'datasets'];
    }

    return ['/', this.lang, 'datasets', this.datasetId, 'charts', recommendationId];
  }

  trackById(_: number, card: ChartCardState): string {
    return card.id;
  }

  private applyDashboardPolicy(option: any): any {
    const normalized = {
      ...option,
      grid: {
        top: 48,
        right: 24,
        bottom: 44,
        left: 56,
        ...(option.grid || {})
      }
    };

    const seriesList: any[] = Array.isArray(normalized.series) ? [...normalized.series] : [];
    if (seriesList.length > this.maxSeries) {
      seriesList.sort((a, b) => this.getSeriesMagnitude(b) - this.getSeriesMagnitude(a));
      normalized.series = seriesList.slice(0, this.maxSeries);
    } else {
      normalized.series = seriesList;
    }

    normalized.legend = {
      type: 'scroll',
      bottom: 0,
      icon: 'circle',
      itemWidth: 8,
      itemHeight: 8,
      ...(normalized.legend || {})
    };

    if (Array.isArray(normalized.legend.data) && normalized.legend.data.length > this.maxLegendItems) {
      normalized.legend.data = normalized.legend.data.slice(0, this.maxLegendItems);
    }

    const axisFormatter = (value: number) => formatCompactNumber(value, { locale: 'pt-BR' });
    normalized.yAxis = this.applyAxisFormat(normalized.yAxis, axisFormatter);
    normalized.xAxis = this.applyAxisFormat(normalized.xAxis, axisFormatter);

    normalized.tooltip = {
      trigger: 'axis',
      ...(normalized.tooltip || {})
    };

    const previousFormatter = normalized.tooltip.formatter;
    normalized.tooltip.formatter = (params: any) => {
      if (typeof previousFormatter === 'function') {
        return previousFormatter(params);
      }

      const list = Array.isArray(params) ? params : [params];
      const lines: string[] = [];
      const first = list[0];
      if (first?.axisValueLabel) {
        lines.push(first.axisValueLabel);
      }

      for (const item of list.slice(0, this.maxSeries)) {
        const value = Array.isArray(item.value) ? item.value[item.value.length - 1] : item.value;
        const safeValue = typeof value === 'number' ? formatCompactNumber(value, { locale: 'pt-BR' }) : String(value ?? '-');
        lines.push(`${item.marker || ''}${item.seriesName}: ${safeValue}`);
      }

      return lines.join('<br/>');
    };

    return normalized;
  }

  private applyAxisFormat(axis: any, formatter: (value: number) => string): any {
    if (Array.isArray(axis)) {
      return axis.map(item => ({
        ...item,
        axisLabel: {
          ...(item?.axisLabel || {}),
          formatter
        }
      }));
    }

    if (!axis) {
      return axis;
    }

    return {
      ...axis,
      axisLabel: {
        ...(axis.axisLabel || {}),
        formatter
      }
    };
  }

  private getSeriesMagnitude(series: any): number {
    if (!Array.isArray(series?.data)) {
      return 0;
    }

    return series.data.reduce((acc: number, current: any) => {
      if (typeof current === 'number') {
        return acc + Math.abs(current);
      }

      if (Array.isArray(current)) {
        const value = current[current.length - 1];
        return acc + (typeof value === 'number' ? Math.abs(value) : 0);
      }

      return acc;
    }, 0);
  }
}
