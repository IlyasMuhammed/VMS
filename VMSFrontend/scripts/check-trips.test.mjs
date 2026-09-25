// Tests for the framework-free rules of the Trip screens (FSD §21-§31).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  canCancel, canHold, cancelNeedsElevatedPermission, fuelAmountMismatch, needsEndOdometer, needsStartOdometer,
  nextMoves, optionsOf, podSeverity, expenseSeverity, statusSeverity, words,
} from '../src/app/pages/trips/trip-logic.ts';

test('values read as words', () => {
  assert.equal(words('InTransit'), 'In transit');
  assert.equal(words('AtPickup'), 'At pickup');
  assert.equal(words('CNG'), 'CNG');
  assert.equal(words(null), '');
});

test('options are built label/value from a word list', () => {
  assert.deepEqual(optionsOf(['Cash', 'Card']), [
    { label: 'Cash', value: 'Cash' },
    { label: 'Card', value: 'Card' },
  ]);
});

test('status chip severity', () => {
  assert.equal(statusSeverity('Completed'), 'success');
  assert.equal(statusSeverity('Cancelled'), 'danger');
  assert.equal(statusSeverity('OnHold'), 'warn');
  assert.equal(statusSeverity('Draft'), 'secondary');
  assert.equal(statusSeverity('InTransit'), 'info');
});

test("the normal transition table mirrors the server's own TripLifecycle", () => {
  assert.deepEqual(nextMoves('Draft'), ['Planned']);
  assert.deepEqual(nextMoves('Started'), ['InTransit', 'AtPickup']);
  assert.deepEqual(nextMoves('Loaded'), ['InTransit', 'AtDelivery']);
  assert.deepEqual(nextMoves('Delivered'), ['Completed']);
  assert.deepEqual(nextMoves('Completed'), []);
  assert.deepEqual(nextMoves('Cancelled'), []);
});

test('hold is offered from every non-terminal status except Draft', () => {
  assert.equal(canHold('Planned'), true);
  assert.equal(canHold('Delivered'), true);
  assert.equal(canHold('Draft'), false);
  assert.equal(canHold('Completed'), false);
  assert.equal(canHold('Cancelled'), false);
});

test('cancel is offered from everywhere except the two terminal statuses', () => {
  assert.equal(canCancel('Draft'), true);
  assert.equal(canCancel('OnHold'), true);
  assert.equal(canCancel('Completed'), false);
  assert.equal(canCancel('Cancelled'), false);
});

test('cancel needs the elevated permission from Started onward, not before', () => {
  assert.equal(cancelNeedsElevatedPermission('Draft'), false);
  assert.equal(cancelNeedsElevatedPermission('Planned'), false);
  assert.equal(cancelNeedsElevatedPermission('Assigned'), false);
  assert.equal(cancelNeedsElevatedPermission('Started'), true);
  assert.equal(cancelNeedsElevatedPermission('InTransit'), true);
  assert.equal(cancelNeedsElevatedPermission('OnHold'), true);
});

test('start/end odometer are asked for on exactly the transitions that need them', () => {
  assert.equal(needsStartOdometer('Started'), true);
  assert.equal(needsStartOdometer('InTransit'), false);
  assert.equal(needsEndOdometer('Delivered'), true);
  assert.equal(needsEndOdometer('Started'), false);
});

test('a fuel amount more than 1 away from quantity times rate is a mismatch', () => {
  assert.equal(fuelAmountMismatch(50, 280, 14000), false);
  assert.equal(fuelAmountMismatch(50, 280, 14000.5), false);
  assert.equal(fuelAmountMismatch(50, 280, 14500), true);
});

test('POD and expense chip severities', () => {
  assert.equal(podSeverity('Approved'), 'success');
  assert.equal(podSeverity('Rejected'), 'danger');
  assert.equal(podSeverity('Uploaded'), 'warn');
  assert.equal(podSeverity(null), 'secondary');
  assert.equal(expenseSeverity('Approved'), 'success');
  assert.equal(expenseSeverity('Rejected'), 'danger');
  assert.equal(expenseSeverity('Pending'), 'warn');
});
