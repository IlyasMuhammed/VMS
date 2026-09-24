import { Injectable, inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { errorMessage } from './api-error';

/**
 * The one way to tell the user how something went, so every screen does it the same way: a toast at the top right,
 * worded the same, and staying as long as its kind deserves.
 *
 *  - `success`: what was done, in the past tense ("User deleted"). Fades after 3 seconds.
 *  - `info`: something worth knowing that is not an outcome. 4 seconds.
 *  - `warn`: it worked, but check something ("2 logos were not saved"). 6 seconds, or longer if asked.
 *  - `error`: it did not work, and why, in the server's own words. 8 seconds: long enough to read, not so long it piles up.
 *
 * A problem with one field belongs next to that field (`vms-field`), not here; use `error` for what has no field.
 */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly toast = inject(MessageService);

  success(detail: string, summary = 'Done'): void {
    this.toast.add({ severity: 'success', summary, detail, life: 3000 });
  }

  info(detail: string, summary = 'Note'): void {
    this.toast.add({ severity: 'info', summary, detail, life: 4000 });
  }

  warn(detail: string, summary = 'Check this', life = 6000): void {
    this.toast.add({ severity: 'warn', summary, detail, life });
  }

  /** `source` is what was caught (an HTTP error, whose message from the server is shown) or a plain sentence. */
  error(source: unknown, summary = 'Error'): void {
    const detail = typeof source === 'string' ? source : errorMessage(source);
    this.toast.add({ severity: 'error', summary, detail, life: 8000 });
  }
}
