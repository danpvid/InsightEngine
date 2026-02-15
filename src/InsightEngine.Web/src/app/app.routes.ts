import { Routes } from '@angular/router';
import { ShellComponent } from './layout/shell/shell.component';

export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    children: [
      {
        path: '',
        redirectTo: '/datasets/new',
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
    redirectTo: '/datasets/new'
  }
];
