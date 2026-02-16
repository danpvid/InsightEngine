import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { DatasetApiService } from '../../../../core/services/dataset-api.service';
import { ToastService } from '../../../../core/services/toast.service';
import { HttpErrorUtil } from '../../../../core/util/http-error.util';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { LoadingBarComponent } from '../../../../shared/components/loading-bar/loading-bar.component';
import { ErrorPanelComponent } from '../../../../shared/components/error-panel/error-panel.component';
import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { ApiError } from '../../../../core/models/api-response.model';
import { DataSetSummary } from '../../../../core/models/dataset.model';
import { RuntimeConfig } from '../../../../core/models/runtime-config.model';
import { environment } from '../../../../../environments/environment';

@Component({
  selector: 'app-dataset-upload-page',
  standalone: true,
  imports: [
    CommonModule,
    ...MATERIAL_MODULES,
    LoadingBarComponent,
    ErrorPanelComponent,
    PageHeaderComponent
  ],
  templateUrl: './dataset-upload-page.component.html',
  styleUrls: ['./dataset-upload-page.component.scss']
})
export class DatasetUploadPageComponent implements OnInit {
  selectedFile: File | null = null;
  loading: boolean = false;
  error: ApiError | null = null;
  
  datasets: DataSetSummary[] = [];
  loadingDatasets: boolean = false;

  // Drag & drop
  isDragging: boolean = false;

  // Upload progress
  uploadProgress: number = 0;
  runtimeConfig: RuntimeConfig | null = null;
  uploadMaxBytes: number = 20 * 1024 * 1024;

  constructor(
    private datasetApi: DatasetApiService,
    private router: Router,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.loadRuntimeConfig();
    this.loadDatasets();
  }

  loadRuntimeConfig(): void {
    this.datasetApi.getRuntimeConfig().subscribe({
      next: (response) => {
        if (!response.success || !response.data) {
          return;
        }

        this.runtimeConfig = response.data;
        this.uploadMaxBytes = response.data.uploadMaxBytes || this.uploadMaxBytes;
      },
      error: (err) => {
        console.error('Error loading runtime config:', err);
      }
    });
  }

  loadDatasets(): void {
    this.loadingDatasets = true;
    
    this.datasetApi.listDatasets().subscribe({
      next: (response) => {
        this.loadingDatasets = false;
        if (response.success && response.data) {
          this.datasets = response.data;
        }
      },
      error: (err) => {
        this.loadingDatasets = false;
        console.error('Error loading datasets:', err);
        // Silently fail - não interrompe o fluxo de upload
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.handleFile(input.files[0]);
    }
  }

  // Drag & Drop handlers
  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = false;

    if (event.dataTransfer?.files && event.dataTransfer.files.length > 0) {
      this.handleFile(event.dataTransfer.files[0]);
    }
  }

  private handleFile(file: File): void {
    // Validate file type
    if (!file.name.toLowerCase().endsWith('.csv')) {
      this.error = {
        code: 'INVALID_FILE_TYPE',
        message: 'Por favor, selecione um arquivo CSV válido.'
      };
      this.selectedFile = null;
      return;
    }

    // Validate file size using backend runtime config
    if (file.size > this.uploadMaxBytes) {
      this.error = {
        code: 'FILE_TOO_LARGE',
        message: `O arquivo é muito grande. Tamanho máximo: ${this.getUploadMaxLabel()}.`
      };
      this.selectedFile = null;
      return;
    }

    this.selectedFile = file;
    this.error = null;
  }

  uploadDataset(): void {
    if (!this.selectedFile) {
      this.error = {
        code: 'NO_FILE',
        message: 'Por favor, selecione um arquivo CSV primeiro.'
      };
      return;
    }

    this.loading = true;
    this.error = null;
    this.uploadProgress = 0;
    const uploadStart = performance.now();

    this.datasetApi.uploadDatasetWithProgress(this.selectedFile).subscribe({
      next: (progressEvent) => {
        this.uploadProgress = progressEvent.progress;

        if (progressEvent.response) {
          this.loading = false;
          
          if (progressEvent.response.success && progressEvent.response.data) {
            this.logDevTiming('dataset-upload', uploadStart, {
              datasetId: progressEvent.response.data.datasetId,
              fileSizeBytes: this.selectedFile?.size || 0
            });
            this.toast.success('Dataset enviado com sucesso!');
            this.router.navigate(['/datasets', progressEvent.response.data.datasetId, 'recommendations']);
          } else if (progressEvent.response.errors && progressEvent.response.errors.length > 0) {
            const first = progressEvent.response.errors[0];
            this.error = {
              code: first.code,
              message: first.message,
              target: first.target,
              errors: progressEvent.response.errors,
              traceId: progressEvent.response.traceId
            };
          }
        }
      },
      error: (err) => {
        this.loading = false;
        this.uploadProgress = 0;
        this.logDevTiming('dataset-upload-error', uploadStart);
        this.error = HttpErrorUtil.extractApiError(err) || {
          code: 'UPLOAD_ERROR',
          message: HttpErrorUtil.extractErrorMessage(err)
        };
      }
    });
  }

  clearFile(): void {
    this.selectedFile = null;
    this.error = null;
  }

  getFileSizeText(): string {
    if (!this.selectedFile) return '';
    const sizeInMB = this.selectedFile.size / (1024 * 1024);
    return sizeInMB < 1 
      ? `${(this.selectedFile.size / 1024).toFixed(2)} KB`
      : `${sizeInMB.toFixed(2)} MB`;
  }

  changeFile(): void {
    this.clearFile();
  }

  openDataset(dataset: DataSetSummary): void {
    this.router.navigate(['/datasets', dataset.datasetId, 'recommendations']);
  }

  formatFileSize(sizeInBytes: number): string {
    const sizeInMB = sizeInBytes / (1024 * 1024);
    return sizeInMB < 1 
      ? `${(sizeInBytes / 1024).toFixed(2)} KB`
      : `${sizeInMB.toFixed(2)} MB`;
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('pt-BR', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  getUploadMaxLabel(): string {
    if (this.runtimeConfig?.uploadMaxMb) {
      return `${this.runtimeConfig.uploadMaxMb.toFixed(0)}MB`;
    }

    const mb = this.uploadMaxBytes / (1024 * 1024);
    return `${mb.toFixed(0)}MB`;
  }

  private logDevTiming(event: string, startedAt: number, extra?: Record<string, unknown>): void {
    if (environment.production) {
      return;
    }

    console.info(`[timing] ${event}`, {
      durationMs: Math.round(performance.now() - startedAt),
      ...extra
    });
  }
}
