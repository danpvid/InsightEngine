import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NgxEchartsModule } from 'ngx-echarts';
import { MatCardModule } from '@angular/material/card';
import { ChartRecommendation } from '../../core/models/recommendation.model';
import { DatasetApiService } from '../../core/services/dataset-api.service';

interface ChartCardState {
  id: string;
  title: string;
  reason?: string;
  option: any | null;
  loading: boolean;
  error: string | null;
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
  @Input() charts: ChartRecommendation[] = [];

  readonly maxCharts = 6;
  cards: ChartCardState[] = [];

  constructor(private readonly datasetApi: DatasetApiService) {}

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['datasetId'] || changes['charts']) && this.datasetId) {
      this.loadCharts();
    }
  }

  private loadCharts(): void {
    const selected = this.charts.slice(0, this.maxCharts);
    this.cards = selected.map(item => ({
      id: item.id,
      title: item.title,
      reason: item.reason,
      option: null,
      loading: true,
      error: null
    }));

    for (const card of this.cards) {
      this.datasetApi.getChart(this.datasetId!, card.id).subscribe({
        next: response => {
          card.loading = false;
          card.option = response.data?.option ?? null;
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
}
