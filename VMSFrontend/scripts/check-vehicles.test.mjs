// Tests for the framework-free rules of the vehicle screens, and that the screen's vocabulary is the API's.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
  ACQUISITION_TYPES, AGREEMENT_GRACE_DAYS, ARRANGEMENT_TYPES, CAPACITY_TYPE_CODES, CAPACITY_UNITS, CATEGORIES, CATEGORY_RULES, CHARGE_AMOUNT_BASES, CHARGE_FREQUENCIES, CHARGE_POSTING_MODES,
  DISPOSAL_KINDS, EXPENSE_RULES, FUEL_TYPES, ITEM_CONDITIONS,
  FINANCE_FREQUENCIES, FULLY_PAID_CODE, MAX_TENURE, PAYMENT_MODES, RENT_FREQUENCIES, SETTABLE_STATUSES, SHARING_BASES, VEHICLE_STATUSES, WIZARD_STEPS, addDays, agreementDateLimit,
  canDispose, describeChange, describeLifecycle, disposalKindsFor, downPaymentMatches, entityLabel, fieldLabel, isDisposed, isDraft, isInFleet, mayEnterFinance,
  itemRequests, needsCapacity, parseDraftData, reconciliation, registrationKey, serializeDraftData, showsFinanceBlock, statusMoves, stepReachable, suggestedFinanceAmount, totalPayable, visibleCategoryFields, words,
} from '../src/app/pages/vehicles/vehicle-logic.ts';

test('a registration number compares without spaces or dashes, in capitals', () => {
  assert.equal(registrationKey('LES-1234'), 'LES1234');
  assert.equal(registrationKey('les 1234'), 'LES1234');
  assert.equal(registrationKey(' Khi - 4455 '), 'KHI4455');
  assert.equal(registrationKey(null), '');
});

test('only a truck, trailer, tanker or prime mover needs a load capacity', () => {
  for (const code of ['TRUCK', 'TRAILER', 'TANKER', 'PRIME_MOVER']) assert.equal(needsCapacity(code), true, code);
  for (const code of ['PICKUP', 'BUS', 'CAR', 'BIKE', 'OTHER', '', null, undefined]) assert.equal(needsCapacity(code), false, String(code));
});

test('values read as words, abbreviations stay', () => {
  assert.equal(words('UnderMaintenance'), 'Under maintenance');
  assert.equal(words('TemporarilyUnavailable'), 'Temporarily unavailable');
  assert.equal(words('SelfOwned'), 'Self owned');
  assert.equal(words('CNG'), 'CNG');
  assert.equal(words(null), '');
});

test('where a vehicle can be moved from each status', () => {
  assert.deepEqual(statusMoves('Active'), ['UnderMaintenance', 'TemporarilyUnavailable']);
  assert.deepEqual(statusMoves('UnderMaintenance'), ['Active', 'TemporarilyUnavailable']);
  assert.deepEqual(statusMoves('Assigned'), ['Active', 'UnderMaintenance', 'TemporarilyUnavailable']);
  assert.deepEqual(statusMoves('Retired'), ['Active'], 'a retired vehicle can be reinstated');
  for (const s of ['Draft', 'Sold', 'Transferred']) assert.deepEqual(statusMoves(s), [], s);
});

test('who can be retired, sold or transferred', () => {
  for (const s of ['Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable', 'Retired']) assert.equal(canDispose(s), true, s);
  for (const s of ['Draft', 'Sold', 'Transferred']) assert.equal(canDispose(s), false, s);
  assert.deepEqual(disposalKindsFor('Active'), ['Retire', 'Sell', 'Transfer']);
  assert.deepEqual(disposalKindsFor('Retired'), ['Sell', 'Transfer'], 'not retired twice');
  assert.deepEqual(disposalKindsFor('Sold'), []);
});

test('status groups', () => {
  assert.equal(isDraft('Draft'), true);
  assert.equal(isInFleet('Draft'), false);
  assert.equal(isInFleet('Assigned'), true);
  assert.equal(isDisposed('Sold'), true);
  assert.equal(isDisposed('Retired'), false, 'retired is still owned');
});

test('each category names its counterparty and its fields', () => {
  assert.equal(CATEGORY_RULES.Rented.counterpartyLabel, 'Lessor');
  assert.deepEqual(CATEGORY_RULES.BankLeased.roles, ['Bank']);
  assert.deepEqual(CATEGORY_RULES.CustomerArrangement.roles, ['Customer', 'RunningCustomer']);
  assert.deepEqual(CATEGORY_RULES.Shared.roles, [], 'any partner');
  assert.deepEqual(CATEGORY_RULES.SelfOwned.fields, []);
});

