// Tests for the framework-free rules of the Customer Ledger screens (FSD §40A).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { balanceLabel, balanceSeverity, entryTypeLabel } from '../src/app/pages/ledger/ledger-logic.ts';

test('entry types are worded for the screen; an unknown code still shows something', () => {
  assert.equal(entryTypeLabel('INVOICE'), 'Invoice');
  assert.equal(entryTypeLabel('ADVANCE_APPLY_OUT'), 'Advance applied (trip)');
  assert.equal(entryTypeLabel('SOMETHING_NEW'), 'SOMETHING_NEW');
});

test('a balance always reads with its Dr/Cr suffix, never a bare signed number', () => {
  assert.equal(balanceLabel(500), '500 Dr');
  assert.equal(balanceLabel(-500), '500 Cr');
  assert.equal(balanceLabel(0), '0 Dr');
});

test('balance severity: owed is danger, a credit is success, zero is neutral', () => {
  assert.equal(balanceSeverity(500), 'danger');
  assert.equal(balanceSeverity(-500), 'success');
  assert.equal(balanceSeverity(0), 'neutral');
});
