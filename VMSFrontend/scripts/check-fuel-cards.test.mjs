// Tests for the framework-free rules of the Fuel Card screen (FSD §28).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { isExpiringSoon, isPastExpiry, statusSeverity } from '../src/app/pages/fuel-cards/fuel-card-logic.ts';

test('status chip severity', () => {
  assert.equal(statusSeverity('Active'), 'success');
  assert.equal(statusSeverity('Inactive'), 'secondary');
  assert.equal(statusSeverity('Expired'), 'warn');
  assert.equal(statusSeverity('Blocked'), 'danger');
});

test('past expiry', () => {
  assert.equal(isPastExpiry('2026-01-01', '2026-02-01'), true);
  assert.equal(isPastExpiry('2026-03-01', '2026-02-01'), false);
  assert.equal(isPastExpiry('2026-02-01', '2026-02-01'), false);
});

test('expiring soon: within the window, and not already past it', () => {
  assert.equal(isExpiringSoon('2026-02-15', '2026-02-01', 30), true);
  assert.equal(isExpiringSoon('2026-04-01', '2026-02-01', 30), false);
  assert.equal(isExpiringSoon('2026-01-15', '2026-02-01', 30), false); // already past
});
