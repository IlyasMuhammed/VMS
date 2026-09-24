/**
 * The rules of the Business Partner form (FSD §6 to §8, §12.1) that do not depend on Angular: the formats, what is
 * mandatory for a party type and set of roles, how the server's field paths map onto the form, and what a duplicate
 * check means for saving. The API checks the same things again; these exist so the screen can say so while the person
 * types. No framework imports: tested under Node (scripts/check-partners.test.mjs).
 */

// ── Vocabulary ─────────────────────────────────────────────────────────────────────

export const PARTY_TYPES = ['Person', 'Company'] as const;
export type PartyType = (typeof PARTY_TYPES)[number];

export interface RoleDef {
  code: string;
  label: string;
  /** True when the role has a panel of its own fields that the API stores (Driver, Vendor, Customer). */
  panel: boolean;
}

/** The nine roles (FSD §4). Only three have fields the API stores: the rest wait on OQ-03. */
export const ROLES: readonly RoleDef[] = [
  { code: 'Driver', label: 'Driver', panel: true },
  { code: 'Workshop', label: 'Workshop', panel: false },
  { code: 'Bank', label: 'Bank', panel: false },
  { code: 'Vendor', label: 'Vendor', panel: true },
  { code: 'Customer', label: 'Customer', panel: true },
  { code: 'RunningCustomer', label: 'Running customer', panel: false },
  { code: 'TrackerCompany', label: 'Tracker company', panel: false },
  { code: 'BodyMaker', label: 'Body maker', panel: false },
  { code: 'FuelCardCompany', label: 'Fuel card company', panel: false },
];

export const roleLabel = (code: string): string => ROLES.find((r) => r.code === code)?.label ?? code;

export const FILER_STATUSES = ['Unknown', 'Filer', 'NonFiler'] as const;
export const ADDRESS_TYPES = ['Registered', 'Billing', 'Workshop', 'Yard', 'Correspondence'] as const;
export const LICENCE_TYPES = ['LTV', 'HTV', 'Motorcycle', 'Other'] as const;
export const EMPLOYMENT_TYPES = ['Employee', 'Contractor', 'AdHoc'] as const;
export const COMMISSION_BASES = ['None', 'Percent', 'Fixed'] as const;
export const SUPPLY_CATEGORIES = ['Parts', 'Tyres', 'Lubricants', 'Fuel', 'Toll', 'Services', 'Other'] as const;
export const CUSTOMER_TYPES = ['Adda', 'CargoCompany', 'Factory', 'Trader', 'Other'] as const;
export const BILLING_CYCLES = ['PerTrip', 'Weekly', 'Fortnightly', 'Monthly'] as const;
export const RATE_BASES = ['PerTrip', 'PerTonne', 'PerKm', 'MonthlyFixed'] as const;

