import { Routes } from '@angular/router';

// Supervisor/admin only, but the route is not role-guarded in the client: the API returns 403
// and the page shows it. The nav link is hidden as a convenience, not as security.
export const REPORTS_ROUTES: Routes = [
  {
    path: '',
    title: 'Reports · PlantOps',
    loadComponent: () => import('./reports.page').then((m) => m.ReportsPage),
  },
];
