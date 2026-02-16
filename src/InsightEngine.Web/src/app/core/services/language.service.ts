import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { LanguageCode } from '../models/language.model';
import { TRANSLATIONS } from '../i18n/translations';

@Injectable({
  providedIn: 'root'
})
export class LanguageService {
  private static readonly storageKey = 'insightengine.language';
  private readonly supportedLanguages: LanguageCode[] = ['pt-br', 'en'];
  private readonly defaultLanguage: LanguageCode = 'pt-br';

  private readonly currentLanguageSubject = new BehaviorSubject<LanguageCode>(this.resolveInitialLanguage());
  readonly currentLanguage$ = this.currentLanguageSubject.asObservable();

  get currentLanguage(): LanguageCode {
    return this.currentLanguageSubject.value;
  }

  get availableLanguages(): LanguageCode[] {
    return this.supportedLanguages;
  }

  isSupportedLanguage(language: string | null | undefined): language is LanguageCode {
    if (!language) {
      return false;
    }

    return this.supportedLanguages.includes(language.toLowerCase() as LanguageCode);
  }

  setCurrentLanguage(language: string | null | undefined): LanguageCode {
    const normalized = this.normalizeLanguage(language);
    this.currentLanguageSubject.next(normalized);
    localStorage.setItem(LanguageService.storageKey, normalized);
    return normalized;
  }

  translate(key: string, params?: Record<string, string | number>): string {
    const activeLanguage = this.currentLanguage;
    const fallbackLanguage = this.defaultLanguage;
    const template = TRANSLATIONS[activeLanguage][key]
      ?? TRANSLATIONS[fallbackLanguage][key]
      ?? key;

    if (!params) {
      return template;
    }

    return Object.entries(params).reduce((message, [param, value]) => {
      return message.replace(new RegExp(`{{\\s*${param}\\s*}}`, 'g'), String(value));
    }, template);
  }

  async switchLanguage(language: string, router: Router): Promise<void> {
    const normalized = this.setCurrentLanguage(language);
    const currentUrl = router.url;
    const urlTree = router.parseUrl(currentUrl);
    const pathSegments = currentUrl.split('?')[0].split('#')[0].split('/').filter(segment => segment.length > 0);

    if (pathSegments.length === 0) {
      await router.navigate(['/', normalized, 'datasets', 'new']);
      return;
    }

    if (this.isSupportedLanguage(pathSegments[0])) {
      pathSegments[0] = normalized;
    } else {
      pathSegments.unshift(normalized);
    }

    await router.navigate(['/', ...pathSegments], {
      queryParams: urlTree.queryParams,
      fragment: urlTree.fragment ?? undefined
    });
  }

  private resolveInitialLanguage(): LanguageCode {
    const stored = localStorage.getItem(LanguageService.storageKey);
    if (this.isSupportedLanguage(stored)) {
      return stored;
    }

    const browserLanguage = navigator.language?.toLowerCase();
    if (browserLanguage?.startsWith('pt')) {
      return 'pt-br';
    }

    if (browserLanguage?.startsWith('en')) {
      return 'en';
    }

    return this.defaultLanguage;
  }

  private normalizeLanguage(language: string | null | undefined): LanguageCode {
    if (this.isSupportedLanguage(language)) {
      return language.toLowerCase() as LanguageCode;
    }

    return this.defaultLanguage;
  }
}

