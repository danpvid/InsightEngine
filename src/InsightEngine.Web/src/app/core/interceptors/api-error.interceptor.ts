import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ApiErrorHandlerService } from '../services/api-error-handler.service';
import { HttpErrorUtil } from '../util/http-error.util';

export const apiErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const errorHandler = inject(ApiErrorHandlerService);

  return next(req).pipe(
    catchError((error) => {
      if (!HttpErrorUtil.isRequestAbort(error)) {
        errorHandler.handle(error);
      }

      return throwError(() => error);
    })
  );
};
