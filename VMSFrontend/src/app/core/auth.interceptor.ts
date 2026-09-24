import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { clientTimeZone } from './datetime/datetime';

const isAnonymousAuthCall = (url: string) =>
  ['/auth/login', '/auth/refresh', '/auth/logout', '/auth/forgot-password', '/auth/reset-password', '/auth/accept-invite'].some((p) =>
    url.endsWith(p),
  );

// Every API call says which time zone the viewer is in, so exports and printouts built on the server can
// show that person's own local time with the zone named (NFR-DT-07). Screens are formatted in the browser.
const withToken = (req: HttpRequest<unknown>, token: string | null) => {
  const zone = clientTimeZone();
  const headers: Record<string, string> = zone ? { 'X-Time-Zone': zone } : {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  return req.clone({ setHeaders: headers });
};

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // Only ever send the token to our own API.
  if (!req.url.startsWith(environment.apiUrl) || isAnonymousAuthCall(req.url)) return next(req);

  return next(withToken(req, auth.token)).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401) return throwError(() => err);

      // Access token expired (or the tenant was cut off): try one refresh, then replay the request once.
      return auth.refresh().pipe(
        switchMap((token) => next(withToken(req, token))),
        catchError((refreshErr) => {
          auth.clearSession();
          router.navigate(['/auth/login']);
          return throwError(() => refreshErr);
        }),
      );
    }),
  );
};
