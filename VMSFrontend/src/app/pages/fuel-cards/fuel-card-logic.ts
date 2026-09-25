/** The vocabulary and expiry rules of the Fuel Card screen (FSD §28, §48.3 screen 17) that do not depend on
 * Angular. No framework imports: tested under Node (scripts/check-fuel-cards.test.mjs). */

export function statusSeverity(status: string): 'success' | 'secondary' | 'danger' | 'warn' {
  switch (status) {
    case 'Active': return 'success';
    case 'Inactive': return 'secondary';
    case 'Expired': return 'warn';
    case 'Blocked': return 'danger';
    default: return 'secondary';
  }
}

/** §28: an expired card cannot be set Active by hand — the nightly job is the only thing that sets Expired, and
 * only the nightly job clears it (by nobody: once past its date, a card stays Expired until its date is moved
 * forward and it is saved again). This just answers whether `today` is past `expiryDate`, for the "expired chip"
 * the grid shows ahead of the server's own status catching up. */
export function isPastExpiry(expiryDate: string, today: string): boolean {
  return expiryDate < today;
}

/** Within `days` of expiring (inclusive), and not already past it — the amber warning the grid shows before a
 * card goes fully Expired. */
export function isExpiringSoon(expiryDate: string, today: string, days = 30): boolean {
  if (isPastExpiry(expiryDate, today)) return false;
  const expiry = new Date(`${expiryDate}T00:00:00Z`);
  const from = new Date(`${today}T00:00:00Z`);
  const diffDays = Math.round((expiry.getTime() - from.getTime()) / 86_400_000);
  return diffDays <= days;
}
