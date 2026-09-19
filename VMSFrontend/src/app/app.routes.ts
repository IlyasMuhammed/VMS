import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard, superAdminGuard } from './core/guards';

export const routes: Routes = [
  {
    path: 'auth',
    canActivate: [guestGuard],
    children: [
      { path: 'login', loadComponent: () => import('./pages/auth/login.component').then((m) => m.LoginComponent) },
      { path: 'forgot-password', loadComponent: () => import('./pages/auth/forgot-password.component').then((m) => m.ForgotPasswordComponent) },
      { path: 'reset-password', loadComponent: () => import('./pages/auth/reset-password.component').then((m) => m.ResetPasswordComponent) },
      { path: 'accept-invite', loadComponent: () => import('./pages/auth/accept-invite.component').then((m) => m.AcceptInviteComponent) },
      { path: '', pathMatch: 'full', redirectTo: 'login' },
    ],
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./pages/dashboard/dashboard.component').then((m) => m.DashboardComponent) },
      { path: 'profile', loadComponent: () => import('./pages/profile/profile.component').then((m) => m.ProfileComponent) },
      {
        path: 'users',
        canActivate: [permissionGuard('USER_VIEW')],
        loadComponent: () => import('./pages/users/users.component').then((m) => m.UsersComponent),
      },
      {
        path: 'roles',
        canActivate: [permissionGuard('ROLE_VIEW')],
        loadComponent: () => import('./pages/roles/roles.component').then((m) => m.RolesComponent),
      },
      {
        path: 'roles/:id',
        canActivate: [permissionGuard('ROLE_VIEW')],
        loadComponent: () => import('./pages/roles/role-detail.component').then((m) => m.RoleDetailComponent),
      },
      {
        path: 'tenants',
        canActivate: [superAdminGuard],
        loadComponent: () => import('./pages/tenants/tenants.component').then((m) => m.TenantsComponent),
      },
      { path: 'forbidden', loadComponent: () => import('./pages/forbidden/forbidden.component').then((m) => m.ForbiddenComponent) },
    ],
  },
  { path: '**', redirectTo: '' },
];
