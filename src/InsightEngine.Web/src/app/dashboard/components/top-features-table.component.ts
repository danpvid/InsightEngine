import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { DashboardTopFeatureRow } from '../models/dashboard.model';
import { formatCompactNumber } from '../../shared/format/compact-number';

@Component({
  selector: 'app-top-features-table',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatTableModule],
  templateUrl: './top-features-table.component.html',
  styleUrls: ['./top-features-table.component.scss']
})
export class TopFeaturesTableComponent {
  @Input() rows: DashboardTopFeatureRow[] = [];
  readonly displayedColumns = ['column', 'score', 'correlation', 'nullRate'];

  compact(value: number): string {
    return formatCompactNumber(value, { locale: 'pt-BR' });
  }
}
