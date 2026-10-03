import { Routes } from '@angular/router';

export const WORK_ORDERS_ROUTES: Routes = [
  {
    path: '',
    title: 'Work orders · PlantOps',
    loadComponent: () => import('./work-orders-list.page').then((m) => m.WorkOrdersListPage),
  },
  // Before ':id', or "new" and "queue" would be captured as ids.
  {
    path: 'new',
    title: 'Raise work order · PlantOps',
    loadComponent: () => import('./work-order-new.page').then((m) => m.WorkOrderNewPage),
  },
  {
    path: 'queue',
    title: 'My queue · PlantOps',
    loadComponent: () => import('./work-orders-queue.page').then((m) => m.WorkOrdersQueuePage),
  },
  {
    path: ':id',
    title: 'Work order · PlantOps',
    loadComponent: () => import('./work-order-detail.page').then((m) => m.WorkOrderDetailPage),
  },
];