test('conditional fields show for the choices made', () => {
  assert.equal(visibleCategoryFields('Shared', { sharingBasis: 'ProfitShare' }).includes('fixedMonthlyAmount'), false);
  assert.equal(visibleCategoryFields('Shared', { sharingBasis: 'FixedMonthly' }).includes('fixedMonthlyAmount'), true);
  assert.equal(visibleCategoryFields('Rented', { rentFrequency: 'Weekly' }).includes('rentDueDay'), false);
  assert.equal(visibleCategoryFields('Rented', { rentFrequency: 'Monthly' }).includes('rentDueDay'), true);
  assert.equal(visibleCategoryFields('CustomerArrangement', { arrangementType: 'DedicatedMonthly' }).includes('agreedAmount'), true);
  assert.equal(visibleCategoryFields('CustomerArrangement', { arrangementType: 'RevenueShare' }).includes('revenueSharePercent'), true);
  assert.equal(visibleCategoryFields('CustomerArrangement', { arrangementType: 'PerTrip' }).some((f) => f === 'agreedAmount' || f === 'revenueSharePercent'), false);
  assert.deepEqual(visibleCategoryFields('Nonsense', {}), []);
});

test('the wizard has five steps and later ones open only once step one is valid', () => {
  assert.deepEqual(WIZARD_STEPS.map((s) => s.key), ['details', 'ownership', 'acquisition', 'items', 'review']);
  assert.equal(stepReachable(0, false), true);
  for (const i of [1, 2, 3, 4]) {
    assert.equal(stepReachable(i, false), false, `step ${i + 1} before step 1 is valid`);
    assert.equal(stepReachable(i, true), true);
  }
  assert.equal(WIZARD_STEPS[0].pending, undefined, 'step 1 is built');
  assert.equal(WIZARD_STEPS[1].pending, undefined, 'step 2 is built');
  assert.equal(WIZARD_STEPS[2].pending, undefined, 'step 3 is built');
  assert.equal(WIZARD_STEPS[3].pending, undefined, 'step 4 is built');
  assert.equal(WIZARD_STEPS[4].pending, undefined, 'step 5 is built');
  assert.ok(WIZARD_STEPS.every((s) => !s.pending), 'every step is built');
});

test('total payable is installment times tenure, to the paisa', () => {
  assert.equal(totalPayable(150000, 24), 3600000);
  assert.equal(totalPayable(0.1, 3), 0.3, 'no floating point drift');
  assert.equal(totalPayable(null, 24), 0);
  assert.equal(totalPayable(150000, null), 0);
  assert.equal(totalPayable(12345.67, 12), 148148.04);
});

test('finance plus down payment is compared with the price only when there is a price', () => {
  assert.equal(reconciliation(5000000, 3000000, 2000000), null);
  assert.deepEqual(reconciliation(5000000, 2800000, 2000000), { sum: 4800000, price: 5000000 });
  assert.equal(reconciliation(null, 1, 1), null, 'nothing to compare with');
  assert.equal(reconciliation(0.3, 0.1, 0.2), null, 'to the paisa, not to the float');
});

test('the down payment must be what was paid, and nothing paid is zero', () => {
  assert.equal(downPaymentMatches(2000000, 2000000), true);
  assert.equal(downPaymentMatches(1500000, 2000000), false);
  assert.equal(downPaymentMatches(0, null), true);
  assert.equal(downPaymentMatches(null, 0), true);
  assert.equal(downPaymentMatches(1, null), false);
});

test('the balance of the price is suggested as the finance amount', () => {
  assert.equal(suggestedFinanceAmount(5000000, 2000000), 3000000);
  assert.equal(suggestedFinanceAmount(5000000, null), 5000000);
  assert.equal(suggestedFinanceAmount(5000000, 5000000), null, 'nothing left to finance');
  assert.equal(suggestedFinanceAmount(null, 100), null);
});

test('the agreement may be dated 90 days after the acquisition at the latest', () => {
  assert.equal(AGREEMENT_GRACE_DAYS, 90);
  assert.equal(addDays('2026-01-01', 90), '2026-04-01');
  assert.equal(addDays('2026-12-31', 1), '2027-01-01', 'across a year');
  assert.equal(addDays('2028-02-28', 1), '2028-02-29', 'a leap year');
  assert.equal(agreementDateLimit('2026-09-21'), '2026-12-20');
  assert.equal(agreementDateLimit(null), null);
});

test('the bank block shows for a lease, a saved agreement, or when asked for', () => {
  assert.equal(showsFinanceBlock('Lease', false, false), true);
  assert.equal(showsFinanceBlock('Purchase', false, false), false);
  assert.equal(showsFinanceBlock('Purchase', true, false), true);
  assert.equal(showsFinanceBlock('Purchase', false, true), true);
  assert.equal(showsFinanceBlock(null, false, false), false);
});

