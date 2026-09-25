// Tests for the framework-free rules of the Route screen (FSD §17).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { moveStop, previewString, statusSeverity, stopProblems } from '../src/app/pages/routes/route-logic.ts';

test('status chip severity', () => {
  assert.equal(statusSeverity('Active'), 'success');
  assert.equal(statusSeverity('Inactive'), 'danger');
});

test('the preview string joins city abbreviations with an arrow', () => {
  assert.equal(previewString([{ cityAbbreviation: 'LHR' }, { cityAbbreviation: 'SKP' }, { cityAbbreviation: 'FSD' }]), 'LHR → SKP → FSD');
  assert.equal(previewString([]), '');
});

test('moving a stop swaps it with its neighbour; out-of-range is a no-op', () => {
  assert.deepEqual(moveStop(['a', 'b', 'c'], 1, -1), ['b', 'a', 'c']);
  assert.deepEqual(moveStop(['a', 'b', 'c'], 1, 1), ['a', 'c', 'b']);
  assert.deepEqual(moveStop(['a', 'b', 'c'], 0, -1), ['a', 'b', 'c']);
  assert.deepEqual(moveStop(['a', 'b', 'c'], 2, 1), ['a', 'b', 'c']);
});

test('fewer than two stops is the only problem reported', () => {
  assert.deepEqual(stopProblems([], false, false), ['A route needs at least two stops.']);
  assert.deepEqual(stopProblems([{ stopType: 'Origin' }], false, false), ['A route needs at least two stops.']);
});

test('first must be Origin, last must be Destination, no Origin/Destination in the middle', () => {
  assert.deepEqual(stopProblems([{ stopType: 'Via' }, { stopType: 'Destination' }], false, false), ['The first stop must be Origin.']);
  assert.deepEqual(stopProblems([{ stopType: 'Origin' }, { stopType: 'Via' }], false, false), ['The last stop must be Destination.']);
  assert.deepEqual(stopProblems([{ stopType: 'Origin' }, { stopType: 'Destination' }, { stopType: 'Via' }], false, false), ['The last stop must be Destination.', 'Only the first stop can be Origin and only the last can be Destination.']);
  assert.deepEqual(stopProblems([{ stopType: 'Origin' }, { stopType: 'Origin' }, { stopType: 'Destination' }], false, false), ['Only the first stop can be Origin and only the last can be Destination.']);
  assert.deepEqual(stopProblems([{ stopType: 'Origin' }, { stopType: 'Destination' }], false, false), []);
});

test('origin and destination must differ unless it is a round trip', () => {
  assert.deepEqual(stopProblems([{ stopType: 'Origin' }, { stopType: 'Destination' }], false, true), ['Origin and destination must differ unless the route is a round trip.']);
  assert.deepEqual(stopProblems([{ stopType: 'Origin' }, { stopType: 'Destination' }], true, true), []);
});
