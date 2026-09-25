/**
 * The vocabulary and format rules of the Customer screens (FSD §10-§15) that do not depend on Angular — mirrors
 * the server's own `CustomerFormats` regexes so the screen can say so while the person types; the server checks
 * the same things again. No framework imports: tested under Node (scripts/check-customers.test.mjs).
 */

// ── Vocabulary ─────────────────────────────────────────────────────────────────────

export const CONTACT_PURPOSES = ['Billing', 'Operations', 'POD', 'Other'] as const;
export const TAX_TYPES = ['Percentage', 'Fixed'] as const;
export const CALCULATION_BASES = ['GrossTripAmount', 'InvoiceSubtotal'] as const;
export const DUPLICATE_REFERENCE_BEHAVIOURS = ['Allow', 'Warn', 'Block'] as const;
export const INVOICE_TEMPLATE_TYPES = ['SystemStandard', 'HTMLPDF', 'DocumentTemplate', 'ReportDefinition'] as const;

/** A stored value read as words: `GrossTripAmount` → `Gross trip amount`. */
export function words(value: string | null | undefined): string {
  if (!value) return '';
  if (value === value.toUpperCase()) return value;
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export const optionsOf = (values: readonly string[]): { label: string; value: string }[] => values.map((v) => ({ label: words(v), value: v }));

// ── Formats (mirrors `CustomerFormats.cs`) ──────────────────────────────────────────

/** `03XX-XXXXXXX`, 11 digits with no dash, or `+` and 8-15 digits. */
export function isMobile(value: string): boolean {
  const v = value.trim();
  return /^03\d{2}-\d{7}$/.test(v) || /^03\d{9}$/.test(v) || /^\+\d{8,15}$/.test(v);
}

/** Digits, `+`, `(`, `)`, `-` and spaces, 5 to 20 characters. */
export function isPhone(value: string): boolean {
  return /^[0-9+()\- ]{5,20}$/.test(value.trim());
}

export function isEmail(value: string): boolean {
  const v = value.trim();
  return v.length <= 150 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v);
}

// ── Status ─────────────────────────────────────────────────────────────────────────

export type CustomerStatus = 'Draft' | 'Active' | 'Inactive';

export function statusSeverity(status: string): 'success' | 'secondary' | 'warn' | 'danger' | 'info' {
  switch (status) {
    case 'Active':
      return 'success';
    case 'Inactive':
      return 'danger';
    case 'Draft':
      return 'secondary';
    default:
      return 'info';
  }
}

/** What status moves are ever offered from here, before the caller's own permission is checked. */
export function statusMoves(status: string): CustomerStatus[] {
  switch (status) {
    case 'Draft':
      return ['Active'];
    case 'Active':
      return ['Inactive'];
    case 'Inactive':
      return ['Active'];
    default:
      return [];
  }
}

// ── History wording (mirrors `vehicle-logic.ts`'s own, for the same audit-row shape) ───

const ENTITY_LABELS: Record<string, string> = {
  Customer: 'Customer',
  CustomerContact: 'Contact',
  CustomerBillingAddress: 'Billing address',
  CustomerBillingConfiguration: 'Billing configuration',
  CustomerTaxRule: 'Tax rule',
  CustomerInvoiceTemplate: 'Invoice template',
};
export const entityLabel = (entity: string): string => ENTITY_LABELS[entity] ?? entity;

export interface ChangeLike {
  entity: string;
  action: string;
  field?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  restricted: boolean;
}

/** One line for a history row: "Status: Draft → Active", "Added contact Ali Raza", "Payment terms (days): 30 → 45". */
export function describeChange(c: ChangeLike): string {
  const what = entityLabel(c.entity);
  const show = (v: string | null | undefined): string => (v === null || v === undefined || v === '' ? '(empty)' : v);
  const title = (json: string | null | undefined): string => {
    try {
      const o = JSON.parse(json ?? '') as Record<string, unknown>;
      for (const key of ['CustomerName', 'Name', 'AddressName', 'TaxName', 'TemplateName']) if (o[key] !== undefined && o[key] !== null && o[key] !== '') return String(o[key]);
    } catch {
      /* not JSON */
    }
    return '';
  };

  if (c.action === 'Created' && c.entity !== 'Customer') return `Added ${what.toLowerCase()} ${title(c.newValue)}`.trim();
  if (c.action === 'Deleted') return `Removed ${what.toLowerCase()} ${title(c.oldValue)}`.trim();
  const label = c.entity === 'Customer' ? words(c.field ?? '') : `${what} ${words(c.field ?? '').toLowerCase()}`;
  return c.restricted ? `${label} changed (values hidden)` : `${label}: ${show(c.oldValue)} → ${show(c.newValue)}`;
}
