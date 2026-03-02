import { Routes } from '@angular/router';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { languageRouteGuard } from './core/guards/language-route.guard';
import { LanguageRedirectComponent } from './core/components/language-redirect.component';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    component: LanguageRedirectComponent,
    pathMatch: 'full'
  },
  {
    path: ':lang',
    canActivate: [languageRouteGuard],
    children: [
      {
        path: 'auth/login',
        loadComponent: () =>
          import('./auth/login.component').then(m => m.LoginComponent)
      },
      {
        path: 'auth/register',
        loadComponent: () =>
          import('./auth/register.component').then(m => m.RegisterComponent)
      },
      {
        path: '',
        component: AppShellComponent,
        canActivate: [authGuard],
        children: [
          {
            path: '',
            redirectTo: 'dashboard',
            pathMatch: 'full'
          },
          {
            path: 'dashboard',
            loadComponent: () =>
              import('./dashboard/dashboard-page.component').then(m => m.DashboardPageComponent)
          },
          {
            path: 'datasets',
            loadChildren: () =>
              import('./features/datasets/datasets.routes').then(m => m.DATASETS_ROUTES)
          },
          {
            path: 'explore',
            loadComponent: () =>
              import('./features/workspace/pages/workspace-placeholder-page/workspace-placeholder-page.component')
                .then(m => m.WorkspacePlaceholderPageComponent),
            data: { title: 'Explore' }
          },
          {
            path: 'recommendations',
            loadComponent: () =>
              import('./features/workspace/pages/workspace-placeholder-page/workspace-placeholder-page.component')
                .then(m => m.WorkspacePlaceholderPageComponent),
            data: { title: 'Recommendations' }
          },
          {
            path: 'charts',
            loadComponent: () =>
              import('./features/workspace/pages/workspace-placeholder-page/workspace-placeholder-page.component')
                .then(m => m.WorkspacePlaceholderPageComponent),
            data: { title: 'Charts' }
          },
          {
            path: 'insights',
            loadComponent: () =>
              import('./features/workspace/pages/workspace-placeholder-page/workspace-placeholder-page.component')
                .then(m => m.WorkspacePlaceholderPageComponent),
            data: { title: 'Insights' }
          },
          {
            path: 'automations',
            loadComponent: () =>
              import('./features/workspace/pages/workspace-placeholder-page/workspace-placeholder-page.component')
                .then(m => m.WorkspacePlaceholderPageComponent),
            data: { title: 'Automations' }
          },
          {
            path: 'profile',
            loadComponent: () =>
              import('./features/profile/pages/profile-page/profile-page.component').then(m => m.ProfilePageComponent)
          },
          {
            path: 'upgrade',
            redirectTo: 'profile'
          }
        ]
      }
    ]
  },
  {
    path: '**',
    redirectTo: '/pt-br/auth/login'
  }
];
