// Tests for src/app/core/datetime/datetime.ts. Run through `npm run check:datetime`, which repeats them in
// several time zones: a date helper that is only right in the developer's own zone is the bug this guards.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as dt from '../src/app/core/datetime/datetime.ts';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
// ICU versions differ on which kind of space precedes AM/PM; compare with any whitespace as one plain space.
const plain = (text) => text.replace(/\s/g, ' ');

test(`[${zone}] a business date reads the same everywhere, with no shift`, () => {
  for (const [input, shown] of [
    ['2026-10-10', 'Oct 10, 2026'],
    ['2026-12-31', 'Dec 31, 2026'],
    ['2027-01-01', 'Jan 1, 2027'],
    ['2028-02-29', 'Feb 29, 2028'],
  ]) {
    assert.equal(plain(dt.formatDate(input, { locale: 'en-US' })), shown);
  }
});

test(`[${zone}] parsing a business date gives that calendar day at local midnight`, () => {
  const day = dt.parseDateOnly('2026-10-10');
  assert.deepEqual([day.getFullYear(), day.getMonth(), day.getDate(), day.getHours()], [2026, 9, 10, 0]);
});

test(`[${zone}] what is not a real date is not parsed`, () => {
  for (const bad of ['2026-02-31', '2026-13-01', '2026-00-10', '10/10/2026', '2026-10-10T00:00:00Z', '2026-1-1', '', 'tomorrow']) {
    assert.equal(dt.parseDateOnly(bad), null, bad);
  }
  assert.equal(dt.formatDate('2026-02-31'), '2026-02-31'); // shown as it arrived, not as a wrong date
});

test(`[${zone}] a date picker's value becomes the day it shows, not the UTC day`, () => {
  // 23:59 and 00:30 local are where toISOString().slice(0, 10) gives the wrong day in some zone or other.
  assert.equal(dt.toDateOnly(new Date(2026, 9, 10, 23, 59)), '2026-10-10');
  assert.equal(dt.toDateOnly(new Date(2026, 9, 10, 0, 30)), '2026-10-10');
  assert.equal(dt.toDateOnly(new Date(2026, 0, 1, 0, 0)), '2026-01-01');
});

test(`[${zone}] today and "not in the future" are judged on the viewer's own clock`, () => {
  const justAfterMidnight = new Date(2026, 9, 10, 0, 30); // local
  assert.equal(dt.todayDateOnly(justAfterMidnight), '2026-10-10');
  assert.equal(dt.isFutureDate('2026-10-10', justAfterMidnight), false); // today is allowed
  assert.equal(dt.isFutureDate('2026-10-09', justAfterMidnight), false);
  assert.equal(dt.isFutureDate('2026-10-11', justAfterMidnight), true);
  const lateEvening = new Date(2026, 9, 10, 23, 59);
  assert.equal(dt.isFutureDate('2026-10-10', lateEvening), false);
});

test(`[${zone}] an instant needs an offset, whatever shape it takes`, () => {
  for (const ok of ['2026-10-10T09:30:00Z', '2026-10-10T09:30:00.000Z', '2026-10-10T14:30:00+05:00', '2026-10-10T14:30:00+0500', '2026-10-10T01:30:00-08:00']) {
    assert.equal(dt.parseInstant(ok).toISOString(), '2026-10-10T09:30:00.000Z', ok);
  }
  for (const bad of ['2026-10-10T09:30:00', '2026-10-10', '09:30', 'not a date', '']) {
    assert.throws(() => dt.parseInstant(bad), Error, bad);
  }
});

test(`[${zone}] one instant is shown in each viewer's own zone`, () => {
  const instant = '2026-10-10T09:30:00Z';
  const at = (timeZone, style) => plain(dt.formatInstant(instant, { locale: 'en-US', timeZone, style }));

  assert.equal(at('UTC'), 'Oct 10, 2026, 9:30 AM');
  assert.equal(at('Asia/Karachi'), 'Oct 10, 2026, 2:30 PM');
  assert.equal(at('America/Los_Angeles'), 'Oct 10, 2026, 2:30 AM');
  assert.equal(at('Pacific/Pago_Pago'), 'Oct 9, 2026, 10:30 PM'); // still the day before
  assert.equal(at('Pacific/Kiritimati'), 'Oct 10, 2026, 11:30 PM');
  assert.equal(at('Asia/Karachi', 'date'), 'Oct 10, 2026');
  assert.equal(at('Asia/Karachi', 'time'), '2:30 PM');
});

test(`[${zone}] with no zone given, the viewer's own zone is used`, () => {
  const instant = '2026-10-10T09:30:00Z';
  assert.equal(dt.formatInstant(instant, { locale: 'en-US' }), dt.formatInstant(instant, { locale: 'en-US', timeZone: zone }));
});

test(`[${zone}] a printout can name the zone beside the time`, () => {
  const text = plain(dt.formatInstant('2026-10-10T09:30:00Z', { locale: 'en-US', timeZone: 'Asia/Karachi', withZone: true }));
  assert.match(text, /2:30 PM GMT\+5$/);
});

test(`[${zone}] no value is a dash, never a zero or 1970`, () => {
  for (const nothing of [null, undefined, '']) {
    assert.equal(dt.formatInstant(nothing), dt.EMPTY);
    assert.equal(dt.formatDate(nothing), dt.EMPTY);
  }
});

test(`[${zone}] a malformed instant is reported and shown as it arrived instead of breaking the page`, (t) => {
  const logged = t.mock.method(console, 'error', () => {});
  assert.equal(dt.formatInstant('2026-10-10T09:30:00'), '2026-10-10T09:30:00');
  assert.equal(logged.mock.callCount(), 1);
});

test(`[${zone}] instants are sent as UTC`, () => {
  assert.equal(dt.instantToApi(new Date(Date.UTC(2026, 9, 10, 9, 30))), '2026-10-10T09:30:00.000Z');
  assert.equal(dt.nowInstant(new Date(Date.UTC(2026, 0, 1))), '2026-01-01T00:00:00.000Z');
});

test(`[${zone}] the viewer's zone is reported as an IANA name`, () => {
  assert.equal(dt.clientTimeZone(), zone);
});
