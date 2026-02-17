import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-skeleton-card',
  standalone: true,
  imports: [CommonModule, MatCardModule],
  template: `
    <mat-card class="skeleton-card">
      <mat-card-header>
        <div mat-card-avatar class="skeleton-avatar"></div>
        <div class="skeleton-header">
          <div class="skeleton-title"></div>
          <div class="skeleton-subtitle"></div>
        </div>
      </mat-card-header>
      <mat-card-content>
        <div class="skeleton-badge"></div>
        <div class="skeleton-line"></div>
        <div class="skeleton-line short"></div>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .skeleton-card {
      animation: pulse 1.5s ease-in-out infinite;
    }

    @keyframes pulse {
      0%, 100% {
        opacity: 1;
      }
      50% {
        opacity: 0.6;
      }
    }

    .skeleton-avatar {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      background: var(--skeleton-gradient);
      background-size: 200% 100%;
      animation: shimmer 2s infinite;
    }

    .skeleton-header {
      flex: 1;
      margin-left: 16px;
    }

    .skeleton-title {
      height: 20px;
      width: 60%;
      background: var(--skeleton-gradient);
      background-size: 200% 100%;
      animation: shimmer 2s infinite;
      border-radius: 4px;
      margin-bottom: 8px;
    }

    .skeleton-subtitle {
      height: 14px;
      width: 80%;
      background: var(--skeleton-gradient);
      background-size: 200% 100%;
      animation: shimmer 2s infinite;
      border-radius: 4px;
    }

    .skeleton-badge {
      height: 24px;
      width: 80px;
      background: var(--skeleton-gradient);
      background-size: 200% 100%;
      animation: shimmer 2s infinite;
      border-radius: 12px;
      margin: 16px 0;
    }

    .skeleton-line {
      height: 14px;
      width: 100%;
      background: var(--skeleton-gradient);
      background-size: 200% 100%;
      animation: shimmer 2s infinite;
      border-radius: 4px;
      margin-bottom: 8px;
    }

    .skeleton-line.short {
      width: 70%;
    }

    @keyframes shimmer {
      0% {
        background-position: -200% 0;
      }
      100% {
        background-position: 200% 0;
      }
    }
  `]
})
export class SkeletonCardComponent {
  @Input() count: number = 6;
}
