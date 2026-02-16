import { Routes } from '@angular/router';
import { ShellComponent } from './layout/shell/shell.component';
import { languageRouteGuard } from './core/guards/language-route.guard';
import { LanguageRedirectComponent } from './core/components/language-redirect.component';

export const routes: Routes = [
  {
    path: '',
    component: LanguageRedirectComponent,
    pathMatch: 'full'
  },
  {
    path: ':lang',
    component: ShellComponent,
    canActivate: [languageRouteGuard],
    children: [
      {
        path: '',
        redirectTo: 'datasets/new',
        pathMatch: 'full'
      },
      {
        path: 'datasets',
        loadChildren: () =>
          import('./features/datasets/datasets.routes').then(m => m.DATASETS_ROUTES)
      }
    ]
  },
  {
    path: '**',
    redirectTo: '/pt-br/datasets/new'
  }
];
