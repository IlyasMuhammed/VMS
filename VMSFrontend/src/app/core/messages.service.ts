import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiFieldError, DEFAULT_MESSAGES, formatMessage } from './message-format';
import { ApiResponse } from './models';

/**
 * The message catalogue (FSD §12.2): every message the API can show, by ID. Fetched once at start-up from
 * `GET /api/messages`, so a message reworded or translated on the server reaches every screen with no release.
 * Until it arrives (or if it never does) the platform's own wording for the common checks is used.
 */
@Injectable({ providedIn: 'root' })
export class MessagesService {
  private readonly http = inject(HttpClient);
  private readonly catalogue = signal<Record<string, string>>({ ...DEFAULT_MESSAGES });

  /** Fetches the catalogue in the browser's language, falling back to English. Never throws: a failure keeps the defaults. */
  async load(locale: string = document.documentElement.lang || 'en'): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.http.get<ApiResponse<{ messages: Record<string, string> }>>(`${environment.apiUrl}/messages`, { params: new HttpParams().set('locale', locale) }),
      );
      this.catalogue.set({ ...DEFAULT_MESSAGES, ...response.data.messages });
    } catch {
      /* keep what we have */
    }
  }

  has(code: string): boolean {
    return code in this.catalogue();
  }

  /** The message for an ID with its values filled in. An unknown ID reads as the ID, so a gap is visible rather than blank. */
  text(code: string, params?: Record<string, unknown> | null): string {
    return formatMessage(this.catalogue()[code] ?? code, params);
  }

  /** An error from the API in the screen's own wording: from the catalogue when the ID is known, else as the API worded it. */
  describe(error: ApiFieldError): string {
    return this.has(error.code) ? this.text(error.code, error.params) : error.message;
  }
}
