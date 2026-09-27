/** The vocabulary of the Invoice screens (FSD §32-§36, §48.5) that does not depend on Angular. No framework
 * imports: tested under Node (scripts/check-invoices.test.mjs). */

export function statusSeverity(status: string): 'success' | 'info' | 'secondary' | 'danger' {
  switch (status) {
    case 'Submitted': return 'success';
    case 'Generated': return 'info';
    case 'Draft': return 'secondary';
    default: return 'danger'; // Inactive, Cancelled
  }
}

export function paymentStatusSeverity(status: string): 'success' | 'warn' | 'danger' {
  switch (status) {
    case 'Paid': return 'success';
    case 'PartiallyPaid': return 'warn';
    default: return 'danger'; // Unpaid
  }
}

/** A stored value read as words: `PartiallyPaid` → `Partially paid`. */
export function words(value: string | null | undefined): string {
  if (!value) return '';
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

const BLOCKING_LABELS: Record<string, string> = {
  RATE_MISSING: 'Rate not configured for this date',
  POD_MISSING: 'Proof of delivery missing',
  INACTIVE: 'Trip is inactive',
  NOT_COMPLETED: 'Trip is not yet Completed',
  CURRENCY_MISMATCH: "Trip's currency does not match the customer's",
};

/** §32.1's own blocking reasons, worded for the screen. An unrecognised code still shows something, not a blank. */
export function blockingReason(code: string): string {
  return BLOCKING_LABELS[code] ?? code;
}

/** §41's own `InvoiceEvidenceStatuses`: Queued, Generated, Failed, Superseded. */
export function evidenceStatusSeverity(status: string): 'success' | 'info' | 'danger' | 'secondary' {
  switch (status) {
    case 'Generated': return 'success';
    case 'Queued': return 'info';
    case 'Failed': return 'danger';
    default: return 'secondary'; // Superseded
  }
}

/** A byte count read as a size: 1536 → "1.5 KB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(1)} ${units[unit]}`;
}
