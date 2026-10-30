import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./pages/home/home').then((m) => m.Home), title: 'Klavyra' },
  { path: 'projects', loadComponent: () => import('./pages/projects/projects').then((m) => m.Projects), title: 'Klavyra · Mi repertorio' },
  { path: 'projects/:id', loadComponent: () => import('./pages/project/project').then((m) => m.ProjectPage), title: 'Klavyra · Proyecto' },
  { path: '**', redirectTo: '' },
];
