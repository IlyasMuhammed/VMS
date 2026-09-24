# VMS — Vehicle Management System

Multi-tenant user management for VMS: sign-in, users, roles and permissions, and tenants.
This is the foundation the rest of VMS (vehicles, drivers, trips, …) will be built on.

| Part | Path | Stack |
|---|---|---|
| API | `src/VMS.API` | ASP.NET Core 8, JWT bearer auth |
| Shared kernel | `src/VMS.Shared` | tenant context, query filters, permissions, ApiResponse |
| Auth module | `src/VMS.Modules.Auth` | users, roles, permissions, sessions (EF Core, schema `auth`) |
| Tenancy module | `src/VMS.Modules.Tenancy` | tenants, super admins (EF Core, schema `tenancy`) |
| Core module | `src/VMS.Modules.Core` | audit log, numbering series, file storage (EF Core, schema `core`) |
| Business partners module | `src/VMS.Modules.BusinessPartners` | customers, drivers, workshops, banks, vendors: create/edit, roles, status, duplicate control, list, picker, Excel export (EF Core, schema `bp`, endpoints under `api/partners`) |
| Vehicles module | `src/VMS.Modules.Vehicles` | vehicles, ownership relations, lifecycle, attached items, odometer, default driver, list, acquisition and finance blocks, installment payments, recurring charges and the fleet-wide payables workbench, a nightly generation job (EF Core, schema `veh`, endpoints under `api/vehicles`, `api/payables` and `api/admin/jobs`) |
| Tests | `tests/VMS.Tests` | xUnit; boots the real API on a throwaway LocalDB database |
| Web app | `VMSFrontend` | Angular 19 + PrimeNG |
| Database scripts | `database/scripts` | SQL Server, database `VMSGlobal` |

## Run it locally

Prerequisites: .NET 8 SDK, Node 20+, SQL Server LocalDB (`(localdb)\MSSQLLocalDB`), `sqlcmd`.

```powershell
# 1. Database — creates VMSGlobal and both schemas (safe to re-run)
cd database/scripts
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -I -f 65001 -i 000_create_database.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -I -f 65001 -d VMSGlobal -i 001_tenancy_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -I -f 65001 -d VMSGlobal -i 002_auth_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -I -f 65001 -d VMSGlobal -i 003_core_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -I -f 65001 -d VMSGlobal -i 004_bp_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -I -f 65001 -d VMSGlobal -i 005_veh_schema.sql

# 2. API — http://localhost:5000  (Swagger at /swagger)
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/VMS.API --urls http://localhost:5000

# 3. Web app — http://localhost:4200
cd VMSFrontend
npm install
npm start
```

On first start the API seeds the platform tenant (`VMS-PLATFORM`), the permission catalogue (55 codes),
the four platform roles and the first Super Admin. The API also applies pending EF migrations at startup, so
step 1 is only needed to create the database up front.

**Development Super Admin** (from `appsettings.Development.json`): `superadmin@vms.local` /
`Vms@Admin2026!`. Change it, and `AppSettings:Secret`, for any shared environment — outside
Development both must come from user-secrets or environment variables (the API refuses to start
without a 32+ character `AppSettings:Secret`).

## How it works

**Tenancy.** One database, row-level isolation. Every tenant-owned row carries a `TenantId`; EF Core
global query filters add `WHERE TenantId = <caller's tenant>` to every query, and new rows are
stamped automatically. Roles are either *platform* roles (`IsGlobal`, shared by all tenants, edited
only by a Super Admin) or a tenant's own *custom* roles. A Super Admin bypasses the filters.
Deactivating a tenant blocks its logins, revokes its sessions and cuts off live tokens within one
request (`TenantMiddleware`).

**Creating a tenant** (Super Admin → *Tenants* → *New tenant*) creates the tenant and its first
admin together. The admin is invited by a one-time link (72 h, single use) to set their own
password. New users are onboarded the same way. The link is returned to the caller as well as
emailed, so onboarding works before SMTP is configured (configure `AppSettings:Smtp` to send mail;
without it, Development logs the email instead).

**Tenant logos.** A tenant can have one logo for light mode and one for dark mode, added when the
tenant is created or later from *Edit tenant*. PNG, JPEG or WebP, up to 512 KB; the server checks the
file's real bytes, so a renamed file or an SVG is refused. They are stored in `tenancy.TenantLogos` and
served only to signed-in users (the app fetches them with the bearer token; there is no public logo
URL). A "light" logo is one drawn for light backgrounds, a "dark" logo for dark ones. The app shows the
one that suits the surface it sits on, and falls back to the other if only one exists.

**Auth.** Short-lived access tokens (30 min) carry the user's permissions; refresh tokens rotate on
every use, are stored hashed, and replaying a spent one ends all of that user's sessions. Five wrong
passwords lock the account for 30 minutes. Password rules: 8+ characters with upper, lower, digit and
special. Anonymous auth endpoints are rate limited per IP (`RateLimiting:AuthPermitPerMinute`).

**Permissions** live in `VMS.Shared/Authorization/PermissionCodes.cs`: a catalogue of codes, each with a
name, module, description and level (Interface, Operation or Field). Add a new one to `Catalog`; the seeder
adds it to the database and to the Administrator roles on the next start (no other role gains it unasked).
Platform administration is *not* a permission — it is the `is_super_admin` claim, so it can never be granted
by editing a role. You can only assign a role, or grant a permission, that you hold yourself. A user holds
their roles through `auth.UserRoles`, each with a scope (all branches, own branch, own vehicles, own records).

