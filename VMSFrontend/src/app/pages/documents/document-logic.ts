/**
 * The rules of the Documents screens (FSD §23A) that do not depend on Angular: status colours, mandatory-level wording,
 * and the small helpers every Documents screen shares. `words`/`optionsOf` come from the vehicles module (the same generic
 * PascalCase-to-words helper `payables.component.ts` already imports from outside `pages/vehicles`).
 */
export { optionsOf, words } from '../vehicles/vehicle-logic';

/** A version's lifecycle (§23A.3): a Superseded or Rejected version never changes again; the other three are recalculated nightly. */
export const DOCUMENT_STATUSES = ['Active', 'ExpiringSoon', 'Expired', 'Superseded', 'Rejected'] as const;

/** The p-tag severity for a document's status: green while good, amber approaching or waiting on a decision, red once it matters. */
export function statusSeverity(status: string): 'success' | 'warn' | 'danger' | 'secondary' {
  switch (status) {
    case 'Active': return 'success';
    case 'ExpiringSoon': return 'warn';
    case 'Expired': return 'danger';
    case 'Rejected': return 'danger';
    default: return 'secondary'; // Superseded
  }
}

/** A slot with no current version: what its empty state says, and how urgently (§23A.1's Mandatory level, BR-VH-015/BR-BP-005). */
export function mandatorySeverity(level: string): 'danger' | 'warn' | 'secondary' {
  return level === 'Required' ? 'danger' : level === 'Warn' ? 'warn' : 'secondary';
}

export const MANDATORY_LEVELS = ['None', 'Warn', 'Required'] as const;

/** Days remaining as a short phrase: negative reads as overdue, not as "-5 days". */
export function daysRemainingText(days: number | null | undefined): string {
  if (days === null || days === undefined) return '—';
  if (days < 0) return `${Math.abs(days)} day${Math.abs(days) === 1 ? '' : 's'} overdue`;
  if (days === 0) return 'Today';
  return `${days} day${days === 1 ? '' : 's'} left`;
}

/** Mirrors the server's `ValidityUnits.Add` (BR-DOC-005): a quick preview of the renewed expiry, before the server computes the same date. */
export function addValidity(from: string, value: number, unit: string | null | undefined): string {
  const [y, m, d] = from.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  if (unit === 'Days') date.setUTCDate(date.getUTCDate() + value);
  else if (unit === 'Years') date.setUTCFullYear(date.getUTCFullYear() + value);
  else date.setUTCMonth(date.getUTCMonth() + value);
  return date.toISOString().slice(0, 10);
}

/** The route to an owner's own screen, for the Register and Missing Documents report to link back to it. */
export function ownerLink(ownerType: string, ownerId: number): string[] {
  return ownerType === 'Vehicle' ? ['/vehicles', String(ownerId)] : ['/partners', String(ownerId)];
}