/** A stored value read as words: `CargoCompany` → `Cargo company`, `PerTonne` → `Per tonne`. */
export function words(value: string | null | undefined): string {
  if (!value) return '';
  if (value === value.toUpperCase()) return value; // LTV, HTV: an abbreviation stays as it is
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export const optionsOf = (values: readonly string[]): { label: string; value: string }[] => values.map((v) => ({ label: words(v) || v, value: v }));

// ── Formats (mirror PartnerFormats on the server) ──────────────────────────────────

export const isCnic = (v: string | null | undefined): boolean => !!v && /^\d{5}-\d{7}-\d$/.test(v);
export const isNtn = (v: string | null | undefined): boolean => !!v && (/^(\d{7}(-\d)?|\d{13})$/.test(v) || isCnic(v));

/** A CNIC as it is typed: digits only, the dashes put in for the person (`3520212345671` becomes `35202-1234567-1`). */
export function maskCnic(value: string | null | undefined): string {
  const digits = (value ?? '').replace(/\D/g, '').slice(0, 13);
  if (digits.length <= 5) return digits;
  if (digits.length <= 12) return `${digits.slice(0, 5)}-${digits.slice(5)}`;
  return `${digits.slice(0, 5)}-${digits.slice(5, 12)}-${digits.slice(12)}`;
}

/** The mobile in its stored form (`03XX-XXXXXXX`, or `+…` international), or null if it is not a mobile. */
export function normalizeMobile(v: string | null | undefined): string | null {
  if (!v || !v.trim()) return null;
  const compact = v.replace(/ /g, '');
  if (/^03\d{2}-\d{7}$/.test(compact) || /^\+\d{8,15}$/.test(compact)) return compact;
  return /^03\d{9}$/.test(compact) ? `${compact.slice(0, 4)}-${compact.slice(4)}` : null;
}

export const isPhone = (v: string | null | undefined): boolean => !!v && /^[0-9+()\- ]{5,20}$/.test(v);
export const isAccountNumber = (v: string | null | undefined): boolean => !!v && /^[0-9-]{1,34}$/.test(v);

export function isEmail(v: string | null | undefined): boolean {
  if (!v || v.length > 100) return false;
  // What the server accepts: one address, with a dot in the host part, and no display name.
  return /^[^\s@<>()]+@[^\s@<>()]+\.[^\s@<>()]+$/.test(v) && !/\.\./.test(v);
}

/** `PK` and 22 letters or digits, with the ISO 7064 mod-97 checksum. Spaces and case are ignored. */
export function isIban(value: string | null | undefined): boolean {
  if (!value) return false;
  const v = value.replace(/ /g, '').toUpperCase();
  if (!/^PK\d{2}[A-Z0-9]{20}$/.test(v)) return false;
  const rearranged = v.slice(4) + v.slice(0, 4);
  let remainder = 0;
  for (const c of rearranged) {
    const digits = /[A-Z]/.test(c) ? String(c.charCodeAt(0) - 55) : c;
    for (const d of digits) remainder = (remainder * 10 + Number(d)) % 97;
  }
  return remainder === 1;
}

// ── What is mandatory ──────────────────────────────────────────────────────────────

export interface Requirements {
  cnic: boolean;
  ntn: boolean;
  /** Customer and Bank need an email so statements and notices can be sent. */
  email: boolean;
}

/** Live mandatory indicators (FSD §6, FR-BP-009): a Person needs a CNIC, a Company an NTN; a Customer or Bank needs an email. */
export function requirements(partyType: string | null | undefined, roles: readonly string[]): Requirements {
  return {
    cnic: partyType === 'Person',
    ntn: partyType === 'Company',
    email: roles.includes('Customer') || roles.includes('Bank'),
  };
}

/** Roles whose panel is shown (and must be filled in) for these roles. */
export const panelsFor = (roles: readonly string[]): string[] => ROLES.filter((r) => r.panel && roles.includes(r.code)).map((r) => r.code);

/** Roles with no panel the API stores: the Role details tab says so instead of showing nothing. */
export const panellessRoles = (roles: readonly string[]): string[] => ROLES.filter((r) => !r.panel && roles.includes(r.code)).map((r) => r.code);

/** The default display name: the legal name cut to the 60 characters allowed. */
export const defaultDisplayName = (legalName: string): string => legalName.trim().slice(0, 60);

// ── Server errors onto the form ────────────────────────────────────────────────────

/** `contacts[1].mobile` (the API's path) becomes `contacts.1.mobile` (Angular's). */
export const formPath = (apiField: string): string => apiField.replace(/\[(\d+)\]/g, '.$1');

/** Which tab a form path lives on, so an error can badge the tab and open it. */
export type PartnerTab = 'general' | 'roles' | 'contacts' | 'addresses' | 'bank' | 'vehicles' | 'documents' | 'history';

export function tabOfField(path: string): PartnerTab {
  const head = path.split(/[.[]/)[0];
  switch (head) {
    case 'driver':
    case 'vendor':
    case 'customer':
    case 'roleDetails':
      return 'roles';
    case 'contacts':
      return 'contacts';
    case 'addresses':
      return 'addresses';
    case 'bankAccounts':
      return 'bank';
    default:
      return 'general';
  }
}

// ── Duplicates (FSD §12.1) ─────────────────────────────────────────────────────────

export interface DuplicateMatch {
  id: number;
  bpCode: string;
  legalName: string;
  roles: string[];
  cityId: number;
  city?: string | null;
  status: string;
  matchType: 'Cnic' | 'Ntn' | 'NameAndCity' | 'Mobile' | 'NameSimilar';
  isHard: boolean;
  score?: number | null;
  sameCity: boolean;
}

/** Why a candidate was listed, in words. */
export function matchReason(m: Pick<DuplicateMatch, 'matchType' | 'score'>): string {
  switch (m.matchType) {
    case 'Cnic':
      return 'Same CNIC';
    case 'Ntn':
      return 'Same NTN';
    case 'Mobile':
      return 'Same mobile number';
    case 'NameAndCity':
      return 'Same name in the same city';
    default:
      return `Similar name${m.score ? ` (${Math.round(m.score * 100)}%)` : ''}`;
  }
}

/** A CNIC or NTN already on file: the partner cannot be saved (BR-BP-013). */
export const blockingMatches = (matches: readonly DuplicateMatch[]): DuplicateMatch[] => matches.filter((m) => m.isHard);

/**
 * The candidates the person must answer before saving: a shared mobile, the same name in the same city, or a similar name
 * in the same city. A similar name in another city is shown for information but does not stop the save. Mirrors the server.
 */
export const matchesToAcknowledge = (matches: readonly DuplicateMatch[]): DuplicateMatch[] =>
  matches.filter((m) => !m.isHard && (m.matchType !== 'NameSimilar' || m.sameCity));

/** True when the person may save: nothing blocks, and every candidate that needs an answer has one. */
export function canSaveDespite(matches: readonly DuplicateMatch[], acknowledged: boolean): boolean {
  if (blockingMatches(matches).length > 0) return false;
  return matchesToAcknowledge(matches).length === 0 || acknowledged;
}

// ── History ────────────────────────────────────────────────────────────────────────

const ENTITY_LABELS: Record<string, string> = {
  BusinessPartner: 'Partner',
  BusinessPartnerRole: 'Role',
  BpDriverDetail: 'Driver details',
  BpVendorDetail: 'Vendor details',
  BpCustomerDetail: 'Customer details',
  BpContact: 'Contact',
  BpAddress: 'Address',
  BpBankAccount: 'Bank account',
};

export const entityLabel = (entity: string): string => ENTITY_LABELS[entity] ?? entity;

/** A field name as a label: `PrimaryMobile` → `Primary mobile`, `Iban` → `IBAN`. */
export function fieldLabel(field: string | null | undefined): string {
  if (!field) return '';
  if (field === 'Iban') return 'IBAN';
  if (field === 'Cnic') return 'CNIC';
  if (field === 'Ntn') return 'NTN';
  if (field === 'Strn') return 'STRN';
  if (field === 'BpCode') return 'BP code';
  return words(field);
}

export interface ChangeLike {
  entity: string;
  action: string;
  field?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  reason?: string | null;
  restricted: boolean;
}

/** One line for a history row: "Notes: 'a' → 'b'", "Added contact Ali Khan", "Saved beside possible duplicate BP-26-00003 …". */
export function describeChange(c: ChangeLike): string {
  const what = entityLabel(c.entity);
  const show = (v: string | null | undefined): string => (v === null || v === undefined || v === '' ? '(empty)' : v);

  if (c.action === 'DuplicateOverridden') return `Saved beside possible duplicate ${c.newValue ?? ''}`.trim();
  if (c.action === 'Created') {
    if (c.field) return c.restricted ? `Set ${fieldLabel(c.field).toLowerCase()} (value hidden)` : `Set ${fieldLabel(c.field).toLowerCase()} to ${show(c.newValue)}`;
    return `Added ${what.toLowerCase()} ${snapshotTitle(c.newValue)}`.trim();
  }
  if (c.action === 'Deleted') return `Removed ${what.toLowerCase()} ${snapshotTitle(c.oldValue)}`.trim();
  const label = c.entity === 'BusinessPartner' ? fieldLabel(c.field) : `${what} ${fieldLabel(c.field).toLowerCase()}`;
  return c.restricted ? `${label} changed (values hidden)` : `${label}: ${show(c.oldValue)} → ${show(c.newValue)}`;
}

/** The words that name a record in a created or deleted row's JSON snapshot (a contact's name, an account's title). */
export function snapshotTitle(json: string | null | undefined): string {
  if (!json) return '';
  try {
    const snapshot = JSON.parse(json) as Record<string, unknown>;
    for (const key of ['LegalName', 'ContactName', 'AccountTitle', 'Line1', 'RoleCode', 'LicenceNo', 'SupplyCategories', 'CustomerType']) {
      const value = snapshot[key];
      if (typeof value === 'string' && value) return key === 'RoleCode' ? roleLabel(value) : value;
    }
  } catch {
    /* not JSON: nothing to name */
  }
  return '';
}
