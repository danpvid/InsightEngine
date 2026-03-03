import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { DashboardInsights } from '../models/dashboard.model';

@Component({
  selector: 'app-insights-panel',
  standalone: true,
  imports: [CommonModule, MatCardModule],
  templateUrl: './insights-panel.component.html',
  styleUrls: ['./insights-panel.component.scss']
})
export class InsightsPanelComponent {
  @Input() insights: DashboardInsights | null = null;
  expanded = false;

  toggleExpanded(): void {
    this.expanded = !this.expanded;
  }
}
