import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type Theme = 'legacy' | 'light' | 'dark';
const STORAGE_KEY = 'insight-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private themeSubject = new BehaviorSubject<Theme>(this.getInitialTheme());
  readonly theme$ = this.themeSubject.asObservable();

  constructor() {
    // Ensure the document has the proper class if the service is constructed later
    this.applyThemeClass(this.themeSubject.value);

    // If user hasn't chosen a theme, follow system preference changes (only when not using legacy)
    if (window.matchMedia) {
      const mq = window.matchMedia('(prefers-color-scheme: dark)');
      mq.addEventListener?.('change', (e: MediaQueryListEvent) => {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (!stored) {
          // if user hasn't chosen, switch between light/dark (legacy remains opt-in)
          this.setTheme(e.matches ? 'dark' : 'light');
        }
      });
    }
  }

  private getInitialTheme(): Theme {
    const stored = localStorage.getItem(STORAGE_KEY) as Theme | null;
    if (stored === 'legacy' || stored === 'light' || stored === 'dark') {
      return stored;
    }
    // Default to legacy (user requested old theme be available as default)
    return 'legacy';
  }

  setTheme(theme: Theme) {
    localStorage.setItem(STORAGE_KEY, theme);
    this.applyThemeClass(theme);
    this.themeSubject.next(theme);
  }

  toggleTheme() {
    // Cycle: legacy -> light -> dark -> legacy
    const next: Record<Theme, Theme> = { legacy: 'light', light: 'dark', dark: 'legacy' };
    this.setTheme(next[this.themeSubject.value]);
  }

  get currentTheme(): Theme {
    return this.themeSubject.value;
  }

  private applyThemeClass(theme: Theme) {
    document.documentElement.classList.remove('theme-legacy', 'theme-light', 'theme-dark');
    document.documentElement.classList.add(theme === 'dark' ? 'theme-dark' : (theme === 'light' ? 'theme-light' : 'theme-legacy'));
  }
}
