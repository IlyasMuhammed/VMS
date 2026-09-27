// Tests for the framework-free rules of the Advances screen (FSD §37.4).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { canAct, remaining, statusSeverity } from '../src/app/pages/advances/advance-logic.ts';

test('status chip severity', () => {
  assert.equal(statusSeverity('Open'), 'success');
  assert.equal(statusSeverity('Applied'), 'info');
  assert.equal(statusSeverity('Refunded'), 'warn');
  assert.equal(statusSeverity('Reversed'), 'danger');
});

test('move/refund/reverse only act while the advance is still Open', () => {
  assert.equal(canAct('Open'), true);
  assert.equal(canAct('Applied'), false);
  assert.equal(canAct('Refunded'), false);
  assert.equal(canAct('Reversed'), false);
});

test('remaining is the amount not yet applied or refunded', () => {
  assert.equal(remaining({ amount: 1000, appliedAmount: 0, refundedAmount: 0 }), 1000);
  assert.equal(remaining({ amount: 1000, appliedAmount: 600, refundedAmount: 0 }), 400);
  assert.equal(remaining({ amount: 1000, appliedAmount: 0, refundedAmount: 1000 }), 0);
});
