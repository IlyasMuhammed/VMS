/**
 * Turns what a form control is failing (Angular's validation errors) into a catalogue message ID and its values,
 * so every form says the same thing the same way. No framework imports; tested under Node.
 */

export interface FormError {
  /** A catalogue ID (`VAL-GEN-001`), or `server` when the API refused the value and its own message is the text. */
  code: string;
  params: Record<string, unknown>;
  /** The text, for `server` errors and for validators that carry their own wording (`{ message: '…' }`). */
  message?: string;
}

/** The order errors are shown in when a control fails several checks: what the server said, then the plainest mistake first. */
const ORDER = ['server', 'required', 'email', 'minlength', 'maxlength', 'min', 'max', 'pattern', 'futureDate', 'mismatch'];

/**
 * The one error to show for a control, or null if it has none. `label` names the field in the message
 * ("Legal name is required."); `own` gives wording for a custom validator's key (`{ weakPassword: 'At least 8 characters…' }`).
 */
export function classify(errors: Record<string, unknown> | null | undefined, label: string, own: Record<string, string> = {}): FormError | null {
  if (!errors) return null;
  const keys = Object.keys(errors);
  if (keys.length === 0) return null;
  const key = ORDER.find((k) => k in errors) ?? keys[0];
  const detail = errors[key] as Record<string, unknown> | string | boolean | null;
  const field = { Field: label };

  switch (key) {
    case 'server':
      return { code: 'server', params: {}, message: typeof detail === 'string' ? detail : String((detail as { message?: unknown } | null)?.message ?? '') };
    case 'required':
      return { code: 'VAL-GEN-001', params: field };
    case 'email':
      return { code: 'VAL-GEN-002', params: {} };
    case 'minlength':
      return { code: 'VAL-GEN-004', params: { ...field, Min: (detail as { requiredLength?: number })?.requiredLength } };
    case 'maxlength':
      return { code: 'VAL-GEN-003', params: { ...field, Max: (detail as { requiredLength?: number })?.requiredLength } };
    case 'min':
      return { code: 'VAL-GEN-006', params: { ...field, Min: (detail as { min?: number })?.min } };
    case 'max':
      return { code: 'VAL-GEN-007', params: { ...field, Max: (detail as { max?: number })?.max } };
    case 'pattern':
      return { code: 'VAL-GEN-005', params: field };
    case 'futureDate':
      return { code: 'VAL-GEN-008', params: field };
    case 'mismatch':
      return { code: 'VAL-GEN-009', params: {} };
    default: {
      const carried = typeof detail === 'object' && detail !== null && typeof (detail as { message?: unknown }).message === 'string' ? (detail as { message: string }).message : undefined;
      const message = own[key] ?? carried;
      return message ? { code: 'custom', params: {}, message } : { code: 'VAL-GEN-010', params: field };
    }
  }
}
