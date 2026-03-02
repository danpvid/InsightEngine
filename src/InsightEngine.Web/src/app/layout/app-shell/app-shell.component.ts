import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSelectModule } from '@angular/material/select';
import { ThemeToggleComponent } from '../../shared/components/theme-toggle/theme-toggle.component';
import { LanguageCode } from '../../core/models/language.model';
import { LanguageService } from '../../core/services/language.service';
import { AuthService } from '../../core/services/auth.service';
import { AuthUser } from '../../core/models/auth.model';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatSidenavModule,
    MatListModule,
    MatMenuModule,
    MatChipsModule,
    MatTooltipModule,
    MatSelectModule,
    ThemeToggleComponent
  ],
  templateUrl: './app-shell.component.html',
  styleUrls: ['./app-shell.component.scss']
})
export class AppShellComponent implements OnInit {
  collapsed = false;

  readonly menuItems = [
    { label: 'Datasets', icon: 'storage', route: 'datasets' },
    { label: 'Dashboard', icon: 'dashboard', route: 'dashboard' },
    { label: 'Explore', icon: 'travel_explore', route: 'explore' },
    { label: 'Recommendations', icon: 'auto_awesome', route: 'recommendations' },
    { label: 'Charts', icon: 'bar_chart', route: 'charts' },
    { label: 'Insights', icon: 'lightbulb', route: 'insights' },
    { label: 'Automations', icon: 'bolt', route: 'automations' }
  ];

  constructor(
    private readonly languageService: LanguageService,
    private readonly router: Router,
    private readonly authService: AuthService
  ) {}

  ngOnInit(): void {
    if (this.authService.isAuthenticated && !this.authService.currentUser) {
      this.authService.loadMe().subscribe();
    }
  }

  get currentLanguage(): LanguageCode {
    const firstSegment = this.router.url
      .split('?')[0]
      .split('#')[0]
      .split('/')
      .filter(segment => segment.length > 0)[0];

    if (this.languageService.isSupportedLanguage(firstSegment)) {
      return firstSegment;
    }

    return this.languageService.currentLanguage;
  }

  get newDatasetLink(): string[] {
    return ['/', this.currentLanguage, 'datasets', 'new'];
  }

  get profileLink(): string[] {
    return ['/', this.currentLanguage, 'profile'];
  }

  get user(): AuthUser | null {
    return this.authService.currentUser;
  }

  get planLabel(): string {
    return this.user?.plan ?? 'Free';
  }

  get avatarText(): string {
    const name = this.user?.displayName || this.user?.email || 'U';
    return name.substring(0, 1).toUpperCase();
  }

  get sidebarWidth(): string {
    return this.collapsed ? '72px' : '260px';
  }

  async onLanguageChange(language: string): Promise<void> {
    await this.languageService.switchLanguage(language, this.router);
  }

  toggleSidebar(): void {
    this.collapsed = !this.collapsed;
  }

  openUpgrade(): void {
    this.router.navigate(['/', this.currentLanguage, 'profile'], { queryParams: { tab: 'plan' } });
  }

  logout(): void {
    this.authService.logout().subscribe();
  }
}
