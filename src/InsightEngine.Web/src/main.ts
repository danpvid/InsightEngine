import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

// Apply persisted or system theme before Angular boot to avoid flash
(() => {
  try {
    const STORAGE_KEY = 'insight-theme';
    const stored = localStorage.getItem(STORAGE_KEY);
    const prefersDark = typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia('(prefers-color-scheme: dark)').matches
      : false;
    const theme = stored === 'dark' || stored === 'light' ? stored : (prefersDark ? 'dark' : 'light');
    document.documentElement.classList.add(theme === 'dark' ? 'theme-dark' : 'theme-light');
  } catch (e) {
    /* ignore */
  }
})();

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
