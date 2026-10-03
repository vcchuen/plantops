import { Routes } from '@angular/router';

export const WORK_ORDERS_ROUTES: Routes = [
  {
    path: '',
    title: 'Work orders · PlantOps',
    loadComponent: () => import('./work-orders-list.page').then((m) => m.WorkOrdersListPage),
  },
  // Before ':id', or "new" would be captured as an id.
  {
    path: 'new',
    title: 'Raise work order · PlantOps',
    loadComponent: () => import('./work-order-new.page').then((m) => m.WorkOrderNewPage),
  },
  {
    path: ':id',
    title: 'Work order · PlantOps',
    loadComponent: () => import('./work-order-detail.page').then((m) => m.WorkOrderDetailPage),
  },
];
