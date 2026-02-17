import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ThemeService } from '../../../core/theme/theme.service';

@Component({
  selector: 'app-theme-toggle',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, MatTooltipModule],
  template: `
    <button
      mat-icon-button
      (click)="toggle()"
      [attr.aria-pressed]="(themeService.theme$ | async) === 'dark'"
      [matTooltip]="(themeService.theme$ | async) === 'dark' ? 'Switch to light' : 'Switch to dark'"
      aria-label="Toggle theme"
    >
      <mat-icon>{{ (themeService.theme$ | async) === 'dark' ? 'dark_mode' : 'light_mode' }}</mat-icon>
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
  toggle() { this.themeService.toggleTheme(); }
}
