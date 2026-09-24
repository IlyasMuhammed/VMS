// Tests for the framework-free logic behind the shared components: list queries, file rules, messages, form errors.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { classify } from '../src/app/core/form-errors.ts';
import { DEFAULT_MESSAGES, apiErrorsOf, formatMessage, placeholders } from '../src/app/core/message-format.ts';
import { acceptAttribute, checkFile, describeKinds, formatLimit, formatSize } from '../src/app/core/file-rules.ts';
import { DEFAULT_PAGE_SIZE, MIN_SEARCH_LENGTH, hasValue, isFiltering, noFilters, searchToApply, toQueryParams } from '../src/app/shared/list-query.ts';

// ── List queries ────────────────────────────────────────────────────────────────

test('a list pages 25 at a time and searches from three characters, as the FSD says', () => {
  assert.equal(DEFAULT_PAGE_SIZE, 25);
  assert.equal(MIN_SEARCH_LENGTH, 3);
});

test('a search is held back until it is long enough, but clearing it is sent at once', () => {
  assert.equal(searchToApply(''), '');
  assert.equal(searchToApply('   '), '');
  assert.equal(searchToApply('ab'), null);
  assert.equal(searchToApply(' ab '), null, 'spaces do not count toward the length');
  assert.equal(searchToApply('abc'), 'abc');
  assert.equal(searchToApply('  Hino 500  '), 'Hino 500');
  assert.equal(searchToApply('ab', 2), 'ab', 'a screen may ask for fewer');
});

test('what counts as a filter being set', () => {
  for (const set of ['active', 0, 12, true, ['a'], { any: 1 }]) assert.equal(hasValue(set), true, JSON.stringify(set));
  for (const unset of [null, undefined, '', false, []]) assert.equal(hasValue(unset), false, JSON.stringify(unset));
});

test('a list is "filtered" only when something is narrowing it', () => {
  assert.equal(isFiltering(noFilters()), false);
  assert.equal(isFiltering({ search: '  ', filters: {} }), false);
  assert.equal(isFiltering({ search: 'hino', filters: {} }), true);
  assert.equal(isFiltering({ search: '', filters: { status: null, roles: [], expiring: false } }), false);
  assert.equal(isFiltering({ search: '', filters: { status: 'active' } }), true);
  assert.equal(isFiltering({ search: '', filters: { roles: ['DRIVER'] } }), true);
});

test('the query becomes URL parameters: paging, search, sort, filters (several values repeated)', () => {
  const params = toQueryParams({
    page: 3,
    pageSize: 25,
    search: '  hino ',
    sort: { field: 'modifiedOn', order: -1 },
    filters: { status: 'Active', roles: ['DRIVER', 'VENDOR'], city: null, expiring: false, branch: 0 },
  });
  assert.deepEqual(params, { page: '3', pageSize: '25', search: 'hino', sort: 'modifiedOn,desc', status: 'Active', roles: ['DRIVER', 'VENDOR'], branch: '0' });
});

test('an unfiltered, unsorted query is just paging', () => {
  assert.deepEqual(toQueryParams({ page: 1, pageSize: 25, search: '', filters: {} }), { page: '1', pageSize: '25' });
  assert.equal(toQueryParams({ page: 1, pageSize: 25, search: '', filters: {}, sort: { field: 'name', order: 1 } })['sort'], 'name,asc');
});

// ── File rules ──────────────────────────────────────────────────────────────────

const MB = 1024 * 1024;

test('kinds read as a sentence, the same way the server says it', () => {
  assert.equal(describeKinds(['pdf', 'png', 'jpeg']), 'PDF, PNG or JPEG');
  assert.equal(describeKinds(['pdf', 'png']), 'PDF or PNG');
  assert.equal(describeKinds(['pdf']), 'PDF');
  assert.equal(describeKinds(['pdf', 'docx', 'xlsx']), 'PDF, Word (.docx) or Excel (.xlsx)');
  assert.equal(describeKinds([]), 'no file type');
});

