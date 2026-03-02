import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { DashboardDataQualityRow } from '../models/dashboard.model';

@Component({
  selector: 'app-data-quality-table',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatTableModule],
  templateUrl: './data-quality-table.component.html',
  styleUrls: ['./data-quality-table.component.scss']
})
export class DataQualityTableComponent {
  @Input() rows: DashboardDataQualityRow[] = [];
  readonly displayedColumns = ['column', 'nullRate', 'outlierRate', 'distinctCount'];
}
