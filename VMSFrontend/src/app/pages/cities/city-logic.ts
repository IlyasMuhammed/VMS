/** The vocabulary of the City screen (FSD §16, §48.3 screen 8) that does not depend on Angular. No framework
 * imports: tested under Node (scripts/check-cities.test.mjs). */

export function statusSeverity(status: string): 'success' | 'danger' {
  return status === 'Active' ? 'success' : 'danger';
}

/** §16's own display rule, reused everywhere a city is shown: `Lahore (LHR)`. */
export function cityDisplay(cityName: string, abbreviation: string): string {
  return `${cityName} (${abbreviation})`;
}

/** Mirrors the server's own `^[A-Z]{2,5}$` (case-insensitive as typed; the server upper-cases it before checking). */
export function isAbbreviation(value: string): boolean {
  return /^[A-Za-z]{2,5}$/.test(value.trim());
}