test('sizes read the way the server states its limit', () => {
  assert.equal(formatLimit(10 * MB), '10 MB');
  assert.equal(formatLimit(1.5 * MB), '1.5 MB');
  assert.equal(formatLimit(100 * 1024), '100 KB');
  assert.equal(formatLimit(500 * MB), '500 MB');
  assert.equal(formatSize(512), '512 B');
  assert.equal(formatSize(2 * MB), '2 MB');
});

test('the accept attribute lists extensions and MIME types of the allowed kinds', () => {
  const accept = acceptAttribute(['pdf', 'jpeg']);
  assert.match(accept, /\.pdf/);
  assert.match(accept, /\.jpg/);
  assert.match(accept, /\.jpeg/);
  assert.match(accept, /application\/pdf/);
  assert.match(accept, /image\/jpeg/);
  assert.doesNotMatch(accept, /png/);
});

test('a file that is fine passes', () => {
  assert.equal(checkFile({ name: 'Registration Book.pdf', size: 2 * MB, type: 'application/pdf' }, ['pdf', 'png', 'jpeg']), null);
  assert.equal(checkFile({ name: 'scan.JPG', size: 1000, type: '' }, ['jpeg']), null, 'the extension is enough, in any case');
  assert.equal(checkFile({ name: 'noext', size: 1000, type: 'image/png' }, ['png']), null, 'or the type alone');
});

test('a file of the wrong kind is refused with the accepted kinds named', () => {
  assert.equal(checkFile({ name: 'setup.exe', size: 1000, type: 'application/x-msdownload' }, ['pdf', 'png', 'jpeg']), 'Only PDF, PNG or JPEG files are accepted.');
  assert.equal(checkFile({ name: 'notes.txt', size: 1000, type: 'text/plain' }, ['pdf']), 'Only PDF files are accepted.');
  assert.equal(checkFile({ name: 'a.pdf.exe', size: 1000 }, ['pdf']), 'Only PDF files are accepted.', 'the last extension is the one that counts');
});

test('a file that is too large or empty is refused', () => {
  assert.equal(checkFile({ name: 'big.pdf', size: 11 * MB, type: 'application/pdf' }, ['pdf']), 'The file is larger than the 10 MB limit.');
  assert.equal(checkFile({ name: 'big.pdf', size: 200 * 1024, type: 'application/pdf' }, ['pdf'], 100 * 1024), 'The file is larger than the 100 KB limit.');
  assert.equal(checkFile({ name: 'empty.pdf', size: 0, type: 'application/pdf' }, ['pdf']), 'The file is empty.');
  assert.equal(checkFile({ name: 'exactly.pdf', size: 10 * MB, type: 'application/pdf' }, ['pdf']), null, 'exactly the limit is allowed');
});

// ── Messages ────────────────────────────────────────────────────────────────────

test('placeholders are found by name, once each', () => {
  assert.deepEqual(placeholders('{a} and {b} and {a}'), ['a', 'b']);
  assert.deepEqual(placeholders('no placeholders, {not one} or {1st}'), []);
});

test('a message is filled in from its values', () => {
  assert.equal(
    formatMessage('This CNIC belongs to {BPCode} — {LegalName}.', { BPCode: 'BP-26-00147', LegalName: 'Ali Traders' }),
    'This CNIC belongs to BP-26-00147 — Ali Traders.',
  );
  assert.equal(formatMessage('{n} of {n}', { n: 3 }), '3 of 3');
  assert.equal(formatMessage('Count: {n}', { n: 0 }), 'Count: 0', 'zero is a value');
});

test('a placeholder with no value is left as written, and a null value is empty; the screen never fails to render an error', () => {
  assert.equal(formatMessage('Hello {Name}', {}), 'Hello {Name}');
  assert.equal(formatMessage('Hello {Name}', undefined), 'Hello {Name}');
  assert.equal(formatMessage('Hello ({Name})', { Name: null }), 'Hello ()');
});

