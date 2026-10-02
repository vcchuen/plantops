import { Routes } from '@angular/router';

export const ASSETS_ROUTES: Routes = [
  {
    path: '',
    title: 'Assets · PlantOps',
    loadComponent: () => import('./assets-list.page').then((m) => m.AssetsListPage),
  },
  {
    path: ':id',
    title: 'Asset · PlantOps',
    loadComponent: () => import('./asset-detail.page').then((m) => m.AssetDetailPage),
  },
];
