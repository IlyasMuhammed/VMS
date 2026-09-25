/**
 * The vocabulary and lifecycle rules of the Trip screens (FSD §21-§31, §48.4) that do not depend on Angular —
 * `NORMAL_EDGES`/`canHold`/`canCancel`/`cancelNeedsElevatedPermission` mirror the server's own pure
 * `TripLifecycle` class exactly, so the screen can offer only the buttons a transition will actually accept
 * (the server still re-checks everything — this is what decides which buttons are worth drawing, not the
 * authority on what is allowed). No framework imports: tested under Node (scripts/check-trips.test.mjs).
 */

export const TRIP_STATUSES = ['Draft', 'Planned', 'Assigned', 'Started', 'InTransit', 'AtPickup', 'Loaded', 'AtDelivery', 'Delivered', 'Completed', 'OnHold', 'Cancelled'] as const;
export const FUEL_TYPES = ['Diesel', 'Petrol', 'CNG', 'Other'] as const;
export const FUEL_PAYMENT_METHODS = ['Cash', 'FuelCard', 'Other'] as const;
export const EXPENSE_PAYMENT_METHODS = ['Cash', 'Card', 'Bank', 'PaidByDriver', 'Other'] as const;
export const ISSUE_TYPES = ['Breakdown', 'Accident', 'Delay', 'CustomerHold', 'RouteBlocked', 'Other'] as const;
export const ISSUE_SEVERITIES = ['Low', 'Medium', 'High'] as const;
export const TRIP_DOCUMENT_TYPES = ['LoadingSlip', 'GatePass', 'Challan', 'Photo', 'Other'] as const;
export const OTHER_LOCATION_TYPES = ['Warehouse', 'Factory', 'CustomerSite', 'Depot', 'Terminal', 'ConstructionSite', 'Other'] as const;

/** A stored value read as words: `InTransit` → `In transit`. Abbreviations (all-caps already) are left as-is. */
export function words(value: string | null | undefined): string {
  if (!value) return '';
  if (value === value.toUpperCase()) return value;
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export const optionsOf = (values: readonly string[]): { label: string; value: string }[] => values.map((v) => ({ label: words(v), value: v }));

export function statusSeverity(status: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  switch (status) {
    case 'Completed': return 'success';
    case 'Cancelled': return 'danger';
    case 'OnHold': return 'warn';
    case 'Draft': return 'secondary';
    default: return 'info';
  }
}

// ── Lifecycle (§24) — mirrors `TripLifecycle.cs` ────────────────────────────────────

const NORMAL_EDGES: Record<string, string[]> = {
  Draft: ['Planned'],
  Planned: ['Assigned'],
  Assigned: ['Started'],
  Started: ['InTransit', 'AtPickup'],
  InTransit: ['AtPickup', 'AtDelivery'],
  AtPickup: ['Loaded'],
  Loaded: ['InTransit', 'AtDelivery'],
  AtDelivery: ['Delivered'],
  Delivered: ['Completed'],
};

const CAN_HOLD_FROM = new Set(['Planned', 'Assigned', 'Started', 'InTransit', 'AtPickup', 'Loaded', 'AtDelivery', 'Delivered']);
const CAN_CANCEL_FROM = new Set(['Draft', 'Planned', 'Assigned', 'Started', 'InTransit', 'AtPickup', 'Loaded', 'AtDelivery', 'Delivered', 'OnHold']);
const CANCEL_ELEVATED_UNLESS = new Set(['Draft', 'Planned', 'Assigned']);

/** The normal next statuses from here — never a skip, never Cancel/Hold (each has its own button). */
export function nextMoves(status: string): string[] {
  return NORMAL_EDGES[status] ?? [];
}

export function canHold(status: string): boolean {
  return CAN_HOLD_FROM.has(status);
}

export function canCancel(status: string): boolean {
  return CAN_CANCEL_FROM.has(status);
}

/** §24: "Cancel after Started requires Fleet/Admin" — Assigned and earlier is an ordinary back-office/driver cancel. */
export function cancelNeedsElevatedPermission(status: string): boolean {
  return !CANCEL_ELEVATED_UNLESS.has(status);
}

/** §21: "Start odometer captured" / §24: "End odometer" — the two transitions that need an extra field before
 * the request can be sent, so the dialog knows to ask for it. */
export function needsStartOdometer(toStatus: string): boolean {
  return toStatus === 'Started';
}

export function needsEndOdometer(toStatus: string): boolean {
  return toStatus === 'Delivered';
}

// ── Fuel efficiency / amount checks (§27) ───────────────────────────────────────────

/** §27: a caller-supplied amount differing from Quantity × Rate by more than 1 (base currency unit) is a
 * non-blocking warning, never a rejection — the same rounding-tolerant comparison the server itself uses. */
export function fuelAmountMismatch(quantity: number, rate: number, amount: number): boolean {
  return Math.abs(quantity * rate - amount) > 1;
}

// ── POD / Expense chips ──────────────────────────────────────────────────────────────

export function podSeverity(status: string | null | undefined): 'success' | 'warn' | 'danger' | 'secondary' {
  switch (status) {
    case 'Approved': return 'success';
    case 'Rejected': return 'danger';
    case 'Uploaded': return 'warn';
    default: return 'secondary';
  }
}

export function expenseSeverity(status: string): 'success' | 'warn' | 'danger' {
  switch (status) {
    case 'Approved': return 'success';
    case 'Rejected': return 'danger';
    default: return 'warn';
  }
}
