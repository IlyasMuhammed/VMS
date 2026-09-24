// Tests for the framework-free rules of the partner form: formats, what is mandatory, duplicate handling, history wording,
// and that the screen's vocabulary is the API's.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
  ADDRESS_TYPES, BILLING_CYCLES, COMMISSION_BASES, CUSTOMER_TYPES, EMPLOYMENT_TYPES, FILER_STATUSES, LICENCE_TYPES, PARTY_TYPES, RATE_BASES, ROLES, SUPPLY_CATEGORIES,
  blockingMatches, canSaveDespite, defaultDisplayName, describeChange, entityLabel, fieldLabel, formPath, isAccountNumber, isCnic, isEmail, isIban, isNtn, isPhone,
  maskCnic, matchReason, matchesToAcknowledge, normalizeMobile, panelsFor, panellessRoles, requirements, roleLabel, snapshotTitle, tabOfField, words,
} from '../src/app/pages/partners/partner-logic.ts';

// ── Formats: the same cases the API's tests use, so screen and server cannot drift ──

test('a CNIC is five, seven and one digits with dashes', () => {
  assert.equal(isCnic('35202-1234567-1'), true);
  for (const bad of ['3520212345671', '35202-123456-1', '35202-1234567-12', '3520a-1234567-1', '', null, undefined]) assert.equal(isCnic(bad), false, String(bad));
});

test('an NTN is read leniently, as the API reads it', () => {
  for (const ok of ['1234567', '1234567-8', '1234567890123', '35202-1234567-1']) assert.equal(isNtn(ok), true, ok);
  for (const bad of ['123456', 'ABCDEFG', '']) assert.equal(isNtn(bad), false, bad);
});

test('a mobile is stored in one form', () => {
  assert.equal(normalizeMobile('0300-1234567'), '0300-1234567');
  assert.equal(normalizeMobile('03001234567'), '0300-1234567', 'a missing dash is added');
  assert.equal(normalizeMobile('0300 1234567'), '0300-1234567', 'spaces are ignored');
  assert.equal(normalizeMobile('+923001234567'), '+923001234567');
  for (const bad of ['0400-1234567', '0300-123456', 'hello', '', null]) assert.equal(normalizeMobile(bad), null, String(bad));
});

test('other formats: phone, email, account number, IBAN', () => {
  assert.equal(isPhone('042-35761234'), true);
  assert.equal(isPhone('12'), false);
  assert.equal(isEmail('accounts@ali-traders.pk'), true);
  for (const bad of ['accounts@localhost', 'Ali <a@b.pk>', 'not an email', '']) assert.equal(isEmail(bad), false, bad);
  assert.equal(isAccountNumber('0123-4567-89'), true);
  assert.equal(isAccountNumber('01234567890123456789012345678901234'), false, '35 characters');
  assert.equal(isAccountNumber('12AB'), false);
  assert.equal(isIban('PK36SCBL0000001123456702'), true);
  assert.equal(isIban('pk36 scbl 0000 0011 2345 6702'), true, 'case and spaces are ignored');
  assert.equal(isIban('PK37SCBL0000001123456702'), false, 'one wrong check digit');
  assert.equal(isIban('PK36SCBL000000112345670'), false);
  assert.equal(isIban('GB29NWBK60161331926819'), false);
});

test('the dashes go into a CNIC as it is typed', () => {
  assert.equal(maskCnic('3'), '3');
  assert.equal(maskCnic('35202'), '35202');
  assert.equal(maskCnic('352021'), '35202-1');
  assert.equal(maskCnic('3520212345671'), '35202-1234567-1');
  assert.equal(maskCnic('35202-1234567-1'), '35202-1234567-1', 'already masked');
  assert.equal(maskCnic('35202123456719999'), '35202-1234567-1', 'extra digits are dropped');
  assert.equal(maskCnic('35a20b2'), '35202');
  assert.equal(maskCnic(null), '');
});

// ── What is mandatory ───────────────────────────────────────────────────────────────

test('a person needs a CNIC, a company an NTN, a customer or bank an email', () => {
  assert.deepEqual(requirements('Person', ['Workshop']), { cnic: true, ntn: false, email: false });
  assert.deepEqual(requirements('Company', ['Vendor']), { cnic: false, ntn: true, email: false });
  assert.equal(requirements('Company', ['Customer']).email, true);
  assert.equal(requirements('Person', ['Driver', 'Bank']).email, true);
  assert.deepEqual(requirements(null, []), { cnic: false, ntn: false, email: false });
});

