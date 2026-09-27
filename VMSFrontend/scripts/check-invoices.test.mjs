// Tests for the framework-free rules of the Invoice screens (FSD §32-§36).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { blockingReason, evidenceStatusSeverity, formatBytes, paymentStatusSeverity, statusSeverity, words } from '../src/app/pages/invoices/invoice-logic.ts';

test('values read as words', () => {
  assert.equal(words('PartiallyPaid'), 'Partially paid');
  assert.equal(words('Generated'), 'Generated');
  assert.equal(words(null), '');
});

test('status chip severity', () => {
  assert.equal(statusSeverity('Submitted'), 'success');
  assert.equal(statusSeverity('Generated'), 'info');
  assert.equal(statusSeverity('Draft'), 'secondary');
  assert.equal(statusSeverity('Cancelled'), 'danger');
  assert.equal(statusSeverity('Inactive'), 'danger');
});

test('payment status chip severity', () => {
  assert.equal(paymentStatusSeverity('Paid'), 'success');
  assert.equal(paymentStatusSeverity('PartiallyPaid'), 'warn');
  assert.equal(paymentStatusSeverity('Unpaid'), 'danger');
});

test('blocking reasons are worded for the screen; an unknown code still shows something', () => {
  assert.equal(blockingReason('RATE_MISSING'), 'Rate not configured for this date');
  assert.equal(blockingReason('POD_MISSING'), 'Proof of delivery missing');
  assert.equal(blockingReason('SOMETHING_NEW'), 'SOMETHING_NEW');
});

test('evidence status chip severity', () => {
  assert.equal(evidenceStatusSeverity('Generated'), 'success');
  assert.equal(evidenceStatusSeverity('Queued'), 'info');
  assert.equal(evidenceStatusSeverity('Failed'), 'danger');
  assert.equal(evidenceStatusSeverity('Superseded'), 'secondary');
});

test('byte counts read as a size', () => {
  assert.equal(formatBytes(512), '512 B');
  assert.equal(formatBytes(1536), '1.5 KB');
  assert.equal(formatBytes(5 * 1024 * 1024), '5.0 MB');
});
