import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatProgressBarModule } from '@angular/material/progress-bar';

@Component({
  selector: 'app-loading-bar',
  standalone: true,
  imports: [CommonModule, MatProgressBarModule],
  template: `
    <div class="loading-container" *ngIf="loading">
      <mat-progress-bar mode="indeterminate"></mat-progress-bar>
    </div>
  `,
  styles: [`
    .loading-container {
      width: 100%;
      margin: 16px 0;
    }
  `]
})
export class LoadingBarComponent {
  @Input() loading: boolean = false;
}
