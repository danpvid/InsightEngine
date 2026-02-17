import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

// Apply persisted theme (legacy default) before Angular boot to avoid flash
(() => {
  try {
    const STORAGE_KEY = 'insight-theme';
    const stored = localStorage.getItem(STORAGE_KEY);
    const theme = stored === 'legacy' || stored === 'light' || stored === 'dark' ? stored : 'legacy';
    document.documentElement.classList.add(theme === 'dark' ? 'theme-dark' : (theme === 'light' ? 'theme-light' : 'theme-legacy'));
  } catch (e) {
    /* ignore */
  }
})();

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
