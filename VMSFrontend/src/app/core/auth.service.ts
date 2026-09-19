import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, firstValueFrom, map, of, shareReplay, tap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, CurrentUser, LoginResponse, RefreshResponse } from './models';

// The refresh token lives in sessionStorage — it dies with the tab, which limits how long a stolen
// one is useful. The access token is kept in memory only, so it is never written to disk.
const REFRESH_KEY = 'vms.refreshToken';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly base = `${environment.apiUrl}/auth`;

  private accessToken: string | null = null;
  private refreshInFlight$: Observable<string> | null = null;

  readonly user = signal<CurrentUser | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly isSuperAdmin = computed(() => this.user()?.isSuperAdmin === true);

  get token(): string | null {
    return this.accessToken;
  }

  hasPermission(code: string): boolean {
    const u = this.user();
    return !!u && (u.isSuperAdmin || u.permissions.includes(code));
  }

  login(email: string, password: string): Observable<CurrentUser> {
    return this.http.post<ApiResponse<LoginResponse>>(`${this.base}/login`, { email, password }).pipe(
      tap((res) => this.setSession(res.data.accessToken, res.data.refreshToken, res.data.user)),
      map((res) => res.data.user),
    );
  }

  /** Called once at startup: turns a stored refresh token back into a live session, if it is still good. */
  async restoreSession(): Promise<void> {
    if (!sessionStorage.getItem(REFRESH_KEY)) return;
    try {
      await firstValueFrom(this.refresh());
      const me = await firstValueFrom(this.http.get<ApiResponse<CurrentUser>>(`${this.base}/me`));
      this.user.set(me.data);
    } catch {
      this.clearSession();
    }
  }

  /**
   * Exchanges the refresh token for a new pair. Refresh tokens rotate and are single-use, so
   * concurrent callers must share one request — a second call would replay a spent token, which the
   * server treats as theft and answers by ending every session.
   */
  refresh(): Observable<string> {
    if (this.refreshInFlight$) return this.refreshInFlight$;

    const refreshToken = sessionStorage.getItem(REFRESH_KEY);
    if (!refreshToken) return throwError(() => new Error('No refresh token'));

    this.refreshInFlight$ = this.http
      .post<ApiResponse<RefreshResponse>>(`${this.base}/refresh`, { refreshToken })
      .pipe(
        tap((res) => {
          this.accessToken = res.data.accessToken;
          sessionStorage.setItem(REFRESH_KEY, res.data.refreshToken);
        }),
        map((res) => res.data.accessToken),
        finalize(() => (this.refreshInFlight$ = null)),
        shareReplay(1),
      );
    return this.refreshInFlight$;
  }

  logout(): void {
    const refreshToken = sessionStorage.getItem(REFRESH_KEY);
    if (refreshToken) {
      this.http
        .post(`${this.base}/logout`, { refreshToken })
        .pipe(catchError(() => of(null)))
        .subscribe();
    }
    this.clearSession();
    this.router.navigate(['/auth/login']);
  }

  /** Drops local state without calling the API — used when the server has already ended the session. */
  clearSession(): void {
    this.accessToken = null;
    sessionStorage.removeItem(REFRESH_KEY);
    this.user.set(null);
  }

  private setSession(accessToken: string, refreshToken: string, user: CurrentUser): void {
    this.accessToken = accessToken;
    sessionStorage.setItem(REFRESH_KEY, refreshToken);
    this.user.set(user);
  }
}
