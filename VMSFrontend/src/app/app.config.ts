import { ApplicationConfig, provideAppInitializer, inject, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { providePrimeNG } from 'primeng/config';
import { ConfirmationService, MessageService } from 'primeng/api';

import { routes } from './app.routes';
import { VmsPreset } from './core/theme/vms-preset';
import { authInterceptor } from './core/auth.interceptor';
import { AuthService } from './core/auth.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    // Colours come from CSS tokens (src/styles/tokens); dark mode follows <html data-mode="dark">.
    providePrimeNG({ theme: { preset: VmsPreset, options: { darkModeSelector: "[data-mode='dark']" } } }),
    MessageService,
    ConfirmationService,
    // Restores the session (refresh token -> new access token -> /me) before the first route renders.
    provideAppInitializer(() => inject(AuthService).restoreSession()),
  ],
};
