/**
 * The vocabulary and rate-coverage rules of the Trip Configuration and Trip Rates screens (FSD §18, §19, §26,
 * §48.3 screens 10-11) that do not depend on Angular. `findGaps` is this screen's stand-in for the FSD's own
 * "calendar strip showing covered/uncovered days": rather than drawing 365 day-cells, it reduces the same
 * information to the handful of gaps between effective-dated rates, which is what the strip is actually for
 * (§26's own wording: "gap warning 'No rate for 12-Jul to 14-Jul.'"). No framework imports: tested under Node
 * (scripts/check-trip-configurations.test.mjs).
 */

export const TRIP_DIRECTION_TYPES = ['OneWay', 'Return', 'RoundTrip'] as const;
export const ROUTE_STOP_TYPES = ['Origin', 'Pickup', 'Via', 'Delivery', 'Destination'] as const;

/** A stored value read as words: `RoundTrip` → `Round trip`. */
export function words(value: string | null | undefined): string {
  if (!value) return '';
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export const optionsOf = (values: readonly string[]): { label: string; value: string }[] => values.map((v) => ({ label: words(v), value: v }));

export function configStatusSeverity(status: string): 'success' | 'secondary' | 'danger' {
  switch (status) {
    case 'Active': return 'success';
    case 'Draft': return 'secondary';
    default: return 'danger';
  }
}

export function statusSeverity(status: string): 'success' | 'danger' {
  return status === 'Active' ? 'success' : 'danger';
}

// ── Rate coverage (§26) ──────────────────────────────────────────────────────────────

export interface RateRange {
  effectiveFrom: string;
  effectiveTo?: string | null;
  status: string;
}

export interface Gap {
  from: string;
  to: string;
}

function addDays(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/**
 * The gaps between this configuration's Active rates, sorted earliest first. An open-ended rate (no
 * `effectiveTo`) has nothing after it to check — the server's own overlap rule means nothing else can start
 * once one is open-ended, so there is at most one such row and it is always last.
 */
export function findGaps(rates: readonly RateRange[]): Gap[] {
  const active = rates.filter((r) => r.status === 'Active').slice().sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom));
  const gaps: Gap[] = [];
  for (let i = 0; i < active.length - 1; i++) {
    const endOfThis = active[i].effectiveTo;
    if (endOfThis === null || endOfThis === undefined) continue;
    const nextStart = active[i + 1].effectiveFrom;
    const gapStart = addDays(endOfThis, 1);
    if (gapStart < nextStart) gaps.push({ from: gapStart, to: addDays(nextStart, -1) });
  }
  return gaps;
}

/** Whether any Active rate is open-ended (covers every date from its start onward, with nothing missing after). */
export function hasOpenEndedCoverage(rates: readonly RateRange[]): boolean {
  return rates.some((r) => r.status === 'Active' && (r.effectiveTo === null || r.effectiveTo === undefined));
}
