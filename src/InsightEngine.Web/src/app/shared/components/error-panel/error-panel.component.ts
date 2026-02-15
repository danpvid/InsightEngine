import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
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
        <div class="error-details" *ngIf="error.details && hasDetails()">
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
      background-color: #ffebee;
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
      color: #c62828;
    }

    .error-code {
      text-align: center;
      color: #666;
      font-size: 12px;
      margin-bottom: 16px;
    }

    .error-details {
      margin-top: 16px;
      padding: 12px;
      background-color: #fff;
      border-radius: 4px;
    }

    .error-details ul {
      margin-top: 8px;
      padding-left: 20px;
    }

    .error-details li {
      margin-bottom: 4px;
      color: #666;
    }
  `]
})
export class ErrorPanelComponent {
  @Input() error: ApiError | null = null;

  hasDetails(): boolean {
    return !!this.error?.details && Object.keys(this.error.details).length > 0;
  }

  getDetailsList(): string[] {
    if (!this.error?.details) return [];
    
    const details: string[] = [];
    for (const [key, values] of Object.entries(this.error.details)) {
      if (Array.isArray(values)) {
        values.forEach(value => details.push(`${key}: ${value}`));
      } else {
        details.push(`${key}: ${values}`);
      }
    }
    return details;
  }
}
