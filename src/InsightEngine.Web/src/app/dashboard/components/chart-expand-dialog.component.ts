import { AfterViewInit, Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { NgxEchartsModule } from 'ngx-echarts';

export interface ChartExpandDialogData {
  title: string;
  option: any;
}

@Component({
  selector: 'app-chart-expand-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, NgxEchartsModule],
  template: `
    <div class="dialog-root">
      <header class="dialog-header">
        <h3 [title]="data.title">{{ data.title }}</h3>
        <button mat-icon-button type="button" (click)="close()">
          <mat-icon>close</mat-icon>
        </button>
      </header>
      <div class="dialog-chart" echarts [options]="data.option"></div>
    </div>
  `,
  styles: [`
    .dialog-root {
      width: min(95vw, 1400px);
      height: min(92vh, 980px);
      display: flex;
      flex-direction: column;
      background: #0f172a;
      color: #e2e8f0;
      border: 1px solid rgba(148, 163, 184, 0.25);
      border-radius: 12px;
      overflow: hidden;
    }

    .dialog-header {
      height: 56px;
      padding: 0 12px 0 16px;
      border-bottom: 1px solid rgba(148, 163, 184, 0.2);
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 10px;
    }

    .dialog-header h3 {
      margin: 0;
      font-size: 16px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .dialog-chart {
      flex: 1;
      min-height: 0;
      width: 100%;
    }
  `]
})
export class ChartExpandDialogComponent implements AfterViewInit {
  constructor(
    @Inject(MAT_DIALOG_DATA) public readonly data: ChartExpandDialogData,
    private readonly dialogRef: MatDialogRef<ChartExpandDialogComponent>
  ) {
  }

  ngAfterViewInit(): void {
    setTimeout(() => window.dispatchEvent(new Event('resize')), 80);
  }

  close(): void {
    this.dialogRef.close();
  }
}