test('only the roles with fields the API stores have a panel', () => {
  assert.deepEqual(panelsFor(['Driver', 'Workshop', 'Customer']), ['Driver', 'Customer']);
  assert.deepEqual(panellessRoles(['Driver', 'Workshop', 'Bank']), ['Workshop', 'Bank']);
  assert.deepEqual(panelsFor([]), []);
});

test('the display name defaults to the legal name cut to 60 characters', () => {
  assert.equal(defaultDisplayName('  Ali Traders  '), 'Ali Traders');
  assert.equal(defaultDisplayName('x'.repeat(80)).length, 60);
});

test('roles read as words', () => {
  assert.equal(roleLabel('RunningCustomer'), 'Running customer');
  assert.equal(roleLabel('FuelCardCompany'), 'Fuel card company');
  assert.equal(roleLabel('Unknown'), 'Unknown');
  assert.equal(words('CargoCompany'), 'Cargo company');
  assert.equal(words('PerTonne'), 'Per tonne');
  assert.equal(words('LTV'), 'LTV', 'an abbreviation stays as it is');
  assert.equal(words('AdHoc'), 'Ad hoc');
  assert.equal(words(null), '');
});

// ── The API's field names ───────────────────────────────────────────────────────────

test('the API names a row as contacts[1].mobile and Angular as contacts.1.mobile', () => {
  assert.equal(formPath('contacts[1].mobile'), 'contacts.1.mobile');
  assert.equal(formPath('bankAccounts[12].iban'), 'bankAccounts.12.iban');
  assert.equal(formPath('driver.licenceNo'), 'driver.licenceNo');
  assert.equal(formPath('legalName'), 'legalName');
});

test('an error opens the tab its field is on', () => {
  assert.equal(tabOfField('legalName'), 'general');
  assert.equal(tabOfField('roles'), 'general');
  assert.equal(tabOfField('driver.licenceNo'), 'roles');
  assert.equal(tabOfField('vendor.supplyCategories'), 'roles');
  assert.equal(tabOfField('contacts[0].mobile'), 'contacts');
  assert.equal(tabOfField('contacts'), 'contacts');
  assert.equal(tabOfField('addresses[2].cityId'), 'addresses');
  assert.equal(tabOfField('bankAccounts[0].iban'), 'bank');
});

// ── Duplicates ──────────────────────────────────────────────────────────────────────

const match = (over) => ({ id: 1, bpCode: 'BP-26-00001', legalName: 'Ali Traders', roles: [], cityId: 1, status: 'Active', matchType: 'NameAndCity', isHard: false, sameCity: true, ...over });

test('a CNIC or NTN already on file stops the save, whatever else is ticked', () => {
  const hard = [match({ matchType: 'Cnic', isHard: true })];
  assert.equal(blockingMatches(hard).length, 1);
  assert.equal(canSaveDespite(hard, true), false);
  assert.equal(canSaveDespite(hard, false), false);
});

test('a shared mobile or the same name in the same city needs the tick', () => {
  for (const type of ['Mobile', 'NameAndCity']) {
    const soft = [match({ matchType: type })];
    assert.equal(matchesToAcknowledge(soft).length, 1, type);
    assert.equal(canSaveDespite(soft, false), false, type);
    assert.equal(canSaveDespite(soft, true), true, type);
  }
});

test('a similar name is only asked about in the same city, as on the server', () => {
  const sameCity = [match({ matchType: 'NameSimilar', sameCity: true, score: 0.9 })];
  const elsewhere = [match({ matchType: 'NameSimilar', sameCity: false, score: 0.9 })];
  assert.equal(canSaveDespite(sameCity, false), false);
  assert.equal(canSaveDespite(elsewhere, false), true, 'shown for information, does not stop the save');
});

test('nothing found means nothing to answer', () => {
  assert.equal(canSaveDespite([], false), true);
});

test('each match says why it was listed', () => {
  assert.equal(matchReason({ matchType: 'Cnic' }), 'Same CNIC');
  assert.equal(matchReason({ matchType: 'Ntn' }), 'Same NTN');
  assert.equal(matchReason({ matchType: 'Mobile' }), 'Same mobile number');
  assert.equal(matchReason({ matchType: 'NameAndCity' }), 'Same name in the same city');
  assert.equal(matchReason({ matchType: 'NameSimilar', score: 0.923 }), 'Similar name (92%)');
});

// ── History wording ─────────────────────────────────────────────────────────────────

const change = (over) => ({ entity: 'BusinessPartner', action: 'Updated', field: 'Notes', oldValue: 'a', newValue: 'b', reason: null, restricted: false, ...over });