**Default deny.** Every controller action must say who may call it: `[RequirePermission(...)]`,
`[RequireSuperAdmin]`, `[AuthenticatedOnly]` (any signed-in user, on purpose) or `[AllowAnonymous]`. An
action that says nothing stops the API starting, listing the offenders, so a forgotten attribute is caught
in development rather than in production. Refused requests (403) are written to `auth.AccessDenials`.

**Field-level permissions.** Put `[FieldPermission(PermissionCodes.X)]` on a response property; for a user
without that permission the property is left out of the JSON on the server (absent, never zero). Exports and
printouts must use `FieldPermissionRules.HiddenProperties(type, user)` so they follow the same rules.

**Audit trail.** Every change to an audited entity is written to `core.AuditEntries` in the *same
transaction* as the change: one row per changed field on update (old and new value), one JSON snapshot on
create or delete, all sharing a `GroupId` per request. Rows cannot be updated or deleted; a database trigger
refuses it. A `DbContext` opts in by calling `modelBuilder.MapAuditEntries()` and saving through
`SaveAuditedAsync` (see `AuthDbContext`). Keep secrets and per-request counters out with `[NotAudited]`.
An event with no row change (a duplicate override, a reason) goes in with `IAuditContext.Note(...)`. A
property carrying `[FieldPermission]` is audited with the permission needed to see its values.

**Numbering.** `INumberSeries.NextAsync(db, NumberSeriesCodes.BusinessPartner, businessDate)` returns the
next number (`BP-26-00147`). It must run inside the transaction that saves the record
(`db.InTransactionAsync(...)`), which is what makes numbers gap-free; it refuses to run outside one. Prefix,
digits and reset period per tenant are edited through `GET/PUT /api/admin/number-series`.

**File storage.** Files are stored outside the database, encrypted (AES-256-GCM), under generated names in
`{tenant}/{yyyy}/{MM}/{ownerType}/{ownerId}/`, with their SHA-256 returned to the caller. `IFileStore.SaveAsync`
checks the real file type and size, runs the virus scan hook, and stores nothing if any check fails. Browsers
get a file only through a short-lived link (`IFileDownloadLinks`, two minutes by default, fifteen at most) that
hides the path. **Set before go-live:** `FileStorage:RootPath` (a folder outside the app, backed up with the
database), `FileStorage:EncryptionKey` (32 random bytes, base64; losing it loses the files) and register a
real `IVirusScanner` — the built-in one only recognises the EICAR test file. Outside Development the API
will not start without the first two. If the API runs on more than one server, share the ASP.NET Data
Protection key ring between them or download links issued by one server fail on another.

