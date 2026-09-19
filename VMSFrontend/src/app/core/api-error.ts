import { HttpErrorResponse } from '@angular/common/http';

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
