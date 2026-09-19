import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Mirrors the server's PasswordPolicy: 8+ chars with upper, lower, digit and a special character. */
export const strongPassword: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const v: string = control.value ?? '';
  if (!v) return null; // `required` reports emptiness
  const ok = v.length >= 8 && /[A-Z]/.test(v) && /[a-z]/.test(v) && /\d/.test(v) && /[^A-Za-z0-9]/.test(v);
  return ok ? null : { weakPassword: true };
};

export const PASSWORD_HINT = 'At least 8 characters with an uppercase letter, a lowercase letter, a digit and a special character.';

/** Group validator: `confirm` must equal `password`. */
export const passwordsMatch =
  (passwordKey = 'password', confirmKey = 'confirm'): ValidatorFn =>
  (group: AbstractControl): ValidationErrors | null =>
    group.get(passwordKey)?.value === group.get(confirmKey)?.value ? null : { mismatch: true };
