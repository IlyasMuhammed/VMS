import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/auth/login']);
};

/** Login, forgot-password etc. are pointless once signed in — send the user home. */
export const guestGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  // An invite link must work even if someone else is signed in on this browser.
  if (route.routeConfig?.path === 'accept-invite') return true;
  return auth.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

export const permissionGuard =
  (code: string): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    return auth.hasPermission(code) ? true : inject(Router).createUrlTree(['/forbidden']);
  };

export const superAdminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isSuperAdmin() ? true : inject(Router).createUrlTree(['/forbidden']);
};
