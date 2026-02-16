import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ApiErrorHandlerService } from '../services/api-error-handler.service';

export const apiErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const errorHandler = inject(ApiErrorHandlerService);

  return next(req).pipe(
    catchError((error) => {
      errorHandler.handle(error);
      return throwError(() => error);
    })
  );
};
