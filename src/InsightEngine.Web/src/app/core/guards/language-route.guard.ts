import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { LanguageService } from '../services/language.service';

export const languageRouteGuard: CanActivateFn = (route) => {
  const router = inject(Router);
  const languageService = inject(LanguageService);
  const routeLanguage = route.paramMap.get('lang');

  if (!languageService.isSupportedLanguage(routeLanguage)) {
    return router.createUrlTree(['/', languageService.currentLanguage, 'datasets', 'new']);
  }

  languageService.setCurrentLanguage(routeLanguage);
  return true;
};
