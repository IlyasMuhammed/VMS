/** The vocabulary of the Customer Ledger screens (FSD §40A) that does not depend on Angular. No framework
 * imports: tested under Node (scripts/check-ledger.test.mjs). */

/** §40A.1's own L1-L16 entry types. */
const ENTRY_TYPE_LABELS: Record<string, string> = {
  INVOICE: 'Invoice',
  ADJUSTMENT: 'Adjustment',
  DEDUCTION: 'Deduction',
  PAYMENT: 'Payment',
  PAYMENT_REVERSAL: 'Payment reversal',
  WRITE_OFF: 'Write-off',
  DISCOUNT: 'Discount',
  SETTLEMENT_REVERSAL: 'Settlement reversal',
  ADVANCE: 'Advance',
  ADVANCE_APPLY_OUT: 'Advance applied (trip)',
  ADVANCE_APPLY_IN: 'Advance applied (invoice)',
  ADVANCE_REVERSAL: 'Advance reversal',
  INVOICE_CANCEL: 'Invoice cancelled',
  INVOICE_SUPERSEDED: 'Invoice superseded',
  TRANSFER_OUT: 'Transfer out',
  TRANSFER_IN: 'Transfer in',
  CARRY_FORWARD_OUT: 'Carry forward out',
  CARRY_FORWARD_IN: 'Carry forward in',
  REFUND: 'Refund',
  OPENING_BALANCE: 'Opening balance',
};

/** An unrecognised code still shows something, not a blank. */
export function entryTypeLabel(type: string): string {
  return ENTRY_TYPE_LABELS[type] ?? type;
}

/** §40A: "Debit increases what the customer owes; Credit reduces it... Positive = receivable (Dr); negative =
 * customer credit (Cr)" — every balance on these screens is read with this same Dr/Cr suffix, never a bare
 * signed number. */
export function balanceLabel(amount: number): string {
  return `${Math.abs(amount)} ${amount < 0 ? 'Cr' : 'Dr'}`;
}

/** Matches this codebase's own global `.chip--*` variants (`styles.scss`) directly, so callers can write
 * `class="chip chip--{{ severity }}"` without a second translation step. */
export function balanceSeverity(amount: number): 'danger' | 'success' | 'neutral' {
  if (amount > 0) return 'danger'; // still owed
  if (amount < 0) return 'success'; // a customer credit
  return 'neutral';
}
