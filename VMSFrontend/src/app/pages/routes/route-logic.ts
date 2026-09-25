/**
 * The vocabulary and stop rules of the Route screen (FSD §17, §48.3 screen 9) that do not depend on Angular —
 * mirrors the server's own `ValidateStopsAsync` so the screen can say so before the round trip. No drag-and-drop
 * library is pulled in for reordering: `moveStop` is the up/down-button equivalent, one array swap, easy to test
 * and to keep keyboard-accessible. No framework imports: tested under Node (scripts/check-routes.test.mjs).
 */

export const ROUTE_STOP_TYPES = ['Origin', 'Pickup', 'Via', 'Delivery', 'Destination'] as const;
export type RouteStopType = (typeof ROUTE_STOP_TYPES)[number];

export function statusSeverity(status: string): 'success' | 'danger' {
  return status === 'Active' ? 'success' : 'danger';
}

export interface StopLike {
  cityAbbreviation: string;
}

/** §17's own preview string: `LHR → SKP → FSD`. */
export function previewString(stops: readonly StopLike[]): string {
  return stops.map((s) => s.cityAbbreviation).join(' → ');
}

/** Moves the stop at `index` one place up (`-1`) or down (`1`); out-of-range moves are a no-op, not an error, so a
 * button handler never has to guard it first. */
export function moveStop<T>(stops: readonly T[], index: number, by: -1 | 1): T[] {
  const to = index + by;
  if (index < 0 || index >= stops.length || to < 0 || to >= stops.length) return [...stops];
  const next = [...stops];
  [next[index], next[to]] = [next[to], next[index]];
  return next;
}

export interface StopTypeLike {
  stopType: string;
}

/** One problem per rule broken, in the server's own order, so the screen can show them before the round trip
 * that would otherwise report them. Empty when the stops would pass. */
export function stopProblems(stops: readonly StopTypeLike[], isRoundTrip: boolean, sameOriginDestination: boolean): string[] {
  const problems: string[] = [];
  if (stops.length < 2) { problems.push('A route needs at least two stops.'); return problems; }
  if (stops[0].stopType !== 'Origin') problems.push('The first stop must be Origin.');
  if (stops[stops.length - 1].stopType !== 'Destination') problems.push('The last stop must be Destination.');
  for (let i = 1; i < stops.length - 1; i++) {
    if (stops[i].stopType === 'Origin' || stops[i].stopType === 'Destination') { problems.push('Only the first stop can be Origin and only the last can be Destination.'); break; }
  }
  if (!isRoundTrip && sameOriginDestination) problems.push('Origin and destination must differ unless the route is a round trip.');
  return problems;
}
