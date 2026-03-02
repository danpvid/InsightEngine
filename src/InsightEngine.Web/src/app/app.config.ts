import { ApplicationConfig, importProvidersFrom, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideEcharts } from 'ngx-echarts';
import { MatDialogModule } from '@angular/material/dialog';
import { MatSnackBarModule } from '@angular/material/snack-bar';

import { routes } from './app.routes';
import { jwtInterceptor } from './auth/jwt.interceptor';
import { refreshInterceptor } from './auth/refresh.interceptor';
import { apiErrorInterceptor } from './core/interceptors/api-error.interceptor';
import { languageQueryInterceptor } from './core/interceptors/language-query.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([languageQueryInterceptor, jwtInterceptor, refreshInterceptor, apiErrorInterceptor])
    ),
    importProvidersFrom(MatDialogModule, MatSnackBarModule),
    provideAnimations(),
    provideEcharts()
  ]
};
