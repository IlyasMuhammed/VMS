/**
 * Date and time handling for the whole app (FSD §24.4). Two kinds of value, never mixed:
 *
 *  - An INSTANT (created on, last sign-in, an audit time) is a moment in history. The API sends it as
 *    ISO 8601 in UTC (`2026-10-10T09:30:00Z`); we show it in the viewer's own time zone and locale.
 *  - A BUSINESS DATE (acquisition date, due date, expiry) is a calendar day with no time and no zone.
 *    The API sends it as `YYYY-MM-DD`; it reads the same everywhere. It must never be turned into a
 *    `Date` with `new Date('2026-10-10')`, which is midnight UTC and shows as the 9th west of Greenwich.
 *
 * This file has no framework imports so it can be tested under any time zone (see scripts/check-datetime.mjs),
 * and it uses only syntax that Node can run as it is.
 */

/** What is shown where there is no value (or none the viewer may see): a dash, never a zero or a 1970 date. */
export const EMPTY = '—';

export type DateInput = string | Date | null | undefined;
export type InstantStyle = 'date' | 'datetime' | 'time';

export interface InstantFormat {
  /** Default `datetime`. */
  style?: InstantStyle;
  /** Name the zone beside the time (`2:30 PM GMT+5`), as exports and printouts must (NFR-DT-07). */
  withZone?: boolean;
  /** Override the viewer's locale / zone. Only tests and exports need this. */
  locale?: string;
  timeZone?: string;
}

const OFFSET = /(Z|[+-]\d{2}(:?\d{2})?)$/i;
const DATE_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/;

// ── Instants ────────────────────────────────────────────────────────────────────

/** The moment an API instant stands for. The API always includes an offset, so one without it is a bug, not a guess. */
export function parseInstant(value: string | Date): Date {
  if (value instanceof Date) return value;
  const text = value.trim();
  if (!text.includes('T') || !OFFSET.test(text)) throw new Error(`Not an ISO 8601 instant with an offset: "${value}"`);
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) throw new Error(`Not a valid instant: "${value}"`);
  return date;
}

/** An instant in the viewer's zone and locale. Empty input gives a dash; a malformed one is logged and shown as it arrived. */
export function formatInstant(value: DateInput, format: InstantFormat = {}): string {
  if (value === null || value === undefined || value === '') return EMPTY;

  let date: Date;
  try {
    date = parseInstant(value);
  } catch (error) {
    console.error(error);
    return String(value);
  }

  const { style = 'datetime', withZone = false, locale, timeZone } = format;
  const options: Intl.DateTimeFormatOptions = { timeZone };
  if (style !== 'time') Object.assign(options, { year: 'numeric', month: 'short', day: 'numeric' });
  if (style !== 'date') Object.assign(options, { hour: 'numeric', minute: '2-digit' });
  if (withZone) options.timeZoneName = 'short';

  return new Intl.DateTimeFormat(locale, options).format(date);
}

/** What to send for an instant: UTC with a `Z`. */
export const instantToApi = (date: Date): string => date.toISOString();

/** The current instant, for a field that records when something was done. */
export const nowInstant = (now: Date = new Date()): string => now.toISOString();

/** The viewer's IANA time zone, for the `X-Time-Zone` header exports use. */
export const clientTimeZone = (): string | undefined => Intl.DateTimeFormat().resolvedOptions().timeZone;

// ── Business dates ──────────────────────────────────────────────────────────────

const pad = (n: number, width = 2) => String(n).padStart(width, '0');

/** A `YYYY-MM-DD` string as the calendar day it names, at local midnight. Returns null if it is not a real date. */
export function parseDateOnly(value: string): Date | null {
  const match = DATE_ONLY.exec(value.trim());
  if (!match) return null;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(year, month - 1, day);
  // `new Date(2026, 1, 31)` quietly becomes 3 March; a date that changed when built was never a date.
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day ? date : null;
}

/** The calendar day a `Date` shows on the viewer's own clock, as `YYYY-MM-DD`. Use it for whatever a date picker returns. */
export const toDateOnly = (date: Date): string => `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;

/** Today on the viewer's machine (NFR-DT-04): the default of a date picker. */
export const todayDateOnly = (now: Date = new Date()): string => toDateOnly(now);

/** True if the day is later than today on the viewer's machine, not on the server (NFR-DT-04). */
export function isFutureDate(value: string, now: Date = new Date()): boolean {
  return value > todayDateOnly(now); // same-shaped ISO dates sort as text
}

/** A business date for the screen, in the viewer's locale. The same day everywhere; never shifted by a zone. */
export function formatDate(value: DateInput, format: { locale?: string } = {}): string {
  if (value === null || value === undefined || value === '') return EMPTY;

  const day = value instanceof Date ? value : parseDateOnly(value);
  if (!day) return String(value);

  return new Intl.DateTimeFormat(format.locale, { year: 'numeric', month: 'short', day: 'numeric' }).format(day);
}
