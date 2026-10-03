import { Routes } from '@angular/router';

export const INVENTORY_ROUTES: Routes = [
  {
    path: '',
    title: 'Inventory · PlantOps',
    loadComponent: () => import('./parts-list.page').then((m) => m.PartsListPage),
  },
  {
    path: ':id',
    title: 'Part · PlantOps',
    loadComponent: () => import('./part-detail.page').then((m) => m.PartDetailPage),
  },
];