test('a history row reads as one line', () => {
  assert.equal(describeChange(change({})), 'Notes: a → b');
  assert.equal(describeChange(change({ field: 'PrimaryMobile', oldValue: null, newValue: '0300-1234567' })), 'Primary mobile: (empty) → 0300-1234567');
  assert.equal(describeChange(change({ entity: 'BpContact', field: 'ContactName', oldValue: 'Old', newValue: 'New' })), 'Contact contact name: Old → New');
  assert.equal(describeChange(change({ entity: 'BpDriverDetail', field: 'MonthlyRate', restricted: true, oldValue: null, newValue: null })), 'Driver details monthly rate changed (values hidden)');
  assert.equal(describeChange(change({ action: 'Created', field: null, entity: 'BpContact', newValue: '{"ContactName":"Ali Khan","Mobile":"0300-1"}' })), 'Added contact Ali Khan');
  assert.equal(describeChange(change({ action: 'Deleted', field: null, entity: 'BpBankAccount', oldValue: '{"AccountTitle":"Ali Traders"}' })), 'Removed bank account Ali Traders');
  assert.equal(describeChange(change({ action: 'DuplicateOverridden', field: null, newValue: 'BP-26-00003 Ali Traders (Mobile)' })), 'Saved beside possible duplicate BP-26-00003 Ali Traders (Mobile)');
  assert.equal(describeChange(change({ action: 'Created', field: 'OpeningBalance', newValue: '2500', restricted: true })), 'Set opening balance (value hidden)');
});

test('records and fields are named for people', () => {
  assert.equal(entityLabel('BpBankAccount'), 'Bank account');
  assert.equal(entityLabel('Something'), 'Something');
  assert.equal(fieldLabel('Iban'), 'IBAN');
  assert.equal(fieldLabel('BpCode'), 'BP code');
  assert.equal(fieldLabel('LicenceExpiryDate'), 'Licence expiry date');
  assert.equal(snapshotTitle('{"RoleCode":"RunningCustomer"}'), 'Running customer');
  assert.equal(snapshotTitle('not json'), '');
  assert.equal(snapshotTitle(null), '');
});

// ── The screen and the API agree on the vocabulary ──────────────────────────────────

const constants = readFileSync(new URL('../../src/VMS.Modules.BusinessPartners/Domain/PartnerConstants.cs', import.meta.url), 'utf8');

/** The values of a static list in PartnerConstants.cs: `public static readonly IReadOnlyList<string> Name = ["a", "b"]`. */
const listOf = (name, className) => {
  const from = className ? constants.indexOf(`class ${className}`) : 0;
  const m = new RegExp(`${name}\\s*=\\s*\\[([^\\]]*)\\]`).exec(constants.slice(from));
  assert.ok(m, `${name} not found in PartnerConstants.cs`);
  return [...m[1].matchAll(/"([^"]+)"/g)].map((x) => x[1]);
};

/** Constants inside a class: `public const string Driver = "Driver";` within `class PartnerRoles`. */
const constsOf = (className) => {
  const start = constants.indexOf(`class ${className}`);
  const body = constants.slice(start, constants.indexOf('\n}', start));
  return [...body.matchAll(/public const string \w+ = "([^"]+)"/g)].map((x) => x[1]);
};

test('the roles are the API\'s roles, in the same order as its list', () => {
  assert.deepEqual(ROLES.map((r) => r.code).sort(), constsOf('PartnerRoles').sort());
});

test('the choices in the role panels are the API\'s', () => {
  assert.deepEqual([...LICENCE_TYPES], listOf('LicenceTypes'));
  assert.deepEqual([...EMPLOYMENT_TYPES].slice(1), listOf('EmploymentTypes'), 'the API lists Employee by its constant, then the rest');
  assert.equal(EMPLOYMENT_TYPES[0], 'Employee');
  assert.deepEqual([...SUPPLY_CATEGORIES], listOf('SupplyCategories'));
  assert.deepEqual([...CUSTOMER_TYPES], listOf('CustomerTypes'));
  assert.deepEqual([...BILLING_CYCLES], listOf('BillingCycles'));
  assert.deepEqual([...RATE_BASES], listOf('RateBases'));
  assert.deepEqual([...ADDRESS_TYPES], listOf('All', 'AddressTypes'));
  assert.deepEqual([...PARTY_TYPES].sort(), constsOf('PartyTypes').sort());
  assert.deepEqual([...FILER_STATUSES].sort(), constsOf('FilerStatuses').sort());
  assert.deepEqual([...COMMISSION_BASES].sort(), ['Fixed', 'None', 'Percent']);
});
