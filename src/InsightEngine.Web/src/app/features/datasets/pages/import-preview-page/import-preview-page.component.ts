import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DatasetApiService } from '../../../../core/services/dataset-api.service';
import { ToastService } from '../../../../core/services/toast.service';
import { HttpErrorUtil } from '../../../../core/util/http-error.util';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { LoadingBarComponent } from '../../../../shared/components/loading-bar/loading-bar.component';
import { ErrorPanelComponent } from '../../../../shared/components/error-panel/error-panel.component';
import { ApiError } from '../../../../core/models/api-response.model';
import {
  FinalizeImportRequest,
  ImportPreviewColumn,
  ImportPreviewResponse
} from '../../../../core/models/dataset.model';
import { LanguageService } from '../../../../core/services/language.service';
import { TranslatePipe } from '../../../../core/pipes/translate.pipe';

@Component({
  selector: 'app-import-preview-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslatePipe,
    ...MATERIAL_MODULES,
    LoadingBarComponent,
    ErrorPanelComponent
  ],
  templateUrl: './import-preview-page.component.html',
  styleUrls: ['./import-preview-page.component.scss']
})
export class ImportPreviewPageComponent implements OnInit {
  datasetId = '';
  loading = false;
  finalizing = false;
  error: ApiError | null = null;

  preview: ImportPreviewResponse | null = null;
  targetColumn = '';
  currencyCode = 'BRL';

  ignoredColumns = new Set<string>();
  confirmedTypeByColumn: Record<string, string> = {};

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private datasetApi: DatasetApiService,
    private toast: ToastService,
    private languageService: LanguageService
  ) {}

  get currentLanguage(): string {
    return this.languageService.currentLanguage;
  }

  get columns(): ImportPreviewColumn[] {
    return this.preview?.columns ?? [];
  }

  get displayedColumns(): string[] {
    if (!this.preview) {
      return [];
    }

    return this.preview.columns
      .map(column => column.name)
      .filter(name => !this.ignoredColumns.has(name));
  }

  ngOnInit(): void {
    this.datasetId = this.route.snapshot.paramMap.get('datasetId') || '';
    if (!this.datasetId) {
      this.error = {
        code: 'MISSING_DATASET_ID',
        message: this.languageService.translate('importPreview.errorMissingDatasetId')
      };
      return;
    }

    this.loadPreview();
  }

  loadPreview(): void {
    this.loading = true;
    this.error = null;

    this.datasetApi.getImportPreview(this.datasetId).subscribe({
      next: (response) => {
        this.loading = false;
        if (!response.success || !response.data) {
          this.error = {
            code: 'IMPORT_PREVIEW_EMPTY',
            message: this.languageService.translate('importPreview.errorLoad')
          };
          return;
        }

        this.preview = response.data;
        this.initializeSelection(response.data);
      },
      error: (err) => {
        this.loading = false;
        this.error = HttpErrorUtil.extractApiError(err) || {
          code: 'IMPORT_PREVIEW_ERROR',
          message: HttpErrorUtil.extractErrorMessage(err)
        };
      }
    });
  }

  setIgnored(columnName: string, ignored: boolean): void {
    if (ignored) {
      this.ignoredColumns.add(columnName);
      if (this.targetColumn === columnName) {
        this.targetColumn = '';
      }
      return;
    }

    this.ignoredColumns.delete(columnName);
  }

  setTarget(columnName: string): void {
    this.targetColumn = columnName;
    this.ignoredColumns.delete(columnName);
  }

  goBackToUpload(): void {
    this.router.navigate(['/', this.currentLanguage, 'datasets', 'new']);
  }

  finalizeImport(): void {
    if (!this.preview || this.finalizing) {
      return;
    }

    const payload: FinalizeImportRequest = {
      targetColumn: this.targetColumn || null,
      ignoredColumns: [...this.ignoredColumns],
      columnTypeOverrides: this.buildTypeOverrides(),
      currencyCode: (this.currencyCode || 'BRL').trim().toUpperCase()
    };

    this.finalizing = true;
    this.error = null;

    this.datasetApi.finalizeImport(this.datasetId, payload).subscribe({
      next: (response) => {
        this.finalizing = false;

        if (!response.success) {
          this.error = {
            code: response.errors?.[0]?.code || 'IMPORT_FINALIZE_ERROR',
            message: response.errors?.[0]?.message || this.languageService.translate('importPreview.errorFinalize')
          };
          return;
        }

        this.toast.success(this.languageService.translate('importPreview.success'));
        this.router.navigate(['/', this.currentLanguage, 'datasets', this.datasetId, 'recommendations']);
      },
      error: (err) => {
        this.finalizing = false;
        this.error = HttpErrorUtil.extractApiError(err) || {
          code: 'IMPORT_FINALIZE_ERROR',
          message: HttpErrorUtil.extractErrorMessage(err)
        };
      }
    });
  }

  trackByColumnName(_: number, column: ImportPreviewColumn): string {
    return column.name;
  }

  private initializeSelection(preview: ImportPreviewResponse): void {
    this.ignoredColumns = new Set(preview.suggestedIgnoredCandidates || []);
    this.targetColumn = preview.suggestedTargetCandidates?.[0] || '';
    this.currencyCode =
      preview.columns.find(column => !!column.hints.currencyCode)?.hints.currencyCode ||
      'BRL';

    this.confirmedTypeByColumn = {};
    preview.columns.forEach(column => {
      this.confirmedTypeByColumn[column.name] = column.inferredType;
    });

    if (this.targetColumn) {
      this.ignoredColumns.delete(this.targetColumn);
    }
  }

  private buildTypeOverrides(): Record<string, string> {
    if (!this.preview) {
      return {};
    }

    const overrides: Record<string, string> = {};
    this.preview.columns.forEach(column => {
      const selected = this.confirmedTypeByColumn[column.name];
      if (selected && selected !== column.inferredType) {
        overrides[column.name] = selected;
      }
    });

    return overrides;
  }
}
