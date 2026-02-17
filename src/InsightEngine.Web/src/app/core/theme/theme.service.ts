import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type Theme = 'light' | 'dark';
const STORAGE_KEY = 'insight-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private themeSubject = new BehaviorSubject<Theme>(this.getInitialTheme());
  readonly theme$ = this.themeSubject.asObservable();

  constructor() {
    // Ensure the document has the proper class if the service is constructed later
    this.applyThemeClass(this.themeSubject.value);

    // If user hasn't chosen a theme, follow system preference changes
    if (window.matchMedia) {
      const mq = window.matchMedia('(prefers-color-scheme: dark)');
      mq.addEventListener?.('change', (e: MediaQueryListEvent) => {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (!stored) {
          this.setTheme(e.matches ? 'dark' : 'light');
        }
      });
    }
  }

  private getInitialTheme(): Theme {
    const stored = localStorage.getItem(STORAGE_KEY) as Theme | null;
    if (stored === 'light' || stored === 'dark') {
      return stored;
    }
    const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    return prefersDark ? 'dark' : 'light';
  }

  setTheme(theme: Theme) {
    localStorage.setItem(STORAGE_KEY, theme);
    this.applyThemeClass(theme);
    this.themeSubject.next(theme);
  }

  toggleTheme() {
    this.setTheme(this.themeSubject.value === 'dark' ? 'light' : 'dark');
  }

  get currentTheme(): Theme {
    return this.themeSubject.value;
  }

  private applyThemeClass(theme: Theme) {
    document.documentElement.classList.remove('theme-light', 'theme-dark');
    document.documentElement.classList.add(theme === 'dark' ? 'theme-dark' : 'theme-light');
  }
}
