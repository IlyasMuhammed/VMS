import { HttpErrorResponse } from '@angular/common/http';
import { ApiFieldError, apiErrorsOf } from './message-format';

/** The server's own message when it sent one (ApiResponse.message), else a generic fallback. */
export function errorMessage(err: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) return 'Cannot reach the server.';
    const message = (err.error as { message?: string } | null)?.message;
    if (message) return message;
    if (err.status === 429) return 'Too many attempts. Please wait a moment and try again.';
  }
  return fallback;
}

/** Every problem the API tied to a field (or to the whole form), when it rejected the input. Empty for any other failure. */
export function apiErrors(err: unknown): ApiFieldError[] {
  return err instanceof HttpErrorResponse ? apiErrorsOf(err.error) : [];
}
