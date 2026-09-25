// Tests for the framework-free rules of the customer screens (FSD §10-§15).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { describeChange, entityLabel, isEmail, isMobile, isPhone, optionsOf, statusMoves, statusSeverity, words } from '../src/app/pages/customers/customer-logic.ts';

test('values read as words, abbreviations stay', () => {
  assert.equal(words('GrossTripAmount'), 'Gross trip amount');
  assert.equal(words('InvoiceSubtotal'), 'Invoice subtotal');
  assert.equal(words('POD'), 'POD');
  assert.equal(words(null), '');
});

test('options are built label/value from a word list', () => {
  assert.deepEqual(optionsOf(['Allow', 'Warn', 'Block']), [
    { label: 'Allow', value: 'Allow' },
    { label: 'Warn', value: 'Warn' },
    { label: 'Block', value: 'Block' },
  ]);
});

test('mobile format: 03XX-XXXXXXX, 11 digits with no dash, or + and 8-15 digits', () => {
  assert.equal(isMobile('0300-1234567'), true);
  assert.equal(isMobile('03001234567'), true);
  assert.equal(isMobile('+923001234567'), true);
  assert.equal(isMobile('12345'), false);
  assert.equal(isMobile('0300123456'), false); // one digit short
  assert.equal(isMobile(''), false);
});

test('phone format: digits, + ( ) - and spaces, 5 to 20 characters', () => {
  assert.equal(isPhone('042-1234567'), true);
  assert.equal(isPhone('(042) 123 4567'), true);
  assert.equal(isPhone('abc'), false);
  assert.equal(isPhone('123'), false); // too short
});

test('email needs an @ and a dot in the host, at most 150 characters', () => {
  assert.equal(isEmail('finance@acme.com'), true);
  assert.equal(isEmail('finance@acme'), false);
  assert.equal(isEmail('not-an-email'), false);
  assert.equal(isEmail('a@' + 'b'.repeat(150) + '.com'), false);
});

test('status chip severity', () => {
  assert.equal(statusSeverity('Active'), 'success');
  assert.equal(statusSeverity('Inactive'), 'danger');
  assert.equal(statusSeverity('Draft'), 'secondary');
});

test('where a customer can move from each status', () => {
  assert.deepEqual(statusMoves('Draft'), ['Active']);
  assert.deepEqual(statusMoves('Active'), ['Inactive']);
  assert.deepEqual(statusMoves('Inactive'), ['Active']);
});

test('entity label reads the row entity in words', () => {
  assert.equal(entityLabel('Customer'), 'Customer');
  assert.equal(entityLabel('CustomerContact'), 'Contact');
  assert.equal(entityLabel('CustomerTaxRule'), 'Tax rule');
  assert.equal(entityLabel('SomethingUnknown'), 'SomethingUnknown');
});

test('a history row reads as one line', () => {
  assert.equal(describeChange({ entity: 'Customer', action: 'FieldChanged', field: 'Status', oldValue: 'Draft', newValue: 'Active', restricted: false }), 'Status: Draft → Active');
  assert.equal(describeChange({ entity: 'CustomerContact', action: 'Created', field: null, oldValue: null, newValue: '{"Name":"Ali Raza"}', restricted: false }), 'Added contact Ali Raza');
  assert.equal(describeChange({ entity: 'CustomerContact', action: 'Deleted', field: null, oldValue: '{"Name":"Ali Raza"}', newValue: null, restricted: false }), 'Removed contact Ali Raza');
  assert.equal(describeChange({ entity: 'Customer', action: 'FieldChanged', field: 'CreditLimit', oldValue: '50000', newValue: '100000', restricted: true }), 'Credit limit changed (values hidden)');
});
