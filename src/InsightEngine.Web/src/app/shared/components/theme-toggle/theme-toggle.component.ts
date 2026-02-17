import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ThemeService } from '../../../core/theme/theme.service';
import { map } from 'rxjs/operators';

@Component({
  selector: 'app-theme-toggle',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, MatTooltipModule],
  template: `
    <button
      mat-icon-button
      (click)="toggle()"
      [attr.aria-pressed]="(themeService.theme$ | async) === 'dark'"
      [matTooltip]="(tooltipText$ | async) || ''"
      aria-label="Toggle theme"
    >
      <mat-icon>{{ icon$ | async }}</mat-icon>
    </button>
  `,
  styles: [
    `
      button[mat-icon-button] { color: var(--text-2); }
      button[mat-icon-button]:hover { color: var(--text); }
    `
  ]
})
export class ThemeToggleComponent {
  constructor(public themeService: ThemeService) {}

  get icon$() {
    return this.themeService.theme$.pipe(map(t => t === 'dark' ? 'dark_mode' : (t === 'light' ? 'light_mode' : 'palette')));
  }

  get tooltipText$() {
    return this.themeService.theme$.pipe(map(t => {
      switch (t) {
        case 'legacy': return 'Theme: Legacy (click to switch to Light)';
        case 'light': return 'Theme: Light (click to switch to Dark)';
        case 'dark': return 'Theme: Dark (click to switch to Legacy)';
      }
    }));
  }

  toggle() { this.themeService.toggleTheme(); }
}

