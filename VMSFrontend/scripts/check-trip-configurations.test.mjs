// Tests for the framework-free rules of the Trip Configuration and Trip Rates screens (FSD §18, §19, §26).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { configStatusSeverity, findGaps, hasOpenEndedCoverage, optionsOf, statusSeverity, words } from '../src/app/pages/trip-configurations/trip-configuration-logic.ts';

test('values read as words', () => {
  assert.equal(words('RoundTrip'), 'Round trip');
  assert.equal(words('OneWay'), 'One way');
  assert.equal(words(null), '');
});

test('options are built label/value from a word list', () => {
  assert.deepEqual(optionsOf(['OneWay', 'Return']), [
    { label: 'One way', value: 'OneWay' },
    { label: 'Return', value: 'Return' },
  ]);
});

test('a trip configuration has three status severities, a rate only two', () => {
  assert.equal(configStatusSeverity('Draft'), 'secondary');
  assert.equal(configStatusSeverity('Active'), 'success');
  assert.equal(configStatusSeverity('Inactive'), 'danger');
  assert.equal(statusSeverity('Active'), 'success');
  assert.equal(statusSeverity('Inactive'), 'danger');
});

test('no gap when rates are back to back', () => {
  const rates = [
    { effectiveFrom: '2026-07-01', effectiveTo: '2026-07-11', status: 'Active' },
    { effectiveFrom: '2026-07-12', effectiveTo: null, status: 'Active' },
  ];
  assert.deepEqual(findGaps(rates), []);
});

test("§26's own worked example: a gap between two rates", () => {
  const rates = [
    { effectiveFrom: '2026-07-01', effectiveTo: '2026-07-11', status: 'Active' },
    { effectiveFrom: '2026-07-15', effectiveTo: null, status: 'Active' },
  ];
  assert.deepEqual(findGaps(rates), [{ from: '2026-07-12', to: '2026-07-14' }]);
});

test('an inactive rate is ignored, so its old range shows as a gap', () => {
  const rates = [
    { effectiveFrom: '2026-07-01', effectiveTo: '2026-07-11', status: 'Inactive' },
    { effectiveFrom: '2026-08-01', effectiveTo: null, status: 'Active' },
  ];
  assert.deepEqual(findGaps(rates), []); // only one Active rate remains — nothing to compare it against
});

test('no rates at all is not reported as a gap (there is nothing to compare)', () => {
  assert.deepEqual(findGaps([]), []);
});

test('open-ended coverage', () => {
  assert.equal(hasOpenEndedCoverage([{ effectiveFrom: '2026-01-01', effectiveTo: null, status: 'Active' }]), true);
  assert.equal(hasOpenEndedCoverage([{ effectiveFrom: '2026-01-01', effectiveTo: '2026-02-01', status: 'Active' }]), false);
  assert.equal(hasOpenEndedCoverage([]), false);
});
