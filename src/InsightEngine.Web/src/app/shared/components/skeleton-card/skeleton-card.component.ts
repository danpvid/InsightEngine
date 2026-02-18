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
    /* Minimal local rules; shared skeleton styles live in global stylesheet */
    .skeleton-card { animation: pulse 1.5s ease-in-out infinite; }
    .skeleton-header { flex: 1; margin-left: 16px; }
  `]
})
export class SkeletonCardComponent {
  @Input() count: number = 6;
}