test('step 3 needs acquisition, cost and finance permissions together', () => {
  const only = (...granted) => (p) => granted.includes(p);
  assert.equal(mayEnterFinance(only('VEH.ACQUISITION.EDIT', 'VEH.FIELD.COST.VIEW', 'VEH.FIELD.FINANCE.VIEW')), true);
  assert.equal(mayEnterFinance(only('VEH.ACQUISITION.EDIT', 'VEH.FIELD.COST.VIEW')), false);
  assert.equal(mayEnterFinance(only('VEH.FIELD.COST.VIEW', 'VEH.FIELD.FINANCE.VIEW')), false);
  assert.equal(MAX_TENURE, 120);
  assert.equal(FULLY_PAID_CODE, 'FULLY_PAID');
});

test('a draft keeps its ownership answers as JSON, and only when there is something to keep', () => {
  assert.equal(serializeDraftData(null), null);
  assert.equal(serializeDraftData({ category: null, details: {} }), null);
  assert.equal(serializeDraftData({ category: null, details: { counterpartyId: null, agreementReference: '' } }), null, 'empty answers leave nothing behind');
  const json = serializeDraftData({ category: 'Rented', details: { counterpartyId: 7, rentAmount: 80000, rentFrequency: 'Monthly', rentDueDay: 5, endDate: null, agreementReference: '' } });
  assert.deepEqual(parseDraftData(json), { ownership: { category: 'Rented', details: { counterpartyId: 7, rentAmount: 80000, rentFrequency: 'Monthly', rentDueDay: 5 } }, items: [] });
  assert.equal(serializeDraftData({ category: 'SelfOwned', details: {} }) !== null, true, 'a category alone is worth keeping');
});

test('anything that is not what we wrote is read as no ownership', () => {
  for (const bad of [null, undefined, '', 'not json', '[]', '{}', '{"ownership":null}', '{"ownership":"x"}', '{"other":1}']) assert.deepEqual(parseDraftData(bad), { ownership: null, items: [] }, String(bad));
  assert.deepEqual(parseDraftData('{"ownership":{"category":5,"details":[1]}}'), { ownership: { category: null, details: {} }, items: [] }, 'wrong types are dropped, not trusted');
});

test('items are kept with the draft and sent without the names used to draw them', () => {
  const item = { itemTypeId: 4, itemTypeName: 'Container', description: ' 40 ft container ', serialNo: ' msku-1 ', supplierId: 9, supplierName: 'Al Noor', installationDate: '', cost: 450000, warrantyUntil: null, condition: '' };
  const json = serializeDraftData(null, [item]);
  assert.notEqual(json, null, 'items alone are worth keeping');
  assert.deepEqual(parseDraftData(json), { ownership: null, items: [item] });
  assert.deepEqual(itemRequests([item]), [{ itemTypeId: 4, description: '40 ft container', serialNo: 'msku-1', supplierId: 9, installationDate: null, cost: 450000, warrantyUntil: null, condition: null }]);
  assert.equal(serializeDraftData(null, []), null);
  assert.deepEqual(parseDraftData('{"items":[1,null,{"description":5},{"description":"ok","itemTypeId":1}]}').items, [{ description: 'ok', itemTypeId: 1 }], 'only well-formed items are read');
});

const change = (over) => ({ entity: 'Vehicle', action: 'Updated', field: 'Colour', oldValue: null, newValue: 'White', restricted: false, ...over });

test('a history row reads as one line', () => {
  assert.equal(describeChange(change({})), 'Colour: (empty) → White');
  assert.equal(describeChange(change({ field: 'RegistrationNo', oldValue: 'LES-1', newValue: 'LES-2' })), 'Registration number: LES-1 → LES-2');
  assert.equal(describeChange(change({ entity: 'VehicleAttachedItem', action: 'Created', field: null, newValue: '{"Description":"40 ft container"}' })), 'Added attached item 40 ft container');
  assert.equal(describeChange(change({ entity: 'VehicleAttachedItem', action: 'Updated', field: 'Status', oldValue: 'Attached', newValue: 'Detached' })), 'Attached item status: Attached → Detached');
  assert.equal(describeChange(change({ entity: 'VehicleAttachedItem', field: 'Cost', restricted: true, oldValue: null, newValue: null })), 'Attached item cost changed (values hidden)');
  assert.equal(describeChange(change({ action: 'FuelCardReassigned', field: 'FuelCardNumber', oldValue: 'PSO-1', newValue: null })), 'Fuel card PSO-1 taken by another vehicle');
});

