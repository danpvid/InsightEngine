import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { ApiError } from '../../../core/models/api-response.model';

@Component({
  selector: 'app-error-panel',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule],
  template: `
    <mat-card class="error-card" *ngIf="error">
      <mat-card-content>
        <div class="error-icon">
          <mat-icon color="warn">error_outline</mat-icon>
        </div>
        <h3>{{ error.message || 'Ocorreu um erro' }}</h3>
        <p class="error-code" *ngIf="error.code">Código: {{ error.code }}</p>
        <p class="error-code" *ngIf="error.traceId">TraceId: {{ error.traceId }}</p>

        <div class="error-details" *ngIf="hasDetails()">
          <p><strong>Detalhes:</strong></p>
          <ul>
            <li *ngFor="let detail of getDetailsList()">{{ detail }}</li>
          </ul>
        </div>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .error-card {
      background-color: var(--danger-bg);
      margin: 16px 0;
    }

    .error-icon {
      text-align: center;
      margin-bottom: 16px;
    }

    .error-icon mat-icon {
      font-size: 48px;
      width: 48px;
      height: 48px;
    }

    h3 {
      text-align: center;
      margin-bottom: 8px;
      color: var(--danger);
    }

    .error-code {
      text-align: center;
      color: var(--text-2);
      font-size: 12px;
      margin-bottom: 8px;
      word-break: break-all;
    }

    .error-details {
      margin-top: 16px;
      padding: 12px;
      background-color: var(--surface);
      border-radius: 4px;
    }

    .error-details ul {
      margin-top: 8px;
      padding-left: 20px;
    }

    .error-details li {
      margin-bottom: 4px;
      color: var(--text-2);
    }
  `]
})
export class ErrorPanelComponent {
  @Input() error: ApiError | null = null;

  hasDetails(): boolean {
    return Array.isArray(this.error?.errors) && this.error!.errors.length > 0;
  }

  getDetailsList(): string[] {
    return this.error?.errors?.map(item =>
      item.target
        ? `${item.code} (${item.target}): ${item.message}`
        : `${item.code}: ${item.message}`) || [];
  }
}
