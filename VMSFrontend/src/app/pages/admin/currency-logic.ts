/** The vocabulary and format rules of the Currency Setup screen (FSD §13A, §48.8) that do not depend on Angular
 * — mirrors the server's own `^[A-Z]{3}$` currency-code check. No framework imports: tested under Node
 * (scripts/check-currency-setup.test.mjs). */

export function statusSeverity(status: string): 'success' | 'danger' {
  return status === 'Active' ? 'success' : 'danger';
}

/** Mirrors the server's own `CodePattern` (case-insensitive as typed; the server upper-cases it before checking). */
export function isCurrencyCode(value: string): boolean {
  return /^[A-Za-z]{3}$/.test(value.trim());
}
