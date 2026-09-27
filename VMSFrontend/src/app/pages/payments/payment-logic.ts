/** The vocabulary of the Payments screens (FSD §37, §37.5, §40) that does not depend on Angular. No framework
 * imports: tested under Node (scripts/check-payments.test.mjs). */

export const PAYMENT_METHODS = ['DirectToAccount', 'BankCheque'] as const;
export const SETTLEMENT_TYPES = ['WriteOff', 'Discount'] as const;

/** A stored value read as words: `DirectToAccount` → `Direct to account`. */
export function words(value: string | null | undefined): string {
  if (!value) return '';
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export const optionsOf = (values: readonly string[]): { label: string; value: string }[] => values.map((v) => ({ label: words(v), value: v }));

export function statusSeverity(status: string): 'success' | 'danger' {
  return status === 'Posted' ? 'success' : 'danger'; // Reversed
}

/** §37 BR-P2: a payment exceeding the invoice's own balance needs "confirm overpayment" first. */
export function isOverpayment(amount: number, invoiceBalance: number): boolean {
  return amount > invoiceBalance;
}

/** §37.5: "pre-filled with the remaining amount" once the payment above leaves something owing. */
export function remainingAfterPayment(invoiceBalance: number, amount: number): number {
  return Math.max(0, invoiceBalance - amount);
}

/** §40: only a negative balance (a credit) can be carried forward or refunded. */
export function isCredit(balanceAmount: number): boolean {
  return balanceAmount < 0;
}
