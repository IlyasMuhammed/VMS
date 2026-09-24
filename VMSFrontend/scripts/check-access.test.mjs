// Tests for src/app/core/access.ts: who may open what, what the menu shows, where sign-in sends you back to.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildNav, canAccess, safeReturnUrl } from '../src/app/core/access.ts';

const user = (permissions = [], isSuperAdmin = false) => ({ isSuperAdmin, permissions });
const staff = user();
const viewer = user(['USER_VIEW', 'ROLE_VIEW']);
const admin = user(['USER_VIEW', 'USER_MANAGE', 'ROLE_VIEW', 'ROLE_MANAGE', 'ADM_MASTER_MANAGE']);
const superAdmin = user([], true);

// ── canAccess ───────────────────────────────────────────────────────────────────

test('nobody signed in may open anything, even a page with no requirements', () => {
  assert.equal(canAccess(undefined, null), false);
  assert.equal(canAccess({ permission: 'USER_VIEW' }, null), false);
  assert.equal(canAccess({}, undefined), false);
});

test('a page that asks for nothing is open to any signed-in user', () => {
  assert.equal(canAccess(undefined, staff), true);
  assert.equal(canAccess({}, staff), true);
});

test('a permission is needed exactly, and only that one', () => {
  assert.equal(canAccess({ permission: 'USER_VIEW' }, viewer), true);
  assert.equal(canAccess({ permission: 'USER_MANAGE' }, viewer), false);
  assert.equal(canAccess({ permission: 'USER_VIEW' }, staff), false);
  assert.equal(canAccess({ permission: 'USER' }, viewer), false, 'a prefix is not a match');
});

test('any of several permissions will do, none will not', () => {
  assert.equal(canAccess({ anyPermission: ['VEH_EDIT', 'USER_VIEW'] }, viewer), true);
  assert.equal(canAccess({ anyPermission: ['VEH_EDIT', 'VEH_CREATE'] }, viewer), false);
  assert.equal(canAccess({ anyPermission: [] }, staff), true, 'an empty list asks for nothing');
});

test('the Super Admin passes every permission but is the only one who passes superAdmin', () => {
  assert.equal(canAccess({ permission: 'ANYTHING' }, superAdmin), true);
  assert.equal(canAccess({ anyPermission: ['A', 'B'] }, superAdmin), true);
  assert.equal(canAccess({ superAdmin: true }, superAdmin), true);
  assert.equal(canAccess({ superAdmin: true }, admin), false, 'holding every permission is not being a Super Admin');
  assert.equal(canAccess({ superAdmin: true }, user(['SUPER_ADMIN'])), false, 'no permission grants it');
});

test('all the requirements must hold together', () => {
  assert.equal(canAccess({ permission: 'USER_VIEW', superAdmin: true }, viewer), false);
  assert.equal(canAccess({ permission: 'USER_VIEW', superAdmin: true }, superAdmin), true);
  assert.equal(canAccess({ permission: 'USER_VIEW', anyPermission: ['ROLE_VIEW'] }, viewer), true);
  assert.equal(canAccess({ permission: 'USER_VIEW', anyPermission: ['ROLE_MANAGE'] }, viewer), false);
});

// ── buildNav ────────────────────────────────────────────────────────────────────

const routes = [
  { path: 'auth', children: [{ path: 'login' }] }, // no nav: never in the menu
  {
    path: '',
    children: [
      { path: '', data: { nav: { label: 'Dashboard', icon: 'pi-home' } } },
      { path: 'profile' },
      { path: 'users', data: { access: { permission: 'USER_VIEW' }, nav: { label: 'Users', icon: 'pi-users', section: 'Administration', description: 'People' } } },
      { path: 'users/:id', data: { access: { permission: 'USER_VIEW' } } },
      { path: 'roles', data: { access: { permission: 'ROLE_VIEW' }, nav: { label: 'Roles', icon: 'pi-shield', section: 'Administration' } } },
      { path: 'admin/master-data', data: { access: { permission: 'ADM_MASTER_MANAGE' }, nav: { label: 'Master data', icon: 'pi-list', section: 'Administration' } } },
      { path: 'tenants', data: { access: { superAdmin: true }, nav: { label: 'Tenants', icon: 'pi-building', section: 'Administration' } } },
      { path: '**' },
    ],
  },
];
const labels = (groups) => groups.flatMap((g) => g.items.map((i) => i.label));

