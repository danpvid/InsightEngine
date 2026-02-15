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
      font-size: 32px;
      width: 32px;
      height: 32px;
      color: #3f51b5;
    }

    h1 {
      margin: 0;
      font-size: 28px;
      font-weight: 400;
      color: #333;
    }

    .subtitle {
      margin: 8px 0 0 0;
      color: #666;
      font-size: 14px;
      margin-left: 44px;
    }
  `]
})
export class PageHeaderComponent {
  @Input() title: string = '';
  @Input() subtitle?: string;
  @Input() icon?: string;
}
