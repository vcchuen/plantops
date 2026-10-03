import { Routes } from '@angular/router';

export const PM_SCHEDULES_ROUTES: Routes = [
  {
    path: '',
    title: 'PM schedules · PlantOps',
    loadComponent: () => import('./pm-schedules-list.page').then((m) => m.PmSchedulesListPage),
  },
  // Before ':id', or "new" would be captured as an id.
  {
    path: 'new',
    title: 'New PM schedule · PlantOps',
    loadComponent: () => import('./pm-schedule-form.page').then((m) => m.PmScheduleFormPage),
  },
  {
    path: ':id/edit',
    title: 'Edit PM schedule · PlantOps',
    loadComponent: () => import('./pm-schedule-form.page').then((m) => m.PmScheduleFormPage),
  },
  {
    path: ':id',
    title: 'PM schedule · PlantOps',
    loadComponent: () => import('./pm-schedule-detail.page').then((m) => m.PmScheduleDetailPage),
  },
];
