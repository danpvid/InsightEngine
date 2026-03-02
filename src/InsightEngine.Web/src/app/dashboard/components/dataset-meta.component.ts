import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { DashboardDatasetSummary, DashboardMetadata } from '../models/dashboard.model';

@Component({
  selector: 'app-dataset-meta',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatChipsModule],
  templateUrl: './dataset-meta.component.html',
  styleUrls: ['./dataset-meta.component.scss']
})
export class DatasetMetaComponent {
  @Input() lang = 'pt-br';
  @Input() datasetId: string | null = null;
  @Input() dataset: DashboardDatasetSummary | null = null;
  @Input() metadata: DashboardMetadata | null = null;

  get recommendationsLink(): string[] {
    if (!this.datasetId) {
      return ['/', this.lang, 'datasets'];
    }

    return ['/', this.lang, 'datasets', this.datasetId, 'recommendations'];
  }

  get exploreLink(): string[] {
    if (!this.datasetId) {
      return ['/', this.lang, 'datasets'];
    }

    return ['/', this.lang, 'datasets', this.datasetId, 'explore'];
  }
}
