import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { isFutureDate, toDateOnly } from './datetime';

/**
 * "Not in the future" for a business date, judged against the viewer's own today (NFR-DT-04), so a user
 * ahead of UTC is not blocked from entering today. Accepts `YYYY-MM-DD` or the `Date` a date picker gives.
 */
export const notFutureDate: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value: unknown = control.value;
  if (!value) return null; // `required` reports emptiness
  const day = value instanceof Date ? toDateOnly(value) : String(value);
  return isFutureDate(day) ? { futureDate: true } : null;
};
