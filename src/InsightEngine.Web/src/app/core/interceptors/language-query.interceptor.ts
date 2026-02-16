import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { LanguageService } from '../services/language.service';

export const languageQueryInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.includes('/api/')) {
    return next(request);
  }

  const languageService = inject(LanguageService);
  const language = languageService.currentLanguage;

  let updatedUrl = request.url;
  try {
    const parsedUrl = new URL(request.url);
    parsedUrl.searchParams.set('lang', language);
    updatedUrl = parsedUrl.toString();
  } catch {
    const separator = request.url.includes('?') ? '&' : '?';
    updatedUrl = `${request.url}${separator}lang=${encodeURIComponent(language)}`;
  }

  const requestWithLanguage = request.clone({
    url: updatedUrl,
    setHeaders: {
      'Accept-Language': language
    }
  });

  return next(requestWithLanguage);
};

