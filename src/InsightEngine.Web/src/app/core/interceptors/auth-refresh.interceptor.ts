import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authRefreshInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error) => {
      if (error.status !== 401 || req.url.includes('/api/v1/auth/refresh')) {
        return throwError(() => error);
      }

      return authService.refreshAccessToken().pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            return throwError(() => error);
          }

          const token = authService.accessToken;
          if (!token) {
            return throwError(() => error);
          }

          const retried = req.clone({
            setHeaders: {
              Authorization: `Bearer ${token}`
            }
          });

          return next(retried);
        })
      );
    })
  );
};
