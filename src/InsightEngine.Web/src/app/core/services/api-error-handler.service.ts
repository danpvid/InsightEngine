import { Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiError } from '../models/api-response.model';
import { HttpErrorUtil } from '../util/http-error.util';
import { ApiErrorDetailsDialogComponent } from '../../shared/components/api-error-details-dialog/api-error-details-dialog.component';

@Injectable({
  providedIn: 'root'
})
export class ApiErrorHandlerService {
  constructor(
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  handle(error: unknown): ApiError {
    const apiError = HttpErrorUtil.extractApiError(error) || {
      code: 'http_error',
      message: HttpErrorUtil.extractErrorMessage(error)
    };

    const actionLabel = apiError.errors && apiError.errors.length > 0 ? 'Details' : 'Close';
    const ref = this.snackBar.open(apiError.message, actionLabel, {
      duration: 7000,
      horizontalPosition: 'end',
      verticalPosition: 'top',
      panelClass: ['error-snackbar']
    });

    if (actionLabel === 'Details') {
      ref.onAction().subscribe(() => {
        this.dialog.open(ApiErrorDetailsDialogComponent, {
          width: '640px',
          maxWidth: '95vw',
          data: apiError
        });
      });
    }

    return apiError;
  }
}