**Master data (lookups).** The lists behind dropdowns (Vehicle Type, Make, Expense Type, Province, City, …) are
defined in code (`PlatformLookups`; a module adds its own with `services.AddLookupType(...)`) and owned per tenant:
each tenant's copy is created from the defaults the first time a list is used, then edited under *Master data*
(`/admin/master-data`, `ADM_MASTER_MANAGE`). Values are never deleted — records that used one keep it — only retired,
and a code never changes. A list can carry typed extra fields (a flag, a number, text, or a reference to a value in
another list, such as a city's province). Read a list with `ILookupReader.GetActiveAsync(type)` or
`GET /api/lookups/{type}` (add `?province=PUNJAB` to filter by a field); store the value's id or code in your own table
and validate it with `ILookupReader.IsActiveAsync`. The definitions are checked at start-up.

**Messages, validation and notifications.** Every user-facing message has an ID and lives in
`VMS.Shared/Messages/messages.en.json`: the FSD's own catalogues (§12.2, §22.2) word for word, plus `VAL-GEN-*` for the
checks every form has. Code refers only to the ID (`Msg.BpCnicUsed`). **To reword or translate without a code change**, point
`Messages:Directory` at a folder holding `messages.en.json` (reword any message) and/or `messages.<locale>.json` (a
translation; whatever it leaves out stays English); edits are picked up without a restart. When input is not acceptable,
throw `new ValidationException(messages.Error("cnic", Msg.BpCnicUsed, ("BPCode", …), ("LegalName", …)))`: the API answers HTTP 400
with `{ success: false, message, errors: [{ field, code, message, params }] }`, every problem at once, each tied to its
field; the framework's own request validation answers in the same shape. The front end fetches the catalogue
(`GET /api/messages`), so a reworded message reaches every screen. In a form, wrap each control in
`<vms-field label="…" [control]="form.controls.x">`: it stars required fields, shows the message under the field once
touched (worded from the catalogue), and on submit call `form.markAllAsTouched()` so every problem shows at once (do not
disable the button). `applyServerErrors(form, apiErrors(err), messages)` puts the API's errors next to their fields and
returns what belongs to no field. For outcomes use `NotifyService`: `success` (3 s), `info` (4 s), `warn` (6 s), `error` (8 s);
a problem with one field belongs next to that field, not in a toast.

**Dates and times.** Two kinds of value, never mixed. An *instant* (created on, last sign-in) is stored as
UTC in a `datetime2`, sent by the API as ISO 8601 UTC (`2026-10-10T09:30:00Z`), and shown by the browser in the
viewer's own zone and locale. A *business date* (acquisition date, due date, expiry) is a `DateOnly` stored in a
`date` column and sent as plain `YYYY-MM-DD`; it reads the same everywhere. The API refuses an instant with no
offset and a business date with a time in it. Every `DbContext` must call
`configurationBuilder.UseUtcDateTimes()` in `ConfigureConventions` (a test fails if one forgets). "What day is
it for this company?" (numbering, reminders) comes from `IOperatingClock`, which uses the tenant's `TimeZone`
(an IANA name such as `Asia/Karachi`, validated when saved) and falls back to `Operations:DefaultTimeZone`
(default `Asia/Karachi`). The browser sends its own zone in `X-Time-Zone` on every API call; `IClientTimeZone`
exposes it to exports and printouts. Front end: use `src/app/core/datetime` — the `vmsInstant` / `vmsDate` pipes,
`toDateOnly` / `todayDateOnly` for date pickers, `notFutureDate` for validation. Never `new Date('2026-10-10')`
and never Angular's `| date` on an API value. `npm run check:datetime` runs the helpers under seven time zones.

## API

| | |
|---|---|
| `POST /api/auth/login` `refresh` `logout` `forgot-password` `reset-password` `accept-invite` | anonymous |
| `GET /api/auth/me`, `PUT /api/auth/password`, `GET /api/tenant`, `GET /api/tenant/logos/{light\|dark}` | signed in |
| `GET/POST/PATCH/DELETE /api/users`, `PUT /api/users/{id}/role`, `POST /api/users/{id}/reset-password`, `GET /api/users/assignable-roles` | `USER_VIEW` / `USER_MANAGE` |
| `GET/POST/PUT /api/roles`, `PUT /api/roles/{id}/permissions`, `PATCH /api/roles/{id}/deactivate`, `GET /api/roles/permissions` | `ROLE_VIEW` / `ROLE_MANAGE` |
| `GET/POST/PUT /api/system/tenants`, `PATCH /api/system/tenants/{id}/status`, `GET/PUT/DELETE /api/system/tenants/{id}/logos/{light\|dark}` | Super Admin |
| `GET /api/admin/number-series`, `PUT /api/admin/number-series/{code}` | `ADM_SERIES_MANAGE` |
| `GET /api/messages?locale=en` | anonymous (public wording) |
| `GET /api/lookups/{type}` (optional `?field=value` filters), `GET /api/lookups/{type}/{id}` | signed in |
| `GET /api/admin/lookups`, `GET/POST /api/admin/lookups/{type}`, `PUT /api/admin/lookups/{type}/{id}` | `ADM_MASTER_MANAGE` |
| `GET /api/files/download/{token}` | a short-lived link issued by the owning module |

## Theming (front end)

Two themes, each with a light and a dark mode. Users pick both in the top bar; the choice is
remembered in the browser, and the mode can follow the device setting.

| Theme | Look | Navigation |
|---|---|---|
| **A "Console"** (default) | Cool slate and blue, Segoe UI, softer corners | Sidebar |
| **B "Workspace"** | Warm stone and teal, IBM Plex Sans, rounder corners | Top bar |

**The rule: no colour literal anywhere except `VMSFrontend/src/styles/tokens/`.** Components,
templates, `styles.scss` and the PrimeNG preset use `var(--vms-*)` only. `npm run check:colors`
enforces it and runs automatically before `npm start` and `npm run build`.

How it fits together:

* `src/styles/tokens/_palettes.scss` holds the raw colour ramps. `_theme-a.scss` and `_theme-b.scss`
  pick steps from them for light and dark, and set the font and corner radius. `_status.scss` holds
  the shared success, warning, danger, info and neutral colours.
* `core/theme/theme.service.ts` writes `<html data-theme data-mode>`. `index.html` applies the saved
  choice before Angular starts, so the page never flashes the wrong theme.
* `core/theme/vms-preset.ts` maps PrimeNG's tokens to `var(--vms-*)`, so tables, dialogs, selects and
  tags follow the theme with no extra work.
* `layout/shell.component.ts` draws the sidebar or the top bar from the active theme.

Everyday use:

* In a component: `var(--vms-surface)`, `--vms-text`, `--vms-muted`, `--vms-border`, `--vms-brand`,
  `--vms-radius`. For status labels use `<span class="chip chip--success">Active</span>`
  (`--warning`, `--danger`, `--info`, `--neutral`).
* Change a colour: edit a ramp in `_palettes.scss`, or change which step a theme picks. Then run
  `npm run check:contrast`, which checks WCAG contrast for every pair the UI uses in all four
  theme and mode combinations.
* Add a token: add it to all four blocks (theme A and B, light and dark). `check:contrast` fails if
  one block is missing it.

## Adding a page (front end)

One entry in `VMSFrontend/src/app/app.routes.ts`:

```ts
{
  path: 'vehicles',
  ...page('Vehicles', { permission: 'VEH_VIEW' }, { label: 'Vehicles', icon: 'pi-truck', section: 'Fleet', description: 'Every vehicle in the fleet.' }),
  loadComponent: () => import('./pages/vehicles/vehicles.component').then((m) => m.VehiclesComponent),
},
```

The route guard, the menu (grouped under `section` in the sidebar) and the dashboard tile (when there is a
`description`) are all built from that one declaration, so they cannot disagree. Leave `access` out for "any
signed-in user"; use `{ superAdmin: true }` for platform-only pages. Inside a page, `*vmsCan="'VEH_EDIT'"` shows a
button only to users who hold the permission (a courtesy: the server refuses regardless). Signing in returns
people to the page they were going to, and every page's `title` shows in the tab. `npm test` runs the front-end
checks (`check:access`, `check:datetime`); `check:colors` and `check:contrast` guard the themes.

## Shared UI components (front end)

Build screens from `VMSFrontend/src/app/shared/`; a live reference with each one working is at **Administration → UI kit**
(`/admin/ui-kit`, Super Admin).

| Component | Use it for |
|---|---|
| `<vms-data-grid>` | Every list. Give it `columns` and a `load(query) => Observable<Paged<T>>`; it does server paging (25 a page), sort, loading, error with *Try again*, keeps only the newest response, goes back to page 1 when the search or filters change, and shows *"Nothing matches these filters" + Clear filters* when filtered. Custom cells and row buttons with `<ng-template vmsCell="key">` / `vmsRowActions`. |
| `<vms-filter-bar [(value)]>` | The search box and filters above a list. Search applies from 3 characters after a short pause; filters are `select`, `multiselect`, `toggle`, or filled from a master list (`lookup: 'CITY'`). |
| `<vms-lookup-picker type="…">` | A dropdown from a master list, for forms. Shows a retired value as "(retired)" instead of blank; `[cascade]="{ province: … }"` narrows it and clears it when the parent changes. |
| `<vms-date-picker>` | A business date as a plain `YYYY-MM-DD` (`[notFuture]`, `[min]`, `[max]`, `[prefillToday]`). |
| `<vms-partner-picker role="Driver">` | Choose a business partner by role (Active only, searched on the server); its **New** button opens a compact partner form and selects the result. The value is the partner's id. |
| `unsavedChangesGuard` | `canDeactivate: [unsavedChangesGuard]` on a route whose component has `hasUnsavedChanges()`: asks before losing typed work. |
| `<vms-file-upload>` | Pick or drop a file; checks kind, size and emptiness in the server's words; optionally uploads with progress (`uploadFile()` in `core/upload.ts`). |
| `ConfirmService.ask({…})` | "Are you sure?" as a promise. A destructive one is red and starts with focus on Cancel. |
| `<vms-field>` and `NotifyService` | A label, hint and validation message for any control; and the one way to show a toast (see *Messages, validation and notifications*). |

`toQueryParams(query)` (in `shared/list-query.ts`) turns a list query into URL parameters. The front-end logic behind these is
tested by `npm run check:shared`.

## Branches

The tenant's own locations (FSD §6 field 15): `core.Branches` (Core module). A tenant starts with none and gets exactly one,
"Head Office", created the first time anything asks for its branches (`GET api/branches`, any signed-in user — the same
lazy-seed idiom the platform lookups use, and just as race-safe). Optional on a partner or a vehicle, not the FSD's mandatory
field: forcing a choice with one branch and no per-user default would have protected nothing. A module that points at a branch
implements `IBranchDirectory` (in `VMS.Shared.Branches`) the same way it would `IPartnerDirectory`: no database foreign key, a
`branchId` checked against the tenant's own branches when it is sent. `vms-branch-picker` is the front-end control; a list's
Branch column and filter (`FilterDef.branches`) resolve the same way a lookup filter does. The row-level "see only your own
branch" scope (§23B.4) is Stage 6 (S6-SEC-03/05), once user branch assignment has a screen.

## Business partners

The screens are `VMSFrontend/src/app/pages/partners`: the list (`partners.component.ts`) and the partner form (`partner-form.component.ts`),
which draws one form (`partner-form.ts`) through tab components (`partner-tabs.component.ts`, `role-panels.component.ts`,
`history-tab.component.ts`). Formats, mandatory rules and duplicate logic that need no Angular are in `partner-logic.ts`, tested by
`npm run check:partners` (which also checks the screen's lists of roles and choices against the API's).

`api/partners` (module `VMS.Modules.BusinessPartners`): list, picker, export, create, update, roles, status, duplicate check, usage and history. The
Swagger page (development) lists every endpoint and its fields.

- **Pointing at a partner from another module.** Store the partner's `BusinessPartnerId` without a database foreign key (schemas stay
  separate), get the choices from `GET api/partners/picker?role=Driver`, and implement `IPartnerUsageCheck` (in `VMS.Shared.Partners`)
  and register it. The partner module asks every implementation before it removes a role, sets the partner Inactive or changes its
  party type, and refuses with what is in the way. Until a module registers one, nothing blocks.