test('a lifecycle row reads as a move', () => {
  assert.equal(describeLifecycle({ eventType: 'Created' }), 'Vehicle created as a draft');
  assert.equal(describeLifecycle({ eventType: 'StatusChange', fromStatus: 'Active', toStatus: 'UnderMaintenance' }), 'Active → Under maintenance');
  assert.equal(describeLifecycle({ eventType: 'CategoryChange', fromCategory: 'SelfOwned', toCategory: 'Rented', counterparty: { name: 'Al Noor' } }), 'Self owned → Rented (with Al Noor)');
  assert.equal(describeLifecycle({ eventType: 'Disposal', toStatus: 'Sold', counterparty: { name: 'Ali Traders' } }), 'Sold to Ali Traders');
  assert.equal(describeLifecycle({ eventType: 'Disposal', toStatus: 'Retired' }), 'Retired');
});

test('records and fields are named for people', () => {
  assert.equal(entityLabel('VehicleRelation'), 'Ownership');
  assert.equal(entityLabel('Something'), 'Something');
  assert.equal(fieldLabel('Gvw'), 'GVW');
  assert.equal(fieldLabel('TyreCount'), 'Tyre count');
});

// ── The screen and the API agree on the vocabulary ──────────────────────────────────

const constants = readFileSync(new URL('../../src/VMS.Modules.Vehicles/Domain/VehicleConstants.cs', import.meta.url), 'utf8');

const classBody = (name) => {
  const start = constants.indexOf(`class ${name}`);
  assert.ok(start >= 0, `${name} not found`);
  return constants.slice(start, constants.indexOf('\n}', start));
};
/** The quoted values of `Name = [ "a", "b" ]` inside a class. */
const listIn = (cls, name) => {
  const m = new RegExp(`${name}\\s*=\\s*\\[([^\\]]*)\\]`).exec(classBody(cls));
  assert.ok(m, `${cls}.${name} not found`);
  return [...m[1].matchAll(/"([^"]+)"/g)].map((x) => x[1]);
};
const constsIn = (cls) => [...classBody(cls).matchAll(/public const string \w+ = "([^"]+)"/g)].map((x) => x[1]);

test('the statuses, categories and choices are the API\'s', () => {
  assert.deepEqual([...VEHICLE_STATUSES].sort(), constsIn('VehicleStatuses').sort());
  assert.deepEqual([...CATEGORIES].sort(), constsIn('OwnershipCategories').sort());
  assert.deepEqual([...FUEL_TYPES], listIn('FuelTypes', 'All'));
  assert.deepEqual([...CAPACITY_UNITS], listIn('CapacityUnits', 'All'));
  assert.deepEqual([...ACQUISITION_TYPES], listIn('AcquisitionTypes', 'All'));
  assert.deepEqual([...PAYMENT_MODES], listIn('PaymentModes', 'All'));
  assert.deepEqual([...FINANCE_FREQUENCIES], constsIn('FinanceFrequencies'));
  assert.deepEqual([...SHARING_BASES], listIn('ArrangementValues', 'SharingBases'));
  assert.deepEqual([...EXPENSE_RULES], listIn('ArrangementValues', 'ExpenseSharingRules'));
  assert.deepEqual([...RENT_FREQUENCIES], listIn('ArrangementValues', 'RentFrequencies'));
  assert.deepEqual([...ARRANGEMENT_TYPES], listIn('ArrangementValues', 'ArrangementTypes'));
  assert.deepEqual([...ITEM_CONDITIONS], listIn('ItemConditions', 'All'));
  assert.deepEqual([...DISPOSAL_KINDS], constsIn('DisposalKinds'));
  assert.deepEqual([...CAPACITY_TYPE_CODES], listIn('CapacityVehicleTypes', 'Codes'));
  assert.deepEqual([...SETTABLE_STATUSES], ['Active', 'UnderMaintenance', 'TemporarilyUnavailable']);
  assert.deepEqual([...CHARGE_AMOUNT_BASES], constsIn('ChargeAmountBases'));
  assert.deepEqual([...CHARGE_FREQUENCIES], constsIn('ChargeFrequencies'));
  assert.deepEqual([...CHARGE_POSTING_MODES], constsIn('ChargePostingModes'));
});
// A URL written without the `${api}` prefix would be sent to the page's own server and fail only at run time (it happened once, to the export).
test('every call in the API services goes through the api prefix', () => {
  const source = readFileSync(new URL('../src/app/core/api.services.ts', import.meta.url), 'utf8');
  const calls = [...source.matchAll(/this\.http\.(?:get|post|put|patch|delete)(?:<[^(]*>)?\(\s*(`[^`]*`|'[^']*')/g)].map((m) => m[1]);
  assert.ok(calls.length > 40, `found only ${calls.length} calls`);
  const bare = calls.filter((c) => !c.startsWith('`${api}'));
  assert.deepEqual(bare, [], 'these calls do not start with ${api}');
});