test('the API error list is read from an error body, ignoring anything malformed', () => {
  const body = { success: false, message: 'x', errors: [{ field: 'cnic', code: 'VAL-BP-002', message: 'Enter CNIC.' }, { nonsense: true }, null] };
  assert.deepEqual(apiErrorsOf(body).map((e) => e.code), ['VAL-BP-002']);
  for (const none of [null, undefined, {}, { errors: 'no' }, { errors: null }, 'text']) assert.deepEqual(apiErrorsOf(none), []);
});

test('the client\'s built-in wording is the same as the API\'s file for the checks every form has', () => {
  // The API's messages.en.json is the source; the client keeps a copy only for before it loads. They must not drift.
  const server = JSON.parse(readFileSync(new URL('../../src/VMS.Shared/Messages/messages.en.json', import.meta.url), 'utf8'));
  const generic = Object.fromEntries(Object.entries(server).filter(([id]) => id.startsWith('VAL-GEN-')));
  assert.deepEqual(DEFAULT_MESSAGES, generic);
});

// ── Form errors ─────────────────────────────────────────────────────────────────

test('each failed check maps to one catalogue message with the field named', () => {
  assert.deepEqual(classify({ required: true }, 'Legal name'), { code: 'VAL-GEN-001', params: { Field: 'Legal name' } });
  assert.deepEqual(classify({ email: true }, 'Email'), { code: 'VAL-GEN-002', params: {} });
  assert.deepEqual(classify({ maxlength: { requiredLength: 30, actualLength: 41 } }, 'Code'), { code: 'VAL-GEN-003', params: { Field: 'Code', Max: 30 } });
  assert.deepEqual(classify({ minlength: { requiredLength: 8, actualLength: 3 } }, 'Password'), { code: 'VAL-GEN-004', params: { Field: 'Password', Min: 8 } });
  assert.deepEqual(classify({ min: { min: 1, actual: 0 } }, 'Share'), { code: 'VAL-GEN-006', params: { Field: 'Share', Min: 1 } });
  assert.deepEqual(classify({ max: { max: 100, actual: 101 } }, 'Share'), { code: 'VAL-GEN-007', params: { Field: 'Share', Max: 100 } });
  assert.deepEqual(classify({ pattern: { requiredPattern: '^x$' } }, 'CNIC'), { code: 'VAL-GEN-005', params: { Field: 'CNIC' } });
  assert.deepEqual(classify({ futureDate: true }, 'Acquisition date'), { code: 'VAL-GEN-008', params: { Field: 'Acquisition date' } });
  assert.deepEqual(classify({ mismatch: true }, 'Confirm'), { code: 'VAL-GEN-009', params: {} });
});

test('no errors, no message', () => {
  assert.equal(classify(null, 'X'), null);
  assert.equal(classify(undefined, 'X'), null);
  assert.equal(classify({}, 'X'), null);
});

test('what the server said comes first, then the plainest mistake', () => {
  assert.equal(classify({ required: true, server: 'Already used.' }, 'X').code, 'server');
  assert.equal(classify({ server: 'Already used.' }, 'X').message, 'Already used.');
  assert.equal(classify({ pattern: {}, required: true }, 'X').code, 'VAL-GEN-001');
  assert.equal(classify({ maxlength: { requiredLength: 5 }, pattern: {} }, 'X').code, 'VAL-GEN-003');
});

test('a validator can bring its own wording, either from the screen or in the error itself', () => {
  assert.deepEqual(classify({ weakPassword: true }, 'Password', { weakPassword: 'Use 8 or more characters.' }), { code: 'custom', params: {}, message: 'Use 8 or more characters.' });
  assert.deepEqual(classify({ licenceExpired: { message: 'Licence expiry must be a future date.' } }, 'Licence'), { code: 'custom', params: {}, message: 'Licence expiry must be a future date.' });
  assert.deepEqual(classify({ somethingOdd: true }, 'Field x'), { code: 'VAL-GEN-010', params: { Field: 'Field x' } }, 'an unknown failure still says something');
});