import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  template: `
    <div class="page-header">
      <div class="header-content">
        <mat-icon *ngIf="icon">{{ icon }}</mat-icon>
        <h1>{{ title }}</h1>
      </div>
      <p *ngIf="subtitle" class="subtitle">{{ subtitle }}</p>
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 24px;
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

    .subtitle {
      margin: 8px 0 0 0;
      color: var(--text-2);
      font-size: 13px;
      margin-left: 28px;
    }
  `]
})
export class PageHeaderComponent {
  @Input() title: string = '';
  @Input() subtitle?: string;
  @Input() icon?: string;
}
