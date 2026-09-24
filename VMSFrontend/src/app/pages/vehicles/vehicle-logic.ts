/**
 * The rules of the vehicle screens (FSD §16 to §23) that do not depend on Angular: the vocabulary, which fields a category
 * has, which moves a status allows, and the wording of history rows. The API checks all of it again; these exist so the
 * screen can offer the right choices and say so while the person types. No framework imports: tested under Node
 * (scripts/check-vehicles.test.mjs), which also checks the lists below against the API's own.
 */

/** A stored value read as words: `UnderMaintenance` → `Under maintenance`, `PerTrip` → `Per trip`. Abbreviations stay. */
export function words(value: string | null | undefined): string {
  if (!value) return '';
  if (value === value.toUpperCase()) return value;
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export const optionsOf = (values: readonly string[]): { label: string; value: string }[] => values.map((v) => ({ label: words(v), value: v }));

// ── Vocabulary ─────────────────────────────────────────────────────────────────────

export const FUEL_TYPES = ['Diesel', 'Petrol', 'CNG', 'LPG', 'Hybrid', 'Electric'] as const;
export const CAPACITY_UNITS = ['Tonne', 'Kg', 'Litre', 'CFT', 'Passengers'] as const;
export const ACQUISITION_TYPES = ['Purchase', 'Lease', 'Rent', 'SharedInduction', 'CustomerInduction'] as const;
export const PAYMENT_MODES = ['Cash', 'BankTransfer', 'Cheque', 'PayOrder'] as const;
export const FINANCE_FREQUENCIES = ['Monthly', 'Quarterly', 'HalfYearly'] as const;

export const VEHICLE_STATUSES = ['Draft', 'Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable', 'Retired', 'Sold', 'Transferred'] as const;
export const IN_FLEET = ['Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable'] as const;
export const SETTABLE_STATUSES = ['Active', 'UnderMaintenance', 'TemporarilyUnavailable'] as const;

export const CATEGORIES = ['SelfOwned', 'Shared', 'Rented', 'CustomerArrangement', 'BankLeased'] as const;
export type Category = (typeof CATEGORIES)[number];

export const SHARING_BASES = ['ProfitShare', 'RevenueShare', 'FixedMonthly'] as const;
export const EXPENSE_RULES = ['AllExpensesSameRatio', 'OnlyMajorExpenses', 'EachBearsOwn'] as const;
export const RENT_FREQUENCIES = ['Monthly', 'Weekly', 'PerTrip'] as const;
export const ARRANGEMENT_TYPES = ['DedicatedMonthly', 'PerTrip', 'RevenueShare'] as const;
export const ITEM_CONDITIONS = ['New', 'Used', 'Refurbished'] as const;
export const DISPOSAL_KINDS = ['Retire', 'Sell', 'Transfer'] as const;

// ── Recurring charges (FSD §19A.1) ──────────────────────────────────────────────────

export const CHARGE_AMOUNT_BASES = ['Fixed', 'Variable', 'PercentageOfIncome'] as const;
export const CHARGE_FREQUENCIES = ['Monthly', 'Quarterly', 'HalfYearly', 'Yearly', 'Weekly', 'CustomDays'] as const;
export const CHARGE_POSTING_MODES = ['GenerateAsDue', 'AutoPost', 'ReminderOnly'] as const;
export const CHARGE_ENTRY_STATUSES = ['Due', 'Overdue', 'Paid', 'Waived', 'Cancelled'] as const;
/** Frequencies that use a day-of-month (Yearly also uses a month); Weekly and Custom Days do not. */
export const CHARGE_FREQUENCIES_WITH_DUE_DAY = ['Monthly', 'Quarterly', 'HalfYearly', 'Yearly'] as const;

/** Vehicle types that need a load capacity (FSD §16.2 field 13): the codes the platform's Vehicle Type list is seeded with. */
export const CAPACITY_TYPE_CODES = ['TRUCK', 'TRAILER', 'TANKER', 'PRIME_MOVER'] as const;
export const needsCapacity = (typeCode: string | null | undefined): boolean => !!typeCode && (CAPACITY_TYPE_CODES as readonly string[]).includes(typeCode);

/** `LES-1234` and `les 1234` are one number (BR-VH-017). */
export const registrationKey = (value: string | null | undefined): string => (value ?? '').toUpperCase().replace(/[\s-]/g, '');

// ── Status ─────────────────────────────────────────────────────────────────────────

export const isDraft = (status: string): boolean => status === 'Draft';
export const isInFleet = (status: string): boolean => (IN_FLEET as readonly string[]).includes(status);
export const isDisposed = (status: string): boolean => status === 'Sold' || status === 'Transferred';

/** Where a person can move a vehicle from here (FSD §23.1). A Draft is activated, not moved; Sold and Transferred are final. */
export function statusMoves(status: string): string[] {
  if (isInFleet(status)) return SETTABLE_STATUSES.filter((s) => s !== status);
  return status === 'Retired' ? ['Active'] : [];
}

/** A vehicle in the fleet, or a retired one, can be retired, sold or transferred. */
export const canDispose = (status: string): boolean => isInFleet(status) || status === 'Retired';

/** The moves of disposal open to this status: a retired vehicle cannot be retired again. */
export const disposalKindsFor = (status: string): string[] => (status === 'Retired' ? ['Sell', 'Transfer'] : canDispose(status) ? [...DISPOSAL_KINDS] : []);

/** An in-fleet vehicle can be given attached items, a driver and odometer readings; a Draft or a disposed one cannot. */
export const takesFleetActions = isInFleet;

// ── Categories (FSD §17) ───────────────────────────────────────────────────────────

export interface CategoryRule {
  /** What the other side is called on the screen. */
  counterpartyLabel: string;
  /** Roles the counterparty may hold; empty means any. */
  roles: readonly string[];
  /** The fields of the category, in the order they are shown. */
  fields: readonly string[];
}

export const CATEGORY_RULES: Record<Category, CategoryRule> = {
  SelfOwned: { counterpartyLabel: '', roles: [], fields: [] },
  Shared: { counterpartyLabel: 'Sharing partner', roles: [], fields: ['sharePercent', 'sharingBasis', 'fixedMonthlyAmount', 'expenseSharingRule', 'agreementReference', 'endDate'] },
  Rented: { counterpartyLabel: 'Lessor', roles: [], fields: ['rentAmount', 'rentFrequency', 'rentDueDay', 'securityDeposit', 'agreementReference', 'endDate'] },
  CustomerArrangement: { counterpartyLabel: 'Customer', roles: ['Customer', 'RunningCustomer'], fields: ['arrangementType', 'agreedAmount', 'revenueSharePercent', 'agreementReference', 'endDate'] },
  BankLeased: { counterpartyLabel: 'Bank', roles: ['Bank'], fields: ['agreementReference'] },
};

/** Which conditional fields show for the choices made so far (FSD §17.1 to §17.3). */
export function visibleCategoryFields(category: string, values: { sharingBasis?: string | null; rentFrequency?: string | null; arrangementType?: string | null }): string[] {
  const rule = CATEGORY_RULES[category as Category];
  if (!rule) return [];
  return rule.fields.filter((f) => {
    if (f === 'fixedMonthlyAmount') return values.sharingBasis === 'FixedMonthly';
    if (f === 'rentDueDay') return values.rentFrequency === 'Monthly';
    if (f === 'agreedAmount') return values.arrangementType === 'DedicatedMonthly';
    if (f === 'revenueSharePercent') return values.arrangementType === 'RevenueShare';
    return true;
  });
}

// ── Acquisition and finance (FSD §18) ──────────────────────────────────────────────

/** The Finance Type list value that means no agreement at all: the vehicle was paid for in full. */
export const FULLY_PAID_CODE = 'FULLY_PAID';
/** The agreement may be dated up to this many days after the acquisition (§18.2). */
export const AGREEMENT_GRACE_DAYS = 90;
export const MAX_TENURE = 120;
/** The API asks this as a question when finance plus down payment is not the purchase price (BR-VH-010): the screen confirms and sends again. */
export const FINANCE_NOT_RECONCILED = 'VAL-VH-013';

const num = (v: number | null | undefined): number => (typeof v === 'number' && Number.isFinite(v) ? v : 0);
/** Money is compared to the paisa, so 0.1 + 0.2 is 0.3. */
const cents = (v: number | null | undefined): number => Math.round(num(v) * 100);

/** Installment times tenure: what the bank is to be paid in installments, before any residual (§18.2). */
export const totalPayable = (installment: number | null | undefined, tenure: number | null | undefined): number => cents(installment) * Math.trunc(num(tenure)) / 100;

/** Finance amount plus down payment against the purchase price (BR-VH-010). Null when they agree, or when there is no price to compare with. */
export function reconciliation(price: number | null | undefined, financeAmount: number | null | undefined, downPayment: number | null | undefined): { sum: number; price: number } | null {
  if (price === null || price === undefined) return null;
  return cents(financeAmount) + cents(downPayment) === cents(price) ? null : { sum: (cents(financeAmount) + cents(downPayment)) / 100, price };
}

/** The down payment is the amount paid at creation, so it is not counted twice (BR-VH-009). An empty amount paid is nothing paid. */
export const downPaymentMatches = (downPayment: number | null | undefined, amountPaid: number | null | undefined): boolean => cents(downPayment) === cents(amountPaid);

/** What the bank is likely financing: the price less what was paid now. Null when there is nothing sensible to suggest. */
export function suggestedFinanceAmount(price: number | null | undefined, amountPaid: number | null | undefined): number | null {
  if (price === null || price === undefined) return null;
  const left = cents(price) - cents(amountPaid);
  return left > 0 ? left / 100 : null;
}

/** A day plus some days, as `YYYY-MM-DD`, worked in UTC so no time zone can move it. */
export function addDays(day: string, days: number): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** The latest date the agreement may carry, from the acquisition date. */
export const agreementDateLimit = (acquisitionDate: string | null | undefined): string | null => (acquisitionDate ? addDays(acquisitionDate, AGREEMENT_GRACE_DAYS) : null);

/** The bank block shows for a lease, or when an agreement is already saved, or when the person asks for it. */
export const showsFinanceBlock = (acquisitionType: string | null | undefined, hasAgreement: boolean, asked: boolean): boolean => hasAgreement || asked || acquisitionType === 'Lease';

/** Step 3 is entered by someone who may enter acquisition and can see both what it costs and what is financed. */
export const mayEnterFinance = (has: (permission: string) => boolean): boolean => has('VEH.ACQUISITION.EDIT') && has('VEH.FIELD.COST.VIEW') && has('VEH.FIELD.FINANCE.VIEW');
// ── The wizard (FSD §21) ───────────────────────────────────────────────────────────

export interface WizardStepDef {
  key: 'details' | 'ownership' | 'acquisition' | 'items' | 'review';
  label: string;
  /** What builds the step, for a step that is not available yet. */
  pending?: string;
}

export const WIZARD_STEPS: readonly WizardStepDef[] = [
  { key: 'details', label: 'Vehicle details' },
  { key: 'ownership', label: 'Ownership' },
  { key: 'acquisition', label: 'Acquisition & finance' },
  { key: 'items', label: 'Items & documents' },
  { key: 'review', label: 'Review & activate' },
];

/** Steps 2 to 5 can be reached only after step 1 validates (FR-VH-009). */
export const stepReachable = (index: number, detailsValid: boolean): boolean => index === 0 || detailsValid;

// ── What a Draft keeps for the steps that are not saved on their own ───────────────

/** A Draft writes only the vehicle row (FR-VH-012), so the ownership answers and the items travel with it as JSON in `draftData` until activation. */
export interface DraftOwnership {
  category: string | null;
  details: Record<string, unknown>;
}

/** An item entered on step 4. The names are only for drawing the grid; the API takes the ids. */
export interface DraftItem {
  itemTypeId: number | null;
  itemTypeName?: string | null;
  description: string;
  serialNo?: string | null;
  supplierId?: number | null;
  supplierName?: string | null;
  installationDate?: string | null;
  cost?: number | null;
  warrantyUntil?: string | null;
  condition?: string | null;
}

export interface DraftData {
  ownership: DraftOwnership | null;
  items: DraftItem[];
}

/** What `draftData` holds. Anything that is not the shape we wrote (an older draft, hand-edited data) is read as nothing. */
export function parseDraftData(json: string | null | undefined): DraftData {
  const none: DraftData = { ownership: null, items: [] };
  if (!json) return none;
  try {
    const parsed = JSON.parse(json) as { ownership?: { category?: unknown; details?: unknown }; items?: unknown };
    const o = parsed?.ownership;
    let ownership: DraftOwnership | null = null;
    if (o && typeof o === 'object') {
      const details = o.details && typeof o.details === 'object' && !Array.isArray(o.details) ? (o.details as Record<string, unknown>) : {};
      ownership = { category: typeof o.category === 'string' ? o.category : null, details };
    }
    const items = Array.isArray(parsed?.items) ? (parsed.items as unknown[]).filter((i): i is DraftItem => !!i && typeof i === 'object' && typeof (i as DraftItem).description === 'string') : [];
    return { ownership, items };
  } catch {
    return none;
  }
}

/** The JSON to save, or null when nothing has been entered (so an empty step leaves no data behind). */
export function serializeDraftData(ownership: DraftOwnership | null, items: readonly DraftItem[] = []): string | null {
  const out: { ownership?: DraftOwnership; items?: DraftItem[] } = {};
  if (ownership) {
    const details = Object.fromEntries(Object.entries(ownership.details).filter(([, v]) => v !== null && v !== undefined && v !== ''));
    if (ownership.category || Object.keys(details).length > 0) out.ownership = { category: ownership.category, details };
  }
  if (items.length > 0) out.items = [...items];
  return out.ownership || out.items ? JSON.stringify(out) : null;
}

/** The items as the API takes them: the names used to draw the grid are left out, and empty text is nothing. */
export function itemRequests(items: readonly DraftItem[]): {
  itemTypeId: number | null; description: string; serialNo: string | null; supplierId: number | null; installationDate: string | null; cost: number | null; warrantyUntil: string | null; condition: string | null;
}[] {
  return items.map((i) => ({
    itemTypeId: i.itemTypeId, description: i.description.trim(), serialNo: i.serialNo?.trim() || null, supplierId: i.supplierId ?? null,
    installationDate: i.installationDate || null, cost: i.cost ?? null, warrantyUntil: i.warrantyUntil || null, condition: i.condition || null,
  }));
}
// ── History wording ────────────────────────────────────────────────────────────────

const ENTITY_LABELS: Record<string, string> = {
  Vehicle: 'Vehicle',
  VehicleRelation: 'Ownership',
  VehicleAttachedItem: 'Attached item',
  OdometerReading: 'Odometer',
  DriverAssignment: 'Driver assignment',
};
export const entityLabel = (entity: string): string => ENTITY_LABELS[entity] ?? entity;

export function fieldLabel(field: string | null | undefined): string {
  if (!field) return '';
  const known: Record<string, string> = { Gvw: 'GVW', RegistrationNo: 'Registration number', VehicleTypeId: 'Vehicle type', MakeId: 'Make', ChassisNo: 'Chassis number', EngineNo: 'Engine number' };
  return known[field] ?? words(field);
}

export interface ChangeLike {
  entity: string;
  action: string;
  field?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  restricted: boolean;
}

/** One line for a history row: "Colour: (empty) → White", "Added attached item 40 ft container", "Status: Active → UnderMaintenance". */
export function describeChange(c: ChangeLike): string {
  const what = entityLabel(c.entity);
  const show = (v: string | null | undefined): string => (v === null || v === undefined || v === '' ? '(empty)' : v);
  const title = (json: string | null | undefined): string => {
    try {
      const o = JSON.parse(json ?? '') as Record<string, unknown>;
      for (const key of ['RegistrationNo', 'Description', 'Category', 'Km', 'DriverId']) if (o[key] !== undefined && o[key] !== null && o[key] !== '') return String(o[key]);
    } catch {
      /* not JSON */
    }
    return '';
  };

  if (c.action === 'FuelCardReassigned') return `Fuel card ${c.oldValue ?? ''} taken by another vehicle`.trim();
  if (c.action === 'Created') return c.field ? (c.restricted ? `Set ${fieldLabel(c.field).toLowerCase()} (value hidden)` : `Set ${fieldLabel(c.field).toLowerCase()} to ${show(c.newValue)}`) : `Added ${what.toLowerCase()} ${title(c.newValue)}`.trim();
  if (c.action === 'Deleted') return `Removed ${what.toLowerCase()} ${title(c.oldValue)}`.trim();
  const label = c.entity === 'Vehicle' ? fieldLabel(c.field) : `${what} ${fieldLabel(c.field).toLowerCase()}`;
  return c.restricted ? `${label} changed (values hidden)` : `${label}: ${show(c.oldValue)} → ${show(c.newValue)}`;
}

export interface LifecycleLike {
  eventType: string;
  fromStatus?: string | null;
  toStatus?: string | null;
  fromCategory?: string | null;
  toCategory?: string | null;
  counterparty?: { name: string } | null;
  amount?: number | null;
}

/** The lifecycle row in words: "Active → Under maintenance", "Self owned → Rented (with Al Noor)", "Sold to Ali Traders". */
export function describeLifecycle(l: LifecycleLike): string {
  switch (l.eventType) {
    case 'Created':
      return 'Vehicle created as a draft';
    case 'StatusChange':
      return `${words(l.fromStatus)} → ${words(l.toStatus)}`;
    case 'CategoryChange':
      return `${words(l.fromCategory)} → ${words(l.toCategory)}${l.counterparty ? ` (with ${l.counterparty.name})` : ''}`;
    case 'Disposal': {
      const to = l.toStatus === 'Sold' ? 'Sold' : l.toStatus === 'Transferred' ? 'Transferred' : 'Retired';
      return `${to}${l.counterparty ? (l.toStatus === 'Sold' ? ' to ' : ' to ') + l.counterparty.name : ''}`;
    }
    default:
      return l.eventType;
  }
}

// ── Server errors ──────────────────────────────────────────────────────────────────

/** The error code the API gives when a fuel card is on another vehicle: the screen asks, then sends again with the yes. */
export const FUEL_CARD_IN_USE = 'VAL-VH-018';
/** …and when a driver is already the default driver of another vehicle. */
export const DRIVER_ALREADY_ASSIGNED = 'VAL-VH-017';
