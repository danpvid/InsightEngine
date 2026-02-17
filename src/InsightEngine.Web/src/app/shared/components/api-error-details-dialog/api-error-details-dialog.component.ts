import { CommonModule } from '@angular/common';
import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { ApiError } from '../../../core/models/api-response.model';

@Component({
  selector: 'app-api-error-details-dialog',
  standalone: true,
  imports: [
    CommonModule,
    MatDialogModule,
    MatButtonModule,
    MatExpansionModule,
    MatIconModule
  ],
  template: `
    <h2 mat-dialog-title>
      <mat-icon color="warn">error</mat-icon>
      Request Failed
    </h2>

    <mat-dialog-content>
      <p class="summary">{{ data.message }}</p>

      <mat-expansion-panel [expanded]="true" *ngIf="data.errors?.length">
        <mat-expansion-panel-header>
          <mat-panel-title>Error details</mat-panel-title>
        </mat-expansion-panel-header>

        <ul class="details-list">
          <li *ngFor="let item of data.errors">
            <strong>{{ item.code }}</strong>: {{ item.message }}
            <span *ngIf="item.target"> ({{ item.target }})</span>
          </li>
        </ul>
      </mat-expansion-panel>

      <p class="trace" *ngIf="data.traceId">
        <strong>Trace ID:</strong> <code>{{ data.traceId }}</code>
      </p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Close</button>
    </mat-dialog-actions>
  `,
  styles: [`
    h2 {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .summary {
      margin-top: 0;
      margin-bottom: 12px;
    }

    .details-list {
      margin: 0;
      padding-left: 18px;
    }

    .details-list li {
      margin-bottom: 8px;
      line-height: 1.35;
    }

    .trace {
      margin-top: 12px;
      font-size: 12px;
      color: var(--text-2);
      word-break: break-all;
    }
  `]
})
export class ApiErrorDetailsDialogComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: ApiError
  ) {}
}
