import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Access, canAccess } from './access';
import { AuthService } from './auth.service';

/** Signed in, or off to the sign-in page with a note of where they were going, so they come back to it. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isAuthenticated()) return true;
  const returnUrl = state.url && state.url !== '/' ? state.url : undefined;
  return inject(Router).createUrlTree(['/auth/login'], returnUrl ? { queryParams: { returnUrl } } : {});
};

/** Login, forgot-password etc. are pointless once signed in — send the user home. */
export const guestGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  // An invite link must work even if someone else is signed in on this browser.
  if (route.routeConfig?.path === 'accept-invite') return true;
  return auth.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

/**
 * Enforces the `data.access` a route declares (see `page()` in app.routes.ts). The same declaration builds
 * the menu, so what is hidden and what is refused cannot disagree.
 */
export const accessGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  return canAccess(route.data['access'] as Access | undefined, auth.user()) ? true : inject(Router).createUrlTree(['/forbidden']);
};