test('the menu shows only what the user may open', () => {
  assert.deepEqual(labels(buildNav(routes, staff)), ['Dashboard']);
  assert.deepEqual(labels(buildNav(routes, viewer)), ['Dashboard', 'Users', 'Roles']);
  assert.deepEqual(labels(buildNav(routes, admin)), ['Dashboard', 'Users', 'Roles', 'Master data']);
  assert.deepEqual(labels(buildNav(routes, superAdmin)), ['Dashboard', 'Users', 'Roles', 'Master data', 'Tenants']);
});

test('nobody signed in gets no menu', () => {
  assert.deepEqual(buildNav(routes, null), []);
});

test('items are grouped under their section, in the order the routes are declared', () => {
  const groups = buildNav(routes, superAdmin);
  assert.deepEqual(groups.map((g) => g.section), ['', 'Administration']);
  assert.deepEqual(groups[0].items.map((i) => i.label), ['Dashboard']);
});

test('a route is addressed by its full path, and only the home page is matched exactly', () => {
  const items = buildNav(routes, superAdmin).flatMap((g) => g.items);
  const byLabel = Object.fromEntries(items.map((i) => [i.label, i]));
  assert.equal(byLabel['Dashboard'].route, '/');
  assert.equal(byLabel['Dashboard'].exact, true);
  assert.equal(byLabel['Master data'].route, '/admin/master-data');
  assert.equal(byLabel['Master data'].exact, false);
});

test('a description travels with the entry, for the dashboard tile', () => {
  const users = buildNav(routes, viewer).flatMap((g) => g.items).find((i) => i.label === 'Users');
  assert.equal(users.description, 'People');
  assert.equal(users.icon, 'pi-users');
});

test('routes without a menu entry, parameters and wildcards never appear', () => {
  const all = buildNav(routes, superAdmin).flatMap((g) => g.items.map((i) => i.route));
  assert.ok(!all.some((r) => r.includes(':') || r.includes('*') || r.startsWith('/auth')));
  assert.ok(!all.includes('/profile'));
});

test('a parent the user may not open hides everything beneath it', () => {
  const nested = [{ path: 'fleet', data: { access: { permission: 'VEH_VIEW' } }, children: [{ path: 'trucks', data: { nav: { label: 'Trucks', icon: 'pi-truck' } } }] }];
  assert.deepEqual(labels(buildNav(nested, staff)), []);
  assert.deepEqual(labels(buildNav(nested, user(['VEH_VIEW']))), ['Trucks']);
  assert.equal(buildNav(nested, user(['VEH_VIEW']))[0].items[0].route, '/fleet/trucks');
});

test('an unaccessible route and its menu entry are decided by the same declaration', () => {
  // The invariant the whole design exists for: for every route, "in the menu" implies "the guard lets it through".
  for (const u of [staff, viewer, admin, superAdmin]) {
    const shown = new Set(buildNav(routes, u).flatMap((g) => g.items.map((i) => i.label)));
    for (const child of routes[1].children.filter((c) => c.data?.nav)) {
      assert.equal(shown.has(child.data.nav.label), canAccess(child.data.access, u), `${child.data.nav.label} for ${JSON.stringify(u)}`);
    }
  }
});

// ── safeReturnUrl ───────────────────────────────────────────────────────────────

test('the page they were on their way to is where they land', () => {
  assert.equal(safeReturnUrl('/users'), '/users');
  assert.equal(safeReturnUrl('/roles/12'), '/roles/12');
  assert.equal(safeReturnUrl('/vehicles?status=Active&page=2'), '/vehicles?status=Active&page=2');
  assert.equal(safeReturnUrl('/documents#expiring'), '/documents#expiring');
});

test('nothing, or nothing sensible, goes home', () => {
  for (const nothing of [null, undefined, '', '/']) assert.equal(safeReturnUrl(nothing), '/');
});

test('the sign-in page cannot be used to send people to another site', () => {
  for (const bad of [
    'https://evil.example/phish',
    'http://evil.example',
    '//evil.example/phish',
    '/\\evil.example',
    '\\\\evil.example',
    'javascript:alert(1)',
    'evil.example',
    'users',
    '/users\\..\\..\\x',
    '/users\r\nSet-Cookie: x=1',
    '/users ',
  ]) {
    assert.equal(safeReturnUrl(bad), '/', JSON.stringify(bad));
  }
});

test('coming back to the sign-in pages, or to an absurdly long address, goes home', () => {
  assert.equal(safeReturnUrl('/auth/login'), '/');
  assert.equal(safeReturnUrl('/auth/login?returnUrl=/users'), '/');
  assert.equal(safeReturnUrl('/auth'), '/');
  assert.equal(safeReturnUrl('/' + 'a'.repeat(600)), '/');
  assert.equal(safeReturnUrl('/authors'), '/authors', 'only /auth itself is refused, not anything that begins with it');
});