- **Saving.** A screen loads a partner, sends the `rowVersion` back with its `PUT`, and lists in `acknowledgedDuplicateIds` every
  possible duplicate the user chose to save alongside (see `POST api/partners/duplicate-check`).
- **History of any record.** An audited entity that belongs to a larger one implements `IAuditRooted`; its audit rows then carry the
  owner (`RootEntity`, `RootRecordId`), and a history screen reads them with one query (see `PartnerQueryService.HistoryAsync`).
- **Not built yet.** Posting the opening balance (waiting on OQ-02), the fields of the Workshop, Bank, Running Customer, Tracker, Body
  Maker and Fuel Card roles (OQ-03), the linked-vehicles read (needs the vehicle module) and merging partners (deferred item D-01).

## Vehicles

Screens: `VMSFrontend/src/app/pages/vehicles` (the list, the wizard, the vehicle screen and its dialogs). Rules that need no Angular
are in `vehicle-logic.ts`, tested by `npm run check:vehicles` (which also checks its lists against the API's `VehicleConstants.cs`).
`api/vehicles`: list, get, create a Draft, update, status, category, dispose, history, driver, items, odometer, acquisition, finance, installments; and
`api/partners/{id}/vehicles` for a partner's Linked vehicles tab.

- **A Draft is not in the fleet.** It holds only the vehicle row (and, in `DraftData`, whatever the wizard collected for later
  steps — ownership and attached items included). Items, drivers, odometer readings, status moves and category changes need a
  vehicle in the fleet. `POST api/vehicles/{id}/activate` writes everything (the relation, the opening postings, the schedule, the
  items, the driver) in one transaction; `POST .../activation-check` runs the same checks and writes nothing, for the review step.
- **Other modules answer for their own records.** The vehicle module answers `IVehicleFinanceGuard` itself (BR-VH-007: a category cannot change
  from Bank Leased while a schedule has a balance). The documents module registers the real `IVehicleDocumentCheck` (in `VMS.Shared.Vehicles`), so
  activation requires the registration book on file (BR-VH-015) — see [Documents](#documents) below.
  The vehicle module registers an `IPartnerUsageCheck`, so a driver, bank, lessor, fuel card company, tracker company or supplier a
  vehicle depends on cannot be deactivated or lose the role.
- **Asking before doing.** A fuel card on another vehicle (VAL-VH-018) and a driver who already has a vehicle (VAL-VH-017) are 400s
  the screen turns into a question; the same request is sent again with `reassignFuelCard` or `releaseFromOther`.
- **Acquisition and finance are held, not posted, on a Draft.** `PUT api/vehicles/{id}/acquisition` and `PUT api/vehicles/{id}/finance`
  (with `GET` and, for finance, `DELETE`) keep the acquisition block and the bank agreement in `veh.VehicleAcquisitions` and
  `veh.VehicleFinanceAgreements`. Both need `VEH.ACQUISITION.EDIT` and the cost and finance field permissions, and only work on a Draft.
  A down payment that differs from the amount paid is refused (BR-VH-009); finance plus down payment not adding up to the price is a
  question (VAL-VH-013) answered by resending with `confirmMismatch`. Activation will write the ledger rows and the schedule.
  The wizard's step 3 (`vehicle-finance-step.component.ts`) is the screen for it; a Draft's acquisition date and type are entered there, not on step 1.
- **Activation** (`POST api/vehicles/{id}/activate`, checked first by `activation-check`) writes everything in one transaction: the vehicle goes Active, its
  ownership relation opens (dated by the acquisition date), the opening postings are written, the bank agreement goes live with its generated schedule (single-amount
  installments; the down payment is the initial payment and counts as a cost of the vehicle), its items (step 4, kept in the draft) are attached with their costs posted, and its default driver is assigned. `GET .../financial-summary` gives paid to
  date and outstanding, always summed from the ledger and the schedule. A ledger entry is never edited or deleted: `POST .../transactions/{id}/reverse` (`FIN.ADJUSTMENT.POST`) posts a reversal with a reason.
- **Paying an installment** (`POST api/vehicles/{id}/installments/{installmentId}/payments`) updates the schedule and posts one ledger
  entry in one transaction; the installment's row version stops two payments entered at once from overpaying it.

## Recurring charges

Insurance, tracker fees, rent and anything else a vehicle owes on a schedule (FSD §19A) — everything except a bank installment,
which is never configured here (see below). Screens: `VMSFrontend/src/app/pages/vehicles/vehicle-recurring-charges-tab.component.ts`
(the vehicle screen's Recurring charges tab and wizard step 4) and `VMSFrontend/src/app/pages/payables` (the fleet-wide workbench).
`api/vehicles/{id}/recurring-charges` (configure, amend, end), `api/vehicles/{id}/payables` (this vehicle's due items),
`api/payables` (the workbench, with filters and bulk confirm), `api/admin/jobs/recurring-charges/run` (the nightly job, on demand).

- **One row per amendment, not an edit in place** (BR-VH-033). `veh.VehicleRecurringCharges` rows share a `SeriesId`; only one per
  series has `EffectiveTo IS NULL` (the version in force). Amending ends the current row and inserts a new one from the effective
  date, so an entry already generated keeps referencing the version whose terms were in force when it was made.
- **A Bank Installment is never configured as a recurring charge.** BR-VH-029: the finance agreement's own schedule (`VehicleInstallment`)
  *is* the Bank Installment charge; the generator reads it, never duplicates it. A manual Bank Installment charge is refused once the
  vehicle has an active agreement (VAL-VH-030 — the FSD's own text cites VAL-VH-019 for this, but that id was already taken by
  `VhCategoryLockedByFinance` in Stage 2). `IPayablesService` is what brings the two kinds together for a screen that needs both.
- **The nightly job has no scheduler in this repo, so it built the pattern for the next one.** `IRecurringChargeGenerator.RunAsync()`
  is tenant-scoped and directly testable, like every other service; an hourly `BackgroundService` loops every active tenant through it
  (`ITenantDirectory` in `VMS.Shared.Tenancy`, implemented by the Tenancy module) via a `BackgroundTenantScope` ambient (in
  `VMS.Shared.Common`) that lets `ITenantContext` resolve a tenant with no HTTP request behind it — the same scoped services and
  tenant query filters a real request would use apply unchanged. Idempotent on (charge, period) — BR-VH-030 — both by an
  application-level pre-check and the database's own unique index, so polling hourly instead of literally once a night is harmless:
  a charge only produces something new once its lead-day window opens. `POST api/admin/jobs/recurring-charges/run` (`ADM.CONFIG.MANAGE`)
  triggers the same run on demand.
- **Auto-post is Fixed-amount only and Admin-only** (BR-VH-027/028, OQ-12: Fixed-amount charges default to it; Variable and
  Percentage-of-income can never be, and default to Generate as Due). An auto-posted transaction is `IsSystemGenerated = true`,
  `Source = RecurringCharge`, `ConfirmedBy = null` — the same posting path (`RecurringChargePosting.PostAsync`) a user's own confirm
  click uses, differing only in those three values.
- **Disposal and category change end-date charges, not just the vehicle.** `LifecycleService.DisposeAsync` end-dates every active
  charge at the disposal date and cancels a Due or Overdue entry not yet due (one already due stays payable, because the money is
  still owed — BR-VH-034). `CategoryService.ChangeAsync` end-dates only the charge type tied to the relation being closed — Vehicle
  Rent Payable for Rented, Shared Partner Payout for Shared — leaving the vehicle's other charges untouched (BR-VH-035).
- **Known simplification:** confirming, waiving or cancelling a recurring charge entry has no row-version concurrency guard (unlike
  an installment payment, which has one). Low-probability for these charge types, and correctable via the existing reversal route
  if it ever matters; revisit if a client reports it.

## Documents

The document master, upload/renew/reject/download engine and mandatory-document checks behind FSD §23A. New module,
`VMS.Modules.Documents`, schema `doc`. Screens: `VMSFrontend/src/app/pages/documents` — `documents-tab.component.ts`
(`app-documents-tab`, embedded as a "Documents" tab on both the vehicle and partner screens), `document-dialogs.component.ts`
(upload/renew, reject), and `documents-reports.component.ts` (`/documents`: Register, Missing documents, Expiry calendar,
one screen with a tab each).

- **One engine, two owners.** `Document` stores `(OwnerType, OwnerId)` with no physical FK — the same cross-module-reference
  pattern as everywhere else in this repo — reached from two routes: `api/vehicles/{id}/documents` and `api/partners/{id}/documents`.
  A slot (one owner, one document type) holds at most one current version (`IsCurrent`) plus a history of superseded ones, enforced
  twice (BR-DOC-001): an application-level check before insert, and the database's own filtered unique index
  `UX_Documents_Current` as the backstop.
- **The type master seeds lazily**, like every other platform master in this repo (`IDocumentTypeService.ListAsync`, S0-FND-12's
  race-safe idiom): the first call for a tenant inserts the twelve types §23A.2 specifies (registration book, insurance, fitness
  certificate, route permit, token tax, lease/finance agreement, purchase invoice, driving licence, CNIC, NTN certificate,
  service-rate agreement, cheque copy). `MandatoryLevel` is `None`/`Warn`/`Required` — a deliberate refinement of the FSD's stated
  single `bit`, matching what its own seed table actually needs.
- **Uploading and renewing** (`POST .../documents`, `POST .../documents/{typeId}/renew`) validate against the type: a document
  number if the type requires one, an expiry date if it is expirable. Renewing creates version n+1 and flips `IsCurrent` in one
  transaction; uploading into a slot that already has a current version is refused (`VAL-DOC-001`) — renew it instead.
- **Rejecting** (`POST /api/documents/{id}/reject`) empties the slot (no current version) so the mandatory check reports it
  missing again and the next upload is a fresh version, not treated as a renewal (BR-DOC-008).
- **Downloading** (`POST /api/documents/{id}/download-link`) returns a short-lived token link (`IFileDownloadLinks`, never the
  storage path) and writes a standalone audit note — a download changes no row, so the note rides the next `SaveChangesAsync`
  (S0-FND-08's existing machinery already covers a note with zero entity changes). A CNIC or Driving Licence needs
  `DOC.DOWNLOAD.SENSITIVE`; anything else needs `DOC.DOWNLOAD` — decided from the document's own type, so the endpoint carries
  `[AuthenticatedOnly]` and the service itself picks which permission applies (§23A.5).
- **The mandatory-document check has two faces.** `IDocumentCheck` (generic, `VMS.Shared.Documents`) feeds
  `PartnerService.GetAsync`'s `documentWarnings` — a Driver with no Driving Licence on file gets a warning, never a blocked save
  (BR-BP-005, OQ-04). `IVehicleDocumentCheck` (`VMS.Shared.Vehicles`) is the real implementation of the interface the vehicles
  module shipped as a no-op stand-in in Stage 2: a Self Owned or Bank Leased vehicle needs its registration book to activate
  (BR-VH-015). Both are the same `DocumentCheckService`, registered before `AddVehiclesModule` in `Program.cs` so the real
  check pre-empts the module's own `TryAddScoped` stand-in.
- **Two nightly jobs, no scheduler in this repo (S4-REC-05's pattern, reused).** `DocumentExpiryRecalculator` recomputes
  Active / Expiring Soon / Expired for every live current document against today plus its type's lead days (BR-DOC-003).
  `DocumentRetentionCleaner` removes a superseded or rejected version once its type's retention period has passed since it was
  superseded — not since it expired (BR-DOC-002) — deleting the stored file first and keeping the row if that fails, so the job
  retries. Both run together, hourly, from `DocumentsHostedService`, and on demand via `POST /api/admin/jobs/documents/run`
  (`ADM.CONFIG.MANAGE`).
- **The renew dialog does BR-DOC-005 and BR-DOC-006 together.** One dialog (`app-upload-renew-dialog`) handles both Upload
  and Renew; the metadata fields gate the file drop zone, so the file and its fields always travel in one multipart request.
  Renewing shows the *computed* expiry pre-filled (previous expiry plus the type's default validity), editable, not just a hint —
  matching the FSD's own wording that the user "confirms the dates" rather than works them out. For a "Has cost" type with a
  linked recurring charge type (`DocumentTypeModel.LinkedChargeTypeCode`, from the existing `DocumentChargeLink` map), it looks
  up the vehicle's matching Due/Overdue payable and, if the person opts in, **confirms that entry first** — the existing Stage 4
  posting path, the only one touched — before uploading the renewal tagged with the resulting transaction id
  (`RenewDocumentRequest.LinkedTransactionId`, purely a traceability tag, never a posting of its own). No second "post an
  expense" path exists, which is what would have let the premium appear twice.
- **A download link opens its tab synchronously.** `window.open('', '_blank')` fires inside the click itself; the tab's
  `location` is set once the short-lived link comes back a moment later. Opening the tab only after that async round-trip
  finishes falls outside the click's "user activation" window and Chrome silently blocks it as a popup — caught by the
  browser check, not by an API test, which cannot see a popup blocker.
- **Bulk document upload** (S8-QA-03, `/admin/bulk-documents`, `DOC.UPLOAD`): loading the paperwork already on file for
  many vehicles and partners at go-live, without opening each record's own Documents tab. No dedicated backend endpoint —
  each row is the same single-document upload the Documents tab itself uses, client-orchestrated one row at a time so a
  bad row's error names exactly that row without stopping the rest.

## Access control

FSD §23B. The enforcement primitives (`[RequirePermission]`, `[FieldPermission]`, the permission catalogue, denial
logging) were built in Stage 0 and used by every module since. Stage 6 added the configuration surface on top: the
last-admin guard, real multi-role assignment, data scope actually applied to queries, the six default role templates,
and the screens for all of it — `VMSFrontend/src/app/pages/roles` and `pages/users`.

- **A tenant can never be left with nobody able to manage its own roles** (BR-SEC-008). The built-in Administrator
  role (`TENANT_ADMIN`, and the platform's `SUPER_ADMIN`) can never be stripped of `ROLE_MANAGE` or deactivated, full
  stop. More generally, deactivating, deleting or reassigning a user, or removing one of their roles, is refused if it
  would drop the tenant's count of active `ROLE_MANAGE` holders to zero (`UserService.EnsureAdministratorRemainsAsync`,
  `RoleService.EnsureSomeOtherRoleGrantsAdminAsync`). Both are **explicitly scoped to the affected user's own tenant**,
  never the ambient `ITenantContext` — a Super Admin's request bypasses the tenant query filter entirely (by design,
  for cross-tenant administration), which would otherwise let the guard see every tenant's administrators at once and
  never refuse anything; a real bug this shape caught during testing, before it shipped.
- **A user can hold more than one role.** `UserRole` always allowed it (unique on (UserID,RoleID), each row with its
  own scope), but until this stage the service layer only ever used one at a time. `POST /api/users/{id}/roles` adds a
  role alongside the ones a user already holds (its own scope); `DELETE .../roles/{roleId}` removes one, never the
  last; `PUT .../role` still exists and still replaces the whole set with just one, for the common single-role case.
  `GET /api/users/{id}/effective-permissions` is the "why can they see that" data — the flattened union across every
  role, each permission tagged with which role(s) grant it. Login and token refresh now bake in that same union, not
  just the primary role's own permissions.
- **Data scope (§23B.4) is read from the JWT, the same way the tenant is.** `ICallerScope` (`VMS.Shared.Authorization`,
  registered once centrally alongside `ITenantContext`) exposes the caller's scope type, branch and linked partner as
  plain claims — no per-request DB call. Applied to the Partners and Vehicles list/export/picker queries: Own branch
  filters by branch, Own vehicles filters to vehicles where the caller's linked partner (`UserAccount.LinkedPartnerId`,
  §23B.1's "linked Business Partner where the user is also a driver") is the assigned driver, Own records filters to
  what the caller themselves created. A user holding several roles with different scopes gets the widest one across
  them (`ScopeTypes.Widest`), the same union principle as their permissions.
- **Field permissions and derived-value suppression were already comprehensive** by the time this stage started —
  every cost, finance, profit, salary and credit field on every response model across Stages 1 to 5 already carried
  `[FieldPermission]`, applied as each stage was built rather than left for a catch-up pass. Confirmed by audit, not
  rebuilt.
- **Six default role templates** (§23B.7), fully editable starting points: `TENANT_ADMIN` ("Administrator", already
  seeded, everything), `FLEET_MANAGER`, `FINANCE_USER`, `OPERATIONS_USER`, `DRIVER` (no permissions yet — its stated
  screens, trip and fuel entry, are not part of Phase 1's built features), `READ_ONLY`.
- **The role screen's permission tree is grouped by module, then by level** (Interface/Operation/Field, §23B.6), with
  a search box and a "Select all" at both grains — never granting a permission the caller does not themselves hold,
  same rule as every individual checkbox. **Cloning** (FR-SEC-001) is client-orchestrated: create the new role, then
  copy the source's own currently-allowed permissions onto it — no dedicated backend endpoint. Reading a role to clone
  it needs only `ROLE_VIEW`, so a tenant admin can clone one of the six global templates (which they cannot edit
  directly — only a Super Admin can, `RoleGuard.EnsureCanModify`) into their own, editable, tenant-owned copy.
- **The Users screen's "Manage access" dialog** is where §23B.6's role assignment, branch scope and driver link all
  live: the roles a user holds (add one with its own scope, remove one down to their last), the primary role's scope
  editor, and the driver-link picker (filtered to the Driver role). The **effective permissions viewer** (the eye
  icon) is the flattened union of everything a user holds, each permission tagged with which role(s) grant it —
  §23B.6's "why can they see that" screen.
- **Deferred, not built:** the separation-of-duties switch (BR-SEC-011) — the user confirmed it is not required for
  Phase 1 (OQ-19).

## Notifications

The rule engine and the in-app alert list behind FSD §9, §19A.6 and §23.4. New module, `VMS.Modules.Notifications`,
schema `notif`. Screens: `VMSFrontend/src/app/pages/admin/notification-rules.component.ts` (`/admin/notification-rules`)
and `VMSFrontend/src/app/layout/notification-bell.component.ts` (the shell's own bell icon, on every screen).

- **No separate Subscription table**, despite the task list's own name for S7-NOT-02. A notification candidate is
  recomputed fresh from the live source tables every time the evaluator runs — the same idiom `DocumentExpiryRecalculator`
  and `RecurringChargeGenerator` already use — so a renewed document or a paid charge simply stops producing one, with
  nothing to reconcile. FR-BP-016/FR-VH-008's "generated at save, not at the next batch run" is met instead by having
  the relevant save paths call the same evaluator synchronously right after their own save: Document upload/renew
  (`DocumentService.SaveWithFileAsync`, Stage 5), `RecurringChargeService.CreateAsync`/`AmendAsync` when an end date is
  set (Stage 4), and `ActivationService.ActivateAsync` for a finance agreement's first installment (Stage 3).
- **Six event types**, matching §19A.6's and §23.4's tables exactly: `DocumentExpiry`, `ChargeDue`, `ChargeOverdue`
  (with escalation), `ChargeEnding`, `InstallmentDue`, `ItemWarrantyEnd`. "Rent due day (Rented)" is not a rule of its
  own — Vehicle Rent Payable is itself a recurring charge (Stage 4), so it already comes through as `ChargeDue`.
- **Two cross-module source interfaces**, the same no-project-reference pattern as `IPartnerDirectory`/`IVehicleDirectory`:
  `IDocumentNotificationSource` (implemented by Documents) and `IVehicleNotificationSource` (implemented by Vehicles),
  both in `VMS.Shared.Notifications`, each returning `NotificationCandidate` rows the evaluator never needs to know the
  shape of their owning module's tables to read. A third, `INotificationTrigger`, runs the other way: Documents and
  Vehicles call it after their own save (falling back to a registered no-op when the Notifications module is absent),
  the Notifications module supplies the real implementation.
- **A recipient is a permission code, not a role** (`NotificationRule.RecipientPermission`), resolved to whoever
  currently holds it via a new cross-module `IUserDirectory` (`VMS.Shared.Users`, implemented by the Auth module) —
  deliberately robust against Stage 6 having made roles fully dynamic, renamable and clonable.
- **Deduplication is a real unique database index**, not only an in-memory check: `(TenantId, UserId, SourceEntity,
  SourceId, EventType, IsEscalation)`, because the hourly job and an admin's "run now" (or two tenants' loop
  iterations) can race — the exact same concurrency shape BR-VH-030 already protects a recurring charge entry's period
  key against, including the same "catch the duplicate-key violation and do nothing more" handling
  (`NotificationEvaluator`, `SqlException { Number: 2601 or 2627 }`). `IsEscalation` is part of that key because the
  escalation recipient can be the very same person as the ordinary recipient — an escalation is additional, not a
  substitute, and treating them as one dedup lane silently dropped the escalation notice when they coincided; caught by
  mutation-testing before it shipped.
- **S7-NOT-04 needed no code of its own.** The evaluator already asks `IOperatingClock.TodayAsync` for "today", the
  same NFR-DT-06 pattern Stage 4/5's jobs use, so a lead time means the same day for every user regardless of where
  they log in.
- **The hourly job, no scheduler in this repo** (S4-REC-05's pattern, reused a third time): `NotificationsHostedService`
  loops every active tenant through `INotificationEvaluator.RunAsync`, on demand via `POST /api/admin/jobs/notifications/run`
  (`ADM.CONFIG.MANAGE`).

## Changing the database model

```powershell
dotnet ef migrations add <Name> --project src/VMS.Modules.Auth --output-dir Data/Migrations      # Core, BusinessPartners: Data/Migrations; Tenancy: Migrations
dotnet ef migrations script --idempotent --project src/VMS.Modules.Auth --output database/scripts/002_auth_schema.sql
```

`dotnet ef` reads the connection string from `src/VMS.API/appsettings.json` (override with
`VMS_DB_CONNECTION`). Regenerate the matching `database/scripts/*.sql` after every migration
(`001` Tenancy, `002` Auth, `003` Core). A raw `CREATE TRIGGER`/`VIEW`/`PROCEDURE` in a migration must be
wrapped in `EXEC(N'…')`, or the idempotent script is invalid SQL. The audit table is *owned* by Core and only
*mapped* (excluded from migrations) in the other modules, so adding a module means a snapshot-only migration there.

## Tests

```powershell
dotnet test tests/VMS.Tests/VMS.Tests.csproj --artifacts-path $env:TEMP\vms-artifacts
```

Needs SQL Server LocalDB. Each run creates a `VMSTest_*` database, lets the API migrate and seed it, and
drops it afterwards; nothing touches `VMSGlobal`. The `--artifacts-path` keeps the build away from a running
API's binaries.

## Rules worth knowing before adding modules

* Never call `IgnoreQueryFilters()` inside a projection or subquery. It switches filters off for the
  **whole** query, not one table, and silently leaks other tenants' rows. Use it only as a deliberate,
  top-level lookup (e.g. the global email-uniqueness check).
* New tenant-owned entities implement `ITenantScopedEntity`; their DbContext implements
  `ITenantScopedDbContext` and calls `ApplyTenantQueryFilters` / `StampTenantScopedEntities` (see
  `AuthDbContext`), then saves through `SaveAuditedAsync` so the change is audited.
* Take numbers from more than one series in a fixed order (BP, VH, VT, FA, DOC). An allocation locks its
  counter until the save commits, so two saves taking them in opposite orders can deadlock.
* Create entities inside `db.InTransactionAsync(...)`, not before it: a transient failure re-runs the lambda.
