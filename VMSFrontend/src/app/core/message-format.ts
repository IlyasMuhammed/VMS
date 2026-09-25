/**
 * Messages by ID (FSD §12.2): the API's rejections carry an ID and the values that filled the message, and
 * the screen words them from the catalogue it fetched (`GET /api/messages`), so a reworded or translated
 * message reaches every screen without a release. No framework imports; tested under Node.
 */

/** One problem with what was entered, as the API reports it. */
export interface ApiFieldError {
  /** The field, in the request's own spelling (`legalName`, `address.city`). Empty when the problem is with the whole form. */
  field?: string | null;
  code: string;
  message: string;
  params?: Record<string, string | null> | null;
}

/**
 * The platform's own wording for the checks every form has. Used until the catalogue has loaded, and if it
 * never does (the API is unreachable), so a form never shows a bare ID. Kept identical to the `VAL-GEN-*` entries of
 * the API's `messages.en.json`; a test compares them.
 */
export const DEFAULT_MESSAGES: Record<string, string> = {
  'VAL-GEN-001': '{Field} is required.',
  'VAL-GEN-002': 'Enter a valid email address.',
  'VAL-GEN-003': '{Field} can be at most {Max} characters.',
  'VAL-GEN-004': '{Field} must be at least {Min} characters.',
  'VAL-GEN-005': '{Field} is not in the expected format.',
  'VAL-GEN-006': '{Field} must be at least {Min}.',
  'VAL-GEN-007': '{Field} must be at most {Max}.',
  'VAL-GEN-008': '{Field} cannot be in the future.',
  'VAL-GEN-009': 'The two values do not match.',
  'VAL-GEN-010': '{Field} is not valid.',
  'VAL-GEN-011': 'Please correct the highlighted fields.',
  'VAL-GEN-012': 'Only one {Field} can be marked primary.',
  'VAL-GEN-013': 'This {Field} belongs to {Code} — {Name}. Open that record instead.',
  'VAL-GEN-014': 'This record was changed by {User} at {At} while you were editing it. Reload to see their changes, then make yours again.',
  'VAL-GEN-015': '{Field} must be one of: {Allowed}.',
  'VAL-GEN-016': '{Field} must be a future date.',
  'VAL-GEN-017': '{Field} is read-only once saved.',
  'VAL-GEN-018': '{n} other partners have this {Field}. Review them before saving.',
  'VAL-GEN-019': 'This {Field} is listed twice.',
  'VAL-GEN-020': '{n} rows match, more than the {Max} an export can hold. Narrow the filter and try again.',
  'VAL-GEN-021': 'An Idempotency-Key header is required for this request.',
};

const PLACEHOLDER = /\{([A-Za-z_][A-Za-z0-9_]*)\}/g;

/** The names in `{Name}` placeholders, in order, without repeats. */
export function placeholders(template: string): string[] {
  return [...new Set([...template.matchAll(PLACEHOLDER)].map((m) => m[1]))];
}

/**
 * Fills `{Name}` placeholders. A placeholder with no value is left as written rather than thrown: a screen must
 * never fail to render an error, and the API (which does throw) is where a missing value is caught.
 */
export function formatMessage(template: string, params?: Record<string, unknown> | null): string {
  return template.replace(PLACEHOLDER, (whole, name: string) => {
    const value = params?.[name];
    return value === undefined ? whole : value === null ? '' : String(value);
  });
}

/** The list of field errors in an API error body, or none if it does not carry any. */
export function apiErrorsOf(body: unknown): ApiFieldError[] {
  const errors = (body as { errors?: unknown } | null | undefined)?.errors;
  if (!Array.isArray(errors)) return [];
  return errors.filter((e): e is ApiFieldError => typeof e?.code === 'string' && typeof e?.message === 'string');
}
