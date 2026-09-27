// Tests for the framework-free rules of the Currency Setup screen (FSD §13A).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { isCurrencyCode, statusSeverity } from '../src/app/pages/admin/currency-logic.ts';

test('status chip severity', () => {
  assert.equal(statusSeverity('Active'), 'success');
  assert.equal(statusSeverity('Inactive'), 'danger');
});

test('currency code format: 3 letters', () => {
  assert.equal(isCurrencyCode('PKR'), true);
  assert.equal(isCurrencyCode('usd'), true);
  assert.equal(isCurrencyCode('PK'), false);
  assert.equal(isCurrencyCode('PKRS'), false);
  assert.equal(isCurrencyCode('123'), false);
});
