/** The vocabulary of the Advances screen (FSD §37.4) that does not depend on Angular. No framework imports:
 * tested under Node (scripts/check-advances.test.mjs). */

export function statusSeverity(status: string): 'success' | 'info' | 'warn' | 'danger' {
  switch (status) {
    case 'Open':
      return 'success';
    case 'Applied':
      return 'info';
    case 'Refunded':
      return 'warn';
    default:
      return 'danger'; // Reversed
  }
}

/** §37.4: Move/Refund/Reverse are all only available while the advance is still sitting unapplied. */
export function canAct(status: string): boolean {
  return status === 'Open';
}

/** How much of the advance is still sitting against its trip, unapplied and unrefunded. */
export function remaining(advance: { amount: number; appliedAmount: number; refundedAmount: number }): number {
  return advance.amount - advance.appliedAmount - advance.refundedAmount;
}
