import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
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
import { LanguageService } from '../../../../core/services/language.service';
import { TranslatePipe } from '../../../../core/pipes/translate.pipe';

type DatasetSortOption =
  | 'createdDesc'
  | 'createdAsc'
  | 'nameAsc'
  | 'nameDesc'
  | 'sizeDesc'
  | 'sizeAsc';

@Component({
  selector: 'app-dataset-upload-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslatePipe,
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
  selectedDatasetSort: DatasetSortOption = 'createdDesc';
  deletingDatasetId: string | null = null;

  isDragging: boolean = false;
  uploadProgress: number = 0;
  runtimeConfig: RuntimeConfig | null = null;
  uploadMaxBytes: number = 20 * 1024 * 1024;

  constructor(
    private datasetApi: DatasetApiService,
    private router: Router,
    private toast: ToastService,
    private languageService: LanguageService
  ) {}

  get currentLanguage(): string {
    return this.languageService.currentLanguage;
  }

  ngOnInit(): void {
    this.loadRuntimeConfig();
    this.loadDatasets();
  }

  get sortedDatasets(): DataSetSummary[] {
    const sorted = [...this.datasets];
    switch (this.selectedDatasetSort) {
      case 'createdAsc':
        return sorted.sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
      case 'nameAsc':
        return sorted.sort((a, b) => a.originalFileName.localeCompare(b.originalFileName));
      case 'nameDesc':
        return sorted.sort((a, b) => b.originalFileName.localeCompare(a.originalFileName));
      case 'sizeDesc':
        return sorted.sort((a, b) => b.fileSizeInBytes - a.fileSizeInBytes);
      case 'sizeAsc':
        return sorted.sort((a, b) => a.fileSizeInBytes - b.fileSizeInBytes);
      case 'createdDesc':
      default:
        return sorted.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    }
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
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.handleFile(input.files[0]);
    }
  }

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
    if (!file.name.toLowerCase().endsWith('.csv')) {
      this.error = {
        code: 'INVALID_FILE_TYPE',
        message: this.languageService.translate('upload.errorInvalidFileType')
      };
      this.selectedFile = null;
      return;
    }

    if (file.size > this.uploadMaxBytes) {
      this.error = {
        code: 'FILE_TOO_LARGE',
        message: this.languageService.translate('upload.errorFileTooLarge', { maxSize: this.getUploadMaxLabel() })
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
        message: this.languageService.translate('upload.errorNoFileSelected')
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
            this.toast.success(this.languageService.translate('upload.successDatasetUploaded'));
            this.router.navigate(['/', this.currentLanguage, 'datasets', progressEvent.response.data.datasetId, 'recommendations']);
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
    if (!this.selectedFile) {
      return '';
    }

    const sizeInMb = this.selectedFile.size / (1024 * 1024);
    return sizeInMb < 1
      ? `${(this.selectedFile.size / 1024).toFixed(2)} KB`
      : `${sizeInMb.toFixed(2)} MB`;
  }

  changeFile(): void {
    this.clearFile();
  }

  openDataset(dataset: DataSetSummary): void {
    this.router.navigate(['/', this.currentLanguage, 'datasets', dataset.datasetId, 'recommendations']);
  }

  openExplore(dataset: DataSetSummary, event?: Event): void {
    event?.stopPropagation();
    this.router.navigate(['/', this.currentLanguage, 'datasets', dataset.datasetId, 'explore']);
  }

  deleteDataset(dataset: DataSetSummary, event?: Event): void {
    event?.stopPropagation();
    if (this.deletingDatasetId) {
      return;
    }

    const warningStep = this.languageService.translate('upload.deleteConfirmStep1', {
      fileName: dataset.originalFileName,
      datasetId: dataset.datasetId
    });

    if (!window.confirm(warningStep)) {
      return;
    }

    const confirmationKeyword = this.currentLanguage === 'pt-br' ? 'EXCLUIR' : 'DELETE';
    const finalStep = this.languageService.translate('upload.deleteConfirmStep2', {
      keyword: confirmationKeyword,
      datasetId: dataset.datasetId
    });
    const typed = window.prompt(finalStep) || '';
    if (typed.trim().toUpperCase() !== confirmationKeyword) {
      this.toast.info(this.languageService.translate('upload.deleteCancelled'));
      return;
    }

    this.deletingDatasetId = dataset.datasetId;
    this.datasetApi.deleteDataset(dataset.datasetId).subscribe({
      next: (response) => {
        this.deletingDatasetId = null;

        if (!response.success) {
          this.toast.error(
            response.errors?.[0]?.message ||
            this.languageService.translate('upload.errorDatasetDeleteFailed'));
          return;
        }

        this.datasets = this.datasets.filter(item => item.datasetId !== dataset.datasetId);
        this.toast.success(this.languageService.translate('upload.successDatasetDeleted'));
      },
      error: (err) => {
        this.deletingDatasetId = null;
        this.toast.error(
          HttpErrorUtil.extractErrorMessage(err) ||
          this.languageService.translate('upload.errorDatasetDeleteFailed'));
      }
    });
  }

  formatFileSize(sizeInBytes: number): string {
    const sizeInMb = sizeInBytes / (1024 * 1024);
    return sizeInMb < 1
      ? `${(sizeInBytes / 1024).toFixed(2)} KB`
      : `${sizeInMb.toFixed(2)} MB`;
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString(this.currentLanguage === 'pt-br' ? 'pt-BR' : 'en-US', {
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

