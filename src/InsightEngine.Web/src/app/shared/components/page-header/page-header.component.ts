import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  template: `
    <div class="page-header">
      <div class="header-row">
        <div class="header-content">
          <mat-icon *ngIf="icon">{{ icon }}</mat-icon>
          <h1>{{ title }}</h1>
        </div>
        <div class="header-actions" *ngIf="subtitle">
          <span class="subtitle-badge">{{ subtitle }}</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 12px; /* compact the header to remove unnecessary vertical space */
    }

    .header-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
    }

    .header-content {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .header-content mat-icon {
      font-size: 20px;
      width: 20px;
      height: 20px;
      color: var(--primary);
    }

    h1 {
      margin: 0;
      font-size: 18px;
      font-weight: 500;
      color: var(--text);
    }

    /* subtitle rendered as a compact badge on the right — more emphasis and aligns with toolbar */
    .subtitle-badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 6px 10px;
      border-radius: 8px;
      background: var(--surface-2);
      border: 1px solid var(--border);
      color: var(--text-2);
      font-size: 13px;
      font-weight: 600;
      white-space: nowrap;
    }
  `]
})
export class PageHeaderComponent {
  @Input() title: string = '';
  @Input() subtitle?: string;
  @Input() icon?: string;
}
