// Tests for the framework-free rules of the Payments screens (FSD §37, §37.5, §40).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { isCredit, isOverpayment, optionsOf, remainingAfterPayment, statusSeverity, words } from '../src/app/pages/payments/payment-logic.ts';

test('values read as words', () => {
  assert.equal(words('DirectToAccount'), 'Direct to account');
  assert.equal(words('WriteOff'), 'Write off');
  assert.equal(words(null), '');
});

test('options are built label/value from a word list', () => {
  assert.deepEqual(optionsOf(['WriteOff', 'Discount']), [
    { label: 'Write off', value: 'WriteOff' },
    { label: 'Discount', value: 'Discount' },
  ]);
});

test('status chip severity', () => {
  assert.equal(statusSeverity('Posted'), 'success');
  assert.equal(statusSeverity('Reversed'), 'danger');
});

test('overpayment: amount greater than the invoice balance', () => {
  assert.equal(isOverpayment(1000, 500), true);
  assert.equal(isOverpayment(500, 1000), false);
  assert.equal(isOverpayment(500, 500), false);
});

test('remaining after payment never goes below zero', () => {
  assert.equal(remainingAfterPayment(1000, 400), 600);
  assert.equal(remainingAfterPayment(1000, 1000), 0);
  assert.equal(remainingAfterPayment(1000, 1500), 0);
});

test('a credit is a negative balance', () => {
  assert.equal(isCredit(-500), true);
  assert.equal(isCredit(500), false);
  assert.equal(isCredit(0), false);
});
