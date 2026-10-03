import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'status' },
  {
    path: 'status',
    title: 'System status · PlantOps',
    loadComponent: () => import('./features/status/status.page').then((m) => m.StatusPage),
  },
  {
    path: 'assets',
    canMatch: [authGuard],
    loadChildren: () => import('./features/assets/assets.routes').then((m) => m.ASSETS_ROUTES),
  },
  {
    path: 'work-orders',
    canMatch: [authGuard],
    loadChildren: () =>
      import('./features/work-orders/work-orders.routes').then((m) => m.WORK_ORDERS_ROUTES),
  },
  {
    path: '**',
    title: 'Page not found · PlantOps',
    loadComponent: () => import('./features/not-found/not-found.page').then((m) => m.NotFoundPage),
  },
];
