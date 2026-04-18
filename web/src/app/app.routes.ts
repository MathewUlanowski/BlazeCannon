import { Routes } from '@angular/router';

export const APP_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('./pages/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent,
      ),
  },
  {
    path: 'traffic',
    loadComponent: () =>
      import('./pages/traffic-inspector/traffic-inspector.component').then(
        (m) => m.TrafficInspectorComponent,
      ),
  },
  {
    path: 'replay',
    loadComponent: () =>
      import('./pages/replay/replay.component').then((m) => m.ReplayComponent),
  },
  {
    path: 'scanner',
    loadComponent: () =>
      import('./pages/placeholder/placeholder.component').then(
        (m) => m.PlaceholderComponent,
      ),
    data: { title: 'Scanner' },
  },
  {
    path: 'workbench',
    loadComponent: () =>
      import('./pages/placeholder/placeholder.component').then(
        (m) => m.PlaceholderComponent,
      ),
    data: { title: 'Payload Workbench' },
  },
  {
    path: 'browser',
    loadComponent: () =>
      import('./pages/placeholder/placeholder.component').then(
        (m) => m.PlaceholderComponent,
      ),
    data: { title: 'Browser Engine' },
  },
  { path: '**', redirectTo: '' },
];
