# VMS Phase 1 — Task Register

**Business Partner & Vehicle** · v1.0 · 20 Sep 2026
Source spec: *VMS Phase 1 FSD — Business Partner & Vehicle Creation*

**136 tasks · 372 developer-days · 8 stages**

---

## How to use this file

Each task is a checkbox. Update the box as work moves, and keep the log line at the bottom of each stage current.

| Mark | Meaning |
| --- | --- |
| `- [ ]` | Not started |
| `- [~]` | In progress |
| `- [x]` | Done — merged, tested, reviewed |
| `- [!]` | Blocked — add the reason inline after the task |

Each line reads: **ID** — task · `layer` · estimate · deps · spec reference.

### Completion protocol

When a task is finished:

1. Change its `- [ ]` to `- [x]`.
2. Append the actual days in brackets if it differed from the estimate, e.g. `[actual 5d]`.
3. Add a one-line note under the task only if something downstream needs to know — an interface decision, a deviation from the spec, a new dependency discovered.
4. Update the stage progress line.
5. Move to the next unchecked task in the same stage; if the stage is complete, move to the next stage whose dependencies are satisfied.

Do not start a task whose `deps` are not yet `[x]`. If a dependency is blocked, mark the dependent task `[!]` rather than working around it.

### Stage summary

| Stage | Scope | Tasks | Days | Status |
| --- | --- | --- | --- | --- |
| S0 | Foundation | 18 | 46 | ✔ 18 of 18 done |
| S1 | Business Partner | 31 | 88 | ✔ 1A (backend) 17 of 17 done, 1B (screens) 14 of 14 done (OQ-02 and OQ-03 answered: nothing to build) |
| S2 | Vehicle | 32 | 105 | ✔ 2A (backend) 16 of 16 done, 2B (screens) 16 of 16 done (documents themselves are Stage 5) |
| S3 | Acquisition & finance | 14 | 46 | ✔ 14 of 14 tasks — Stage 3 done |
| S4 | Recurring charges | 16 | 47 | ✔ 16 of 16 done |
| S5 | Document management | 15 | 44 | ☐ |
| S6 | Role-based access | 13 | 41 | ☐ |
| S7 | Notifications | 6 | 16 | ☐ |
| S8 | QA, migration, go-live | 5 | 23 | ☐ |

---

## Stage 0 — Foundation

Nothing else can start without this. None of it is visible to the client, which is why it is usually underestimated.

- [x] **S0-FND-01** — Solution scaffolding: project structure, DI, logging, configuration, error-handling middleware · `API` · 3d · deps: — · §24.3
  - Existed before the register; verified by `tests/VMS.Tests` (`dotnet test tests\VMS.Tests\VMS.Tests.csproj --artifacts-path $env:TEMP\vms-artifacts`, needs SQL Server LocalDB, uses a throwaway `VMSTest_*` database).
- [x] **S0-FND-02** — Database project, connection management, migration tooling, seed runner · `DB` · 2d · deps: S0-FND-01 · §24.3
  - Migrations run at start-up; after adding one, regenerate `database/scripts/*.sql` (`dotnet ef migrations script --idempotent`, see README).
- [x] **S0-FND-03** — Authentication: login, password hashing, token issue, refresh, logout · `API` · 4d · deps: S0-FND-01 · §23B.1
- [x] **S0-FND-04** — User and Role tables, UserRole with branch scope · `DB` · 2d · deps: S0-FND-02 · §23B.1
  - `auth.UserRoles` (scope: AllBranches / OwnBranch / OwnVehicles / OwnRecords, optional `BranchId`) is where scope lives. `UserAccount.RoleID` stays as the primary role and is kept in step by create / assign-role. `BranchId` has no FK until S0-FND-15.
- [x] **S0-FND-05** — Permission catalogue table; seed all ~55 permission codes · `DB` `CFG` · 2d · deps: S0-FND-04 · §23B.2
  - 55 codes in `PermissionCodes.Catalog`, each with a Level (Interface / Operation / Field). The FSD's `ADM.USER.MANAGE` and `ADM.ROLE.MANAGE` are the existing `USER_MANAGE` and `ROLE_MANAGE`. New codes reach the Administrator roles automatically on start-up; other roles never gain a permission unasked.
- [x] **S0-FND-06** — Authorisation middleware: endpoint permission attributes, default-deny, denial logging · `API` · 4d · deps: S0-FND-05 · BR-SEC-005/006/007
  - Every controller action must carry `[RequirePermission]`, `[RequireSuperAdmin]`, `[AuthenticatedOnly]` or `[AllowAnonymous]`, or the API refuses to start and lists the offenders. Denials (403) are written to `auth.AccessDenials`.
- [x] **S0-FND-07** — Field-level permission filter: server-side stripping of restricted fields from responses · `API` · 3d · deps: S0-FND-06 · BR-SEC-001/002
  - Put `[FieldPermission(PermissionCodes.X)]` on a response DTO property; without the permission the property is absent from the JSON (never zero) and hidden when there is no caller. Exports and printouts must use `FieldPermissionRules.HiddenProperties(type, user)`.
- [x] **S0-FND-08** — Audit log table, append-only enforcement, change-capture interceptor with old/new values · `DB` `API` · 4d · deps: S0-FND-02 · §13.2, BR-BP-022
  - Table `core.AuditEntries`, owned by the new `VMS.Modules.Core` project. Every module DbContext must call `modelBuilder.MapAuditEntries()` and save through `SaveAuditedAsync` / `SaveAudited` (see `AuthDbContext`), or its changes are not audited. Updates write one row per changed field; create and delete write one JSON snapshot; one request shares one `GroupId`.
  - Opt out with `[NotAudited]` (secrets, per-request counters, log tables). Restricted values: a `[FieldPermission]` on an entity property is audited on its own row with `RequiredPermission` set — the History tab and exports must hide those values from callers who lack it.
  - Events with no row change (duplicate override, status reason, block reason): `IAuditContext.Note(...)`; it is written with the next save, in the same transaction. A standalone event with no save (a document download, S5-DOC-10) needs a small `IAuditLog.RecordAsync` that does not exist yet.
  - Append-only is enforced by a database trigger (BR-BP-022), not just by the absence of an endpoint. Saves that need database-assigned keys are two-phase in one transaction and are not retried on a transient failure (nothing is half-saved; the request fails and can be repeated).
  - **Spec conflict for S0-FND-11 / OQ:** §13.2 says audit uses the *server* timestamp; NFR-DT-01 says system stamps, including the audit timestamp, come from the *user's machine clock*. Built as server UTC (a user's clock is not trustworthy for an audit trail). Please confirm.
  - Child rows are audited under their own entity and key; a partner's History tab will need a link back to the partner (add root-entity columns when S1-BP-06 needs them).
- [x] **S0-FND-09** — Numbering series master, gap-free allocator inside the save transaction · `DB` `API` · 3d · deps: S0-FND-02 · §24.1
  - To number a record: `await db.InTransactionAsync(async ct => { code = await series.NextAsync(db, NumberSeriesCodes.BusinessPartner, businessDate); ...; await db.SaveChangesAsync(ct); })`. It throws if called outside a transaction (that is what keeps it gap-free). Create the entities inside the lambda, since a transient failure re-runs it.
  - Allocation locks the series counter until the save commits, so saves for the same series queue. If one save takes numbers from two series, take them in the same order everywhere (BP, VH, VT, FA, DOC) to avoid deadlocks.
  - Per-tenant rows in `core.NumberSeries` (created from `NumberSeriesCodes.Defaults` on first use), counters per year/month in `core.NumberSeriesCounters`. Admin API `GET/PUT api/admin/number-series` needs `ADM_SERIES_MANAGE`; the screen is not built (S0-FND-12 style admin UI). The default date is today in UTC, so pass the record's business date; the year can otherwise be wrong for a few hours around New Year in Pakistan (OQ-11).
- [x] **S0-FND-10** — File storage service: upload, virus-scan hook, SHA-256 hash, generated naming, short-lived download URL · `API` · 4d · deps: S0-FND-01 · §23A.5
  - Contract in `VMS.Shared.Files`: `IFileStore.SaveAsync(new FileUpload(stream, name, new FileOwner("Vehicle", id), FileRules.Scans))` returns a `StoredFile` (key, SHA-256, size, content type from the real bytes, original name). The document table (S5-DOC-03) keeps that; the key is internal and never goes to a browser. `IFileDownloadLinks.Create(...)` gives a link valid two minutes (fifteen at most); the caller must check download permission and write the audit entry first (BR-DOC-007). A standalone audit entry with no save needs the `IAuditLog.RecordAsync` noted under S0-FND-08.
  - No upload endpoint yet: S5-DOC-04 owns it, with the per-type rules (`FileRules`). Files are encrypted at rest in the application (AES-256-GCM, path bound in), so a disk-level encryption requirement is met without infrastructure work, but the key must be backed up with the files.
  - **Before go-live:** set `FileStorage:RootPath` and `FileStorage:EncryptionKey` (the API will not start outside Development without them), and replace the built-in `IVirusScanner`, which only recognises the EICAR test file. **Which antivirus engine will the client run (ClamAV, Defender, an ICAP gateway)?** If the API runs on several servers, the Data Protection key ring must be shared or download links break across servers.
- [x] **S0-FND-11** — Date/time handling: UTC storage, client time-zone rendering, date-only business dates, ISO 8601 API contract · `API` `UI` · 3d · deps: S0-FND-01 · §24.4
  - Rules for every later task: an instant is a `DateTime` (UTC, `Z` on the wire); a business date is a `DateOnly` (`YYYY-MM-DD`, `date` column). Each new `DbContext` calls `UseUtcDateTimes()` (a guard test fails if it does not). Front end: `vmsInstant` / `vmsDate` pipes and the helpers in `src/app/core/datetime`; never `new Date('yyyy-MM-dd')` or Angular's `| date` on API values. Query-string dates are not covered by the JSON converter: send business dates only in bodies or as `YYYY-MM-DD` strings.
  - "Today for the company" is `IOperatingClock` (tenant `TimeZone`, default `Asia/Karachi` — **OQ-11 still needs the client to confirm**). The tenant time zone is now validated on save. Number series default to the tenant's calendar day. `IClientTimeZone` (from the `X-Time-Zone` header) is there for exports (NFR-DT-07); nothing uses it yet.
  - Deviation, see also S0-FND-08: NFR-DT-01 wants stamps taken from the user's machine clock; they are taken from the server clock in UTC, which cannot be forged by a wrong or altered PC clock. Display in the viewer's zone (NFR-DT-02) is unaffected.
  - Not built: a shared date-picker component (S0-FND-17 will wrap PrimeNG's picker with `toDateOnly` / `todayDateOnly`).
- [x] **S0-FND-12** — Generic lookup master framework: table, CRUD API, admin screen, active flag, sort order · `DB` `API` `UI` · 4d · deps: S0-FND-02 · §24.2
  - A list is a `LookupTypeDefinition` in code (`PlatformLookups.All`; a module adds its own with `services.AddLookupType(...)`); its values are per-tenant rows in `core.LookupValues`, created from the defaults the first time the list is used (not audited) and never deleted, only retired. A code is fixed at creation. Extra fields per list are typed (Flag, Number, Text, or Lookup = the code of a value in another list), checked on save, and stored as JSON.
  - To fill a dropdown: `ILookupReader.GetActiveAsync(type)` (or `GET /api/lookups/{type}`, any signed-in user). Store the value's `Id` or `Code` in your own table with no cross-schema foreign key, and validate it with `ILookupReader.IsActiveAsync` / `FindAsync` (a record made last year may still carry a value since retired). Retiring a value that another list's active values point at is refused. Admin API `api/admin/lookups` needs `ADM_MASTER_MANAGE`; the screen is at `/admin/master-data`.
  - The screen uses PrimeNG directly, like Users and Roles; S0-FND-17 replaces its table and dialog with the shared components. S5-DOC-01 (document type master) and S7-NOT-01 can extend the definitions (more attributes) instead of adding tables, or keep their own table and reference this one.
- [x] **S0-FND-13** — Seed all lookup masters listed in §24.2 · `CFG` · 2d · deps: S0-FND-12 · §24.2
  - The ten lists are checked against §24.2 of the FSD text itself by a test, so an edit to the FSD that is not mirrored in the seeds fails the build. Codes are the description in capitals (`BANK_LEASE`, `IJARAH`): business rules will recognise the Finance Type codes, so they must not change.
  - **Please confirm:** §24.2 says the two document-type lists carry an "expirable" flag but not its values. Set from the "typical validity" column of §23A.2: expirable = CNIC, Driving Licence, Agreement, Insurance, Fitness, Route Permit, Token Tax, Lease Agreement; not = NTN Certificate, Cheque Copy, Other, Registration Book, Purchase Invoice. Also, §23A.2 names 12 document types with different names ("Service / Rate Agreement", "Insurance Policy"…); S5-DOC-02 reconciles the two lists. The register asks for the client's own lists at M1; these are the defaults until then.
- [x] **S0-FND-14** — City and Province master with mapping · `DB` `CFG` · 1d · deps: S0-FND-12 · §24.2
  - 7 provinces and territories and 59 larger cities, each city in one province (a required `province` field that must be an active province). `GET /api/lookups/CITY?province=PUNJAB` gives the cities of one province for a cascading dropdown (any list can be filtered by any of its fields). A province cannot be retired while active cities use it. The city list is a starting point: the client will want its own towns.
- [x] **S0-FND-15** — Branch master and branch-scoped query filter · `DB` `API` · 2d · deps: S0-FND-12 · §23B.4
  - OQ-10 answered 2026-09-21: one branch for now. `core.Branches` (Core module, `Branch` entity, migration `Branches`): a tenant starts with none and gets exactly one, "Head Office", created the first time anything asks for its branches (race-safe, the same lazy-seed idiom as the platform lookups — 25 requests at once still make one). `GET api/branches` (any signed-in user, like the lookups). Cross-module references follow the existing convention (no physical FK, same as a vehicle's counterparty): `IBranchDirectory` in `VMS.Shared.Branches`, implemented by Core, consumed by `PartnerService` and `VehicleService` (a `branchId` that does not exist or belongs to another tenant is refused; one already on the record stays valid). Partner and vehicle lists and exports now resolve and can filter on the branch name (`FilterDef.branches` in `vms-filter-bar`, alongside `lookup`). Branch is optional, not the FSD's mandatory field: with one branch and no per-user default yet, forcing every save to choose it added no protection and would have broken the many existing tests and screens built before this task; that tightening, and the row-level `OwnBranch` scope itself, are S6-SEC-03/05, already down as depending on this task. 9 new API tests (mutation-proven: the seed's race guard, and both modules' validation), 579 in total; browser check `shots/branch-ui.mjs` (13 checks: the picker on the vehicle wizard and the partner form, both lists' column and filter); `vehicles-ui.mjs` and `partners-ui.mjs` re-run clean (44, 54).
- [x] **S0-FND-16** — Frontend shell: routing, layout, permission-driven menu, auth guards · `UI` · 4d · deps: S0-FND-06 · §23B
  - The shell, guards and menu already existed (built earlier in the project); this task made them one thing. **To add a page, add one entry in `app.routes.ts` with `page(title, access, nav)`**: the route guard, the sidebar / top-bar menu and the dashboard tile are all read from that declaration, so a page cannot be in the menu but refused at the door. Menu items group under `section` headings in the sidebar (hidden in the top-bar theme). For a button or block inside a page use `*vmsCan="'PERMISSION'"` (a courtesy only; the server still refuses).
  - Also new: signing in returns you to the page you were going to (a `returnUrl` checked so it cannot redirect off the site), a title per page for the tab and screen readers, and a "Page not found" page inside the shell instead of a silent redirect. Checked with `npm run check:access` (18 tests, mutation-checked) and end to end in a browser with a no-permission user, a view-only user and the Super Admin.
  - Not built: the unsaved-changes guard (S1-BPU-02), breadcrumbs. A field the user may not see arrives absent from the API; show the `EMPTY` dash from `core/datetime` for it (BR-SEC-004).
- [x] **S0-FND-17** — Shared UI components: data grid with server paging, filter bar, lookup picker, file upload, date picker, confirm dialog · `UI` · 5d · deps: S0-FND-16 · §9.1, §21
  - In `src/app/shared/` (`vms-data-grid`, `vms-filter-bar`, `vms-lookup-picker`, `vms-date-picker`, `vms-file-upload`, `ConfirmService`); a live reference for all of them, with usage, is at `/admin/ui-kit` (Super Admin). The list contract is `load(ListQuery) => Observable<Paged<T>>` with `toQueryParams()` for the URL; defaults follow §9.1 (25 a page, search from 3 characters, "no match" state with Clear filters). The Users list now runs on them. The list API each screen needs (sort field, filters) is the API task's job: the grid only sends what the filter bar holds.
  - Checked in a real browser against the real API (paging past 25 rows, the 3-character threshold, one request per burst of typing, sort, cascade, date limits, file checks, confirm with Escape and focus) and by `npm run check:shared` (mutation-checked). Added `GET api/lookups/{type}/{id}` so a picker can label a retired value.
  - Not built (add when a screen needs it): column chooser, saved filters, multi-column sort, Excel export, and an upload endpoint (S5-DOC-04 owns it; `uploadFile()` is ready for it). The Master data dialog and the Roles/Tenants tables still use PrimeNG directly.
- [x] **S0-FND-18** — Message resource file, validation display pattern, toast and error conventions · `UI` `API` · 2d · deps: S0-FND-17 · §12.2
  - One resource file, `VMS.Shared/Messages/messages.en.json`, holds every message by ID: the 34 messages of §12.2 and §22.2 (a test compares them with the FSD text itself; only VAL-VH-006's `{Bank / Lessor / …}` is named `{Counterparty}` so code can fill it) plus 11 platform `VAL-GEN-*`. Code uses the IDs (`Msg.BpCnicUsed`). Reword or translate with `Messages:Directory` (`messages.en.json`, `messages.ur.json`; no code change, no restart), and the front end fetches the same catalogue (`GET /api/messages`). VAL-VH-013, 017 and 018 end in a question: show them with `ConfirmService`, not as errors.
  - **Error contract for every later API task:** input that is not acceptable is `throw new ValidationException(messages.Error(field, Msg.X, values…))` (all problems at once, each with `field`, `code`, `message`, `params`, HTTP 400). The framework's own model validation now answers in the same shape. The existing Auth and Tenancy services still throw plain `BadRequestException` / `ConflictException` messages (unchanged, no catalogue ID); move them over only when touched.
  - Front end: `vms-field` (label, star, hint, message under the field once touched, red edge), `applyServerErrors` (API errors next to their fields, cleared on edit), `NotifyService` (the only place that shows a toast: success 3 s, info 4 s, warn 6 s, error 8 s; all existing pages moved over). The Users invite and edit forms use them and show every problem at once on Save instead of a disabled button; Roles, Tenants, Master data and Profile forms still have hand-written labels: switch each to `vms-field` when it is next touched.
  - Verified end to end in a browser with a reworded message file (the wording reached the form), by 281 API tests including the FSD-parity and override tests, and by 21 front-end logic tests (which also check the client's built-in `VAL-GEN-*` wording against the API's file). Limits worth knowing: the JSON reader reports only the first unreadable value in a body, so a screen sees one shape error (a date in the wrong format) at a time; the API words messages in English, while the client re-words by ID from its own catalogue.

> **S0-FND-07 matters more than its size suggests.** Field-level stripping is what makes the cost and profit restrictions real rather than cosmetic. Skip it here and every later screen has to re-solve it, and the exports will leak the amounts.

**Stage 0 progress: 18 / 18 · 54 of 54 days**

> **Register arithmetic:** the eighteen Stage 0 task estimates add up to **54** days, not the 46 stated in the stage summary and here; every other stage's tasks add up to its stated total. The header figures (136 tasks · 372 days) also differ from the stage tables (about 150 tasks · 464 days with Stage 0 corrected), so the "~74 weeks for one developer" and the milestone dates built on 372 are roughly a quarter short. Progress above is counted against the task estimates. Please confirm which figure is right before the plan is quoted to the client.

---

## Stage 1A — Business Partner backend

- [x] **S1-BP-01** — BusinessPartner table: BPCode series, Party Type, identity and tax fields, status, indexes · `DB` · 3d · deps: S0-FND-09 · §5, §6
  - New module `VMS.Modules.BusinessPartners`, schema `bp`, migration `InitialPartners`, script `database/scripts/004_bp_schema.sql`. Unique CNIC/NTN(Company)/STRN are filtered indexes that leave out merged and deleted partners (BR-BP-013). `BranchId` is stored without a foreign key until the branch master exists (S0-FND-15, OQ-10).
- [x] **S1-BP-02** — BusinessPartnerRole table with effective dating and IsActive · `DB` · 2d · deps: S1-BP-01 · §5, BR-BP-001
- [x] **S1-BP-03** — Role detail tables: BPDriverDetail, BPVendorDetail, BPCustomerDetail · `DB` · 2d · deps: S1-BP-02 · §5, §7
  - Salary, commission and credit columns carry `[FieldPermission]`; a role's panel row is kept when the role is removed and reused when it comes back.
- [x] **S1-BP-04** — Child tables: BPContact, BPAddress, BPBankAccount with IsPrimary constraints · `DB` · 3d · deps: S1-BP-01 · §8
  - One primary per partner is a filtered unique index. The General tab's address is the primary Registered address, so extra addresses in the grid cannot be primary.
- [x] **S1-BP-05** — Create partner API: one transaction across master, roles, details, children, opening balance, audit · `API` · 5d · deps: S1-BP-03, S1-BP-04 · §10, FR-BP-013
  - `POST api/partners` (`BP_CREATE`). The screen sends `roles` and the panels for them, plus `contacts`, `addresses`, `bankAccounts`. Errors carry the request's own field path (`contacts[1].mobile`).
- [x] **S1-BP-06** — Update partner API with field-level change capture · `API` · 3d · deps: S1-BP-05 · §13.2
  - `PUT api/partners/{id}` (`BP_EDIT`) must send back the `rowVersion` it loaded; a stale save is a 409 that names who changed it and when. Child rows are matched by `id` (missing = added, left out = removed). A value the caller may not see is ignored, so an edit never wipes a salary. The opening balance cannot be changed by an update.
- [x] **S1-BP-07** — Core field validation: CNIC/NTN format, mobile format, conditional mandatories by role · `API` · 3d · deps: S1-BP-05 · §6, §12.2
  - NTN is read leniently (7 digits with an optional check digit, or 13 digits): the FSD says only "format per FBR". Tighten `PartnerFormats.IsNtn` if FBR's exact format is confirmed. New generic messages `VAL-GEN-012…020` (client `DEFAULT_MESSAGES` updated to match).
- [x] **S1-BP-08** — Uniqueness for CNIC, NTN, STRN, licence number with friendly messages · `API` `DB` · 2d · deps: S1-BP-07 · BR-BP-013
  - Checked before saving and again by the unique index, so two people saving at once get one partner and a normal refusal, not a server error.
- [x] **S1-BP-09** — Duplicate detection service: exact hard checks plus name-similarity scoring · `API` · 4d · deps: S1-BP-08 · §12.1
  - `POST api/partners/duplicate-check` for the screen while typing; the save runs the same check. A similar name (≥ 85 %) only needs an answer when the city is the same.
- [x] **S1-BP-10** — Soft-duplicate override capture and audit · `API` · 1d · deps: S1-BP-09 · BR-BP-021
  - The save must list every soft duplicate in `acknowledgedDuplicateIds`; each is written to the audit trail as `DuplicateOverridden` with who and when.
- [x] **S1-BP-11** — Role add/remove with in-use guard and role change log · `API` · 4d · deps: S1-BP-06 · BR-BP-010/011/016
  - `POST api/partners/{id}/roles`, `DELETE api/partners/{id}/roles/{roleCode}` (`BP_ROLE_MANAGE`). The in-use guard asks every registered `IPartnerUsageCheck` (in `VMS.Shared.Partners`); **no module implements it yet, so nothing blocks until the Vehicle, Finance and Trip modules add theirs.** Tested with a stand-in.
- [x] **S1-BP-12** — Status change API: deactivate, reactivate, blacklist with reason and open-record guard · `API` · 3d · deps: S1-BP-06 · §13.1, BR-BP-014
  - `POST api/partners/{id}/status` (`BP_STATUS_CHANGE`); `GET api/partners/{id}/usage?intent=Deactivate` lists what is in the way. Same `IPartnerUsageCheck` note as S1-BP-11. Merged is set only by the merge action, which is the deferred item D-01.
- [x] **S1-BP-13** — Opening balance posting at creation; field lock after save · `API` · 2d · deps: S1-BP-05 · BR-BP-018
  - OQ-02 answered 2026-09-21: everyone starts at zero, so there is nothing to post. **No posting was built, on purpose.** The opening balance and its date stay as they were (stored, behind `BP_FIELD_OPENING_VIEW`, validated, and locked once the partner is saved), for a client that later wants opening balances; the FSD's posting would need a partner ledger, which Phase 1 does not have. Revisit if OQ-02 is ever answered the other way.
- [x] **S1-BP-14** — Partner list API: search across code, name, CNIC, NTN, mobile; filters; server paging and sorting · `API` · 4d · deps: S1-BP-05 · §9.1
  - `GET api/partners` (`BP_VIEW`): search from 3 characters, also finds a former legal name (BR-BP-019, read from the audit trail); filters roles, status, cityId, branchId, partyType; sort whitelist; page size capped at 100.
- [x] **S1-BP-15** — Role-filtered picker API used by every partner dropdown elsewhere · `API` · 2d · deps: S1-BP-02 · FR-BP-003, BR-BP-020
  - `GET api/partners/picker?role=&search=&take=` (`BP_VIEW`): Active partners only.
- [x] **S1-BP-16** — Linked Vehicles read API for the partner screen · `API` · 2d · deps: S2-VH-03 · §9.2
  - Built after S2-VH-03, in the Vehicles module: `GET api/partners/{id}/vehicles` (`VEH.VIEW`): owner (with the category), driver, fuel card company, tracker company and item supplier links, current first.
- [x] **S1-BP-17** — Partner export to Excel honouring field permissions · `API` · 2d · deps: S1-BP-14, S0-FND-07 · BR-SEC-003
  - `GET api/partners/export` (`BP_EXPORT`), same filters as the list, all rows (refused above 50,000), columns the caller may not see are left out, times in the caller's `X-Time-Zone`, text cells never read as formulas.

**Stage 1A progress: 17 / 17 · 47 of 47 days** (S1-BP-13 closed by the answer to OQ-02: no posting is needed)

---

## Stage 1B — Business Partner frontend

- [x] **S1-BPU-01** — Partner list screen: columns, quick search, filters, row actions, empty state · `UI` · 4d · deps: S1-BP-14, S0-FND-17 · §9.1
  - `/partners` (`BP.VIEW`), built on `vms-data-grid` and `vms-filter-bar`; Export (`BP.EXPORT`) downloads the filtered list. **Left out:** the Branch column and filter (no branch master, OQ-10) and "Has expiring document" (documents are Stage 5).
- [x] **S1-BPU-02** — Partner form shell: tab layout, header with code/name/role chips/status, dirty-state guard · `UI` · 3d · deps: S0-FND-17 · §9.2, FR-BP-011
  - `partners/new` (`BP.CREATE`) and `partners/:id`; one flat form (`partner-form.ts`) drawn by tab components (`partner-tabs.component.ts`), so the tab strip in `partner-form.component.ts` is the only part that changes for a vertical layout (Direction B). Without `BP.EDIT` the whole form is read-only. `unsavedChangesGuard` (`core/unsaved-changes.guard.ts`) is reusable for the vehicle wizard.
- [x] **S1-BPU-03** — General tab: core fields, Party Type switching CNIC/NTN, live mandatory indicators · `UI` · 4d · deps: S1-BPU-02 · §6, FR-BP-009
  - Same formats as the API (`partner-logic.ts`, kept in step by `npm run check:partners`, which also reads the API's constants). The Branch field is not shown (OQ-10).
- [x] **S1-BPU-04** — Role multi-select driving panel visibility without reload · `UI` · 2d · deps: S1-BPU-03 · FR-BP-008
  - On a new partner the roles are chosen on the General tab. On a saved partner they are changed with **Add role / ×** in the header (each change asks for a reason and is logged), not by editing the list, and only while nothing is unsaved.
- [x] **S1-BPU-05** — Role panels ×9: Driver, Workshop, Bank, Vendor, Customer, Running Customer, Tracker, Body Maker, Fuel Card · `UI` · 6d · deps: S1-BPU-04 · §7 · *split across two developers by role group*
  - OQ-03 answered 2026-09-21: Workshop, Bank, Running Customer, Tracker Company, Body Maker and Fuel Card Company need no fields beyond the common partner details, so their panels are the role itself. The Driver, Vendor and Customer panels (`role-panels.component.ts`) are the ones with fields, and the Role details tab says a role with nothing to fill in has nothing to fill in. No table, API field or panel was added, on purpose. If a client later wants a field on one of them, it follows the `Bp…Detail` pattern of S1-BP-03.
- [x] **S1-BPU-06** — Duplicate warning panel with candidate comparison and override tick · `UI` · 3d · deps: S1-BP-09 · §12.1
  - `duplicate-watcher.ts` asks while the person types (and only when an identifying field of a saved partner changes); the panel lists each candidate with why; the tick is required only for what the server would ask about.
- [x] **S1-BPU-07** — Contacts and Addresses grids with inline add, edit, primary toggle · `UI` · 3d · deps: S1-BPU-02 · §8.1, §8.2
  - Rows are inline blocks, not table cells, so they work on a narrow screen. The Addresses tab shows the General tab's address as the fixed registered address.
- [x] **S1-BPU-08** — Bank Accounts grid with IBAN validation · `UI` · 2d · deps: S1-BPU-02 · §8.3
- [x] **S1-BPU-09** — Client-side holding of child rows until first save, then single-transaction submit · `UI` · 3d · deps: S1-BPU-07 · FR-BP-010
- [x] **S1-BPU-10** — Compact partner dialog for inline creation from any picker, role pre-selected and locked · `UI` · 3d · deps: S1-BPU-03 · FR-BP-014, §10
  - Use `<vms-partner-picker role="Driver" formControlName="driverId" />` (`shared/partner-picker.component.ts`): Active partners of that role, searched on the server, with a **New** button (for `BP.CREATE`) that opens the compact dialog and selects the result. Demonstrated on Administration → UI kit.
- [x] **S1-BPU-11** — Status change dialog with reason capture and blocking-record list · `UI` · 2d · deps: S1-BP-12 · §13.1
  - The blocking list comes from `IPartnerUsageCheck`, which no module implements yet, so it is empty until the Vehicle, Finance and Trip modules add theirs (see S1-BP-12).
- [x] **S1-BPU-12** — History tab: audit entries, role change log, status change log · `UI` · 3d · deps: S0-FND-08 · §9.2
  - Needed backend work: `GET api/partners/{id}/history` and a link from a child record's audit rows to its owner. `AuditEntry` now has `RootEntity`/`RootRecordId` (migration `AuditRoot`, Core): an entity implements `IAuditRooted` and its rows carry the owner. Use the same for vehicle attachments, documents and so on. Values the caller may not see come back as `restricted`, without the value.
- [x] **S1-BPU-13** — Linked Vehicles tab · `UI` · 2d · deps: S1-BP-16 · §9.2
  - Built once S2-VH-03 existed. The tab shows for someone with `VEH.VIEW`.
- [x] **S1-BPU-14** — Save and New retaining Party Type, roles, city, branch · `UI` · 1d · deps: S1-BPU-09 · FR-BP-015
  - Keeps party type, roles and city; branch is not kept because there is no branch field yet (OQ-10).

**Stage 1B progress: 14 / 14 · 41 of 41 days** (S1-BPU-05 closed by the answer to OQ-03: the other six roles need no fields)

---

## Stage 2A — Vehicle backend

- [x] **S2-VH-01** — Vehicle table: identity, technical, operational fields, status, current-category copies, indexes · `DB` · 4d · deps: S0-FND-09 · §15, §16
  - New module `VMS.Modules.Vehicles`, schema `veh`, migration `InitialVehicles`, script `database/scripts/005_veh_schema.sql`, API `api/vehicles`. `BranchId` is a plain column (OQ-10), so Branch is optional for now (the FSD makes it mandatory). A Draft keeps what the wizard collected for steps 2 to 5 in `DraftData` (JSON, up to 200 KB, cleared on activation), because FR-VH-012 says a Draft writes only the vehicle row. `AcquisitionDate` and `AcquisitionType` are on the vehicle (settable with `VEH.ACQUISITION.EDIT`); the money of the acquisition belongs to Stage 3.
- [x] **S2-VH-02** — Registration number normalisation; filtered unique index across non-disposed vehicles · `DB` `API` · 2d · deps: S2-VH-01 · BR-VH-017
  - SQL Server cannot filter an index on a computed column, so the filters test `Status` and `IsDeleted` directly. Chassis and engine numbers are unique the same way (VAL-VH-003).
- [x] **S2-VH-03** — VehicleBusinessPartnerRelation with filtered unique index on one open relation · `DB` · 3d · deps: S2-VH-01, S1-BP-01 · BR-VH-002
  - Table `VehicleRelations`. This unblocked S1-BP-16. The partner module is now told what vehicles depend on a partner (`VehiclePartnerUsageCheck`), so the guards in S1-BP-11 and S1-BP-12 work for vehicles.
- [x] **S2-VH-04** — VehicleLifecycleHistory table and status transition service · `DB` `API` · 3d · deps: S2-VH-01 · §23.1
  - `POST api/vehicles/{id}/status` moves an in-fleet vehicle between Active, Under Maintenance and Temporarily Unavailable (and reinstates a Retired one). Assigned is set by trips, not by hand.
- [x] **S2-VH-05** — VehicleAttachedItem table with attach, detach, transfer chain · `DB` `API` · 4d · deps: S2-VH-01 · §20.1, BR-VH-013
  - Cost is written and shown only with `VEH.FIELD.COST.VIEW`. The major expense an item's cost posts is Stage 3 (S3-FIN-07).
- [x] **S2-VH-06** — OdometerReading table with floor validation against the opening reading · `DB` `API` · 2d · deps: S2-VH-01 · BR-VH-025
  - Beyond the FSD: readings must also run forward with their dates (a reading between two others must fit between them). VAL-VH-019 to 022 are new messages.
- [x] **S2-VH-07** — DriverVehicleAssignment with single-active-vehicle guard and release prompt · `DB` `API` · 3d · deps: S2-VH-01, S1-BP-03 · BR-VH-022
  - The prompt is a 400 with VAL-VH-017; the screen asks and sends the same request again with `releaseFromOther: true`. Fuel card (S2-VH-16) works the same way with `reassignFuelCard`.
- [x] **S2-VH-08** — Save Draft API: vehicle row only, no postings · `API` · 2d · deps: S2-VH-01 · FR-VH-012, BR-VH-012
  - `POST api/vehicles` (`VEH.CREATE`) and `PUT api/vehicles/{id}` (`VEH.EDIT`, with `rowVersion`). Until S2-VH-09 exists nothing can activate a vehicle through the API; the tests move a Draft to Active in the database.
- [x] **S2-VH-09** — Activate API: atomic write across vehicle, relation, lifecycle, postings, items, documents · `API` · 6d · deps: S2-VH-03, S3-FIN-06 · FR-VH-012, §19 · **highest-risk task — write rollback failure tests first**
  - `POST api/vehicles/{id}/activate` (`VEH.ACTIVATE`): body has the category and its details (a Draft holds no relation, so the ownership step's answers travel here), corrected due dates, `reassignFuelCard`, `releaseFromOther` and the vehicle's row version. In one transaction: the vehicle goes Active (category, counterparty, `DraftData` cleared), a StatusChange lifecycle row, the opening postings, the agreement goes Active with its schedule, the ownership relation opens dated by the acquisition date, and the default driver is assigned. Its fuel card or driver taken from another vehicle asks first (VAL-VH-018, VAL-VH-017). **Rollback is tested** (an open relation already on the Draft makes the last write collide; the vehicle, ledger, schedule, lifecycle, and what was taken from two other vehicles are all as before) and mutation-proven: without the transaction that test fails. Four simultaneous activations write one set of entries. **Documents:** the registration-book rule (BR-VH-015, VAL-VH-014) is asked of `IVehicleDocumentCheck`; until the documents module registers one the stand-in says nothing can be checked, so it passes and the checklist says the book is not being checked. The Stage 5 documents work must register the real one.
- [x] **S2-VH-10** — Activation pre-check returning the checklist of what is missing · `API` · 3d · deps: S2-VH-09 · BR-VH-019
  - `POST api/vehicles/{id}/activation-check` (`VEH.ACTIVATE`), same body, writes nothing: returns `canActivate`, the checklist (category and counterparty; acquisition date, type and price; bank finance agreement; registration book; installment due dates; each `ok`, `blocking` and a message), the postings and the schedule that activating would write (amounts hidden without the cost and finance permissions). Also enforces: a purchase price for Self Owned and Bank Leased, a bank-leased vehicle needs its agreement and only it may have one, the bank is the agreement's bank, and the down payment still equals the amount paid.
- [x] **S2-VH-11** — Category-specific validation for Shared, Rented, Customer Arrangement · `API` · 4d · deps: S2-VH-03 · §17.1–17.3
  - `ICategoryService.ValidateAsync` is what activation will call too. Someone without `VEH.FIELD.FINANCE.VIEW` is not asked for the amounts and cannot set them (an earlier arrangement's amounts are carried over).
- [x] **S2-VH-12** — Change Category: close relation, open new, lifecycle row, guards on open finance · `API` · 4d · deps: S2-VH-03 · BR-VH-005/006/007
  - `POST api/vehicles/{id}/category` (`VEH.CATEGORY.CHANGE`). BR-VH-007 asks `IVehicleFinanceGuard` (in `VMS.Shared.Vehicles`); **the finance module must register the real one, until then nothing is outstanding.** Bank Leased has no finance block here: that is Stage 3.
- [x] **S2-VH-13** — Dispose actions: retire, sell, transfer with counterparty, amount, reason · `API` · 3d · deps: S2-VH-04 · BR-VH-023
  - `POST api/vehicles/{id}/dispose` (`VEH.DISPOSE`). Ends the open relation and the driver's assignment. The sale amount is stored on the lifecycle row (`VEH.FIELD.COST.VIEW`); posting it is Stage 3.
- [x] **S2-VH-14** — Vehicle list API: columns, filters, quick search across reg/chassis/engine/code · `API` · 4d · deps: S2-VH-01 · §21
  - `GET api/vehicles`. **Not yet:** the filters Finance type, Has open finance and Document expiring in 30 days, and the Next document expiry column (finance and documents are later stages). Also `GET api/vehicles/{id}/history` (changes from the audit trail, plus the lifecycle).
- [x] **S2-VH-15** — Vehicle financial summary API computing paid-to-date and outstanding as aggregates · `API` · 3d · deps: S3-FIN-07 · BR-VH-003, §19
  - Same endpoint as S3-FIN-08 (`financial-summary`). The vehicle screen's panel is S2-VHU-10.
- [x] **S2-VH-16** — Fuel card uniqueness and reassignment prompt · `API` · 2d · deps: S2-VH-01 · BR-VH-026
  - Judged among vehicles in the fleet only (a Draft may hold the same number). Activation must call the same check.

**Stage 2A progress: 16 / 16 · 52 of 52 days** (all done 2026-09-21, after OQ-07 and OQ-08 were answered)

---

## Stage 2B — Vehicle frontend

- [x] **S2-VHU-01** — Wizard framework: five steps, progress bar, per-step error counts, step gating · `UI` · 4d · deps: S0-FND-17 · §21, FR-VH-009
  - `vms-wizard-steps` (shared) draws the bar, error badges and locked steps; `vehicle-wizard.component.ts` uses it. Steps 2 to 5 open once step 1 is valid and, for now, each says what it is waiting for. The bar only says which step is open, so it can lie along the top or run down the side (Direction B).
- [x] **S2-VHU-02** — Step 1 Vehicle details: identity and technical fields, conditional capacity mandatories · `UI` · 4d · deps: S2-VHU-01 · §16.1, §16.2
  - `/vehicles/new` and `/vehicles/:id/edit` (a saved vehicle in the fleet edits only step 1). Save draft; the fuel card prompt (VAL-VH-018) asks, then sends the same save again with the yes. **Left out:** Branch (OQ-10).
- [x] **S2-VHU-03** — Copy from existing vehicle pre-fill · `UI` · 2d · deps: S2-VHU-02 · FR-VH-014
- [x] **S2-VHU-04** — Step 2 Ownership: category selector driving five conditional field sets · `UI` · 5d · deps: S2-VHU-01, S2-VH-11 · §17
  - Step 2 of the wizard (`vehicle-ownership-step.component.ts`) over the shared `category-fields.component.ts`, which the Change category dialog now uses too, so the five field sets are written once. OQ-05 answered (Shared-vehicle expenses are split at month-end profit and loss), so the expense-sharing field just records the agreement. The answers are kept with the Draft as JSON in `draftData` (helpers `parseDraftData` and `serializeDraftData` in `vehicle-logic.ts`, tested) and saved by the same Save as step 1; activation sends them and opens the relation.
- [x] **S2-VHU-05** — Counterparty picker filtered by role, with inline partner creation · `UI` · 2d · deps: S2-VHU-04, S1-BPU-10 · FR-VH-013, BR-VH-020
  - The counterparty picker is filtered to the Bank role for a bank lease and offers "New" (inline partner creation) there; for the other categories it offers every active partner and the API checks the role (Customer or Running Customer for a customer arrangement), because no single role names them.
- [x] **S2-VHU-06** — Step 4 Items and documents grids · `UI` · 4d · deps: S2-VHU-01 · §20
  - Step 4 (`vehicle-items-step.component.ts`): a grid of the draft's items with Add, Change and Remove (a dialog with the same fields as attaching to a vehicle in the fleet; cost only for those who may see cost) and the total cost. The items are kept with the draft as JSON in `draftData` beside the ownership answers and saved by the same Save. **The activation request now carries them** (`items`), and the server judges each with exactly the rules of an item attached to a fleet vehicle (`ItemService.PrepareAsync`: type, description, serial unique among attached items and within the list, installation date between the acquisition date and today, supplier a Vendor or Body Maker, cost); a bad one is reported as `items[n].field`, appears on the checklist and stops the activation. On activation each is attached (installation date defaults to the acquisition date) and its cost posted as a major expense (subtype = item type, dated by the installation date, `VehicleCreation`), in the same transaction as the rest (tested inside the rollback test, AC-VH-010). A cost entered by someone without cost permission is ignored. **Documents** are not part of this step: they come with the documents module (Stage 5), and the step says so; the registration-book rule is the `IVehicleDocumentCheck` stand-in described under S2-VH-09. Checked in a real browser (`shots/activate-ui.mjs`, now 37 checks) and 3 new API tests (563 in all).
- [x] **S2-VHU-07** — Step 5 Review: read-only summary plus the list of postings about to be written · `UI` · 4d · deps: S2-VHU-01, S3-FIN-07 · FR-VH-011
  - Step 5 (`vehicle-review-step.component.ts`): a read-only summary of what was entered, the checklist, and the entries the activation would write (from `activation-check`, amounts hidden without the permissions). Looking writes nothing.
- [x] **S2-VHU-08** — Save Draft and Activate actions with the missing-items checklist · `UI` · 3d · deps: S2-VH-10 · FR-VH-012, BR-VH-019
  - Activate button with a confirm, disabled until the checklist has nothing blocking and while step 1 or 2 has unsaved edits (what is activated is what is saved). A fuel card or driver on another vehicle comes back as a question and the same activation is sent again with the yes. Save draft is on every step. Checked in a real browser: `shots/activate-ui.mjs`, 28 checks (ownership saved as draft data, checklist, postings, month-end dates, corrected due dates kept, confirm and cancel, the vehicle in the fleet with its schedule and ledger, a rented vehicle, unsaved edits blocking, a bare draft).
- [x] **S2-VHU-09** — Vehicle list screen: columns, filters, quick search, row actions · `UI` · 4d · deps: S2-VH-14 · §21
  - `/vehicles` (`VEH.VIEW`). A Draft opens in the wizard, anything else on its own screen. **Left out** (the API does not offer them yet): Branch, Finance type, Document expiring in 30 days and Has open finance filters; the Next document expiry column; the Add expense and Add income row actions; Export (S2-VHU-16).
- [x] **S2-VHU-10** — Vehicle screen shell: header, tab layout, financial summary panel · `UI` · 5d · deps: S2-VH-15 · §19
  - Above the tabs of `/vehicles/:id`, for a vehicle that is not a draft, `app-vehicle-summary` draws paid to date, total cost (with major expenses), outstanding on the lease, next installment due, and an Overdue tile in the danger colour; each tile only if the API sent its figure. There is also a Finance tab (S3-FIN-13).
- [x] **S2-VHU-11** — Change Category dialog with dated close and open · `UI` · 3d · deps: S2-VH-12 · BR-VH-005
- [x] **S2-VHU-12** — Attached Items tab with attach, detach, transfer · `UI` · 3d · deps: S2-VH-05 · §20.1
- [x] **S2-VHU-13** — Driver assignment with release prompt · `UI` · 2d · deps: S2-VH-07 · BR-VH-022
  - Also the odometer readings on the same tab (S2-VH-06).
- [x] **S2-VHU-14** — Dispose dialogs: retire, sell, transfer · `UI` · 3d · deps: S2-VH-13 · BR-VH-023
- [x] **S2-VHU-15** — Vehicle History tab replaying the induction as one dated group · `UI` · 3d · deps: S0-FND-08 · §23.2
  - Changes are grouped by the save that made them (one group per request) beside the lifecycle. The induction itself (one activation writing everything) exists once S2-VH-09 does.
- [x] **S2-VHU-16** — Vehicle export honouring field permissions · `UI` `API` · 2d · deps: S2-VH-14, S0-FND-07 · BR-SEC-003
  - `GET api/vehicles/export` (`VEH.EXPORT`) with the list's filters, every match (refused above 50,000), times in the caller's `X-Time-Zone`, text cells never read as formulas. Its columns hold nothing behind a field permission today; when the finance stage adds cost columns they must be filtered with `FieldPermissionRules` as the partner export does.

**Stage 2B progress: 16 / 16 · 53 of 53 days**

---

## Stage 3 — Acquisition, finance and opening postings

Delivers the §8 worked example: PKR 2,000,000 at creation plus a PKR 150,000 installment showing PKR 2,150,000 paid, with no stored total anywhere.

- [x] **S3-FIN-01** — VehicleTransaction table: type, amount, date, partner, reference, source, IsSystemGenerated, attachment · `DB` · 3d · deps: S2-VH-01 · §19
  - Table eh.VehicleTransactions, migration VehicleFinance, script  05_veh_schema.sql. Types: Acquisition, InitialPayment, MajorExpense, Deposit, Installment, Adjustment. Never edited or deleted (BR-VH-011): ReversesTransactionId links a correction to what it reverses. Amount is behind VEH.FIELD.COST.VIEW. The attachment is a file id (AttachmentFileId); nothing is uploaded yet. Also added eh.VehicleAcquisitions (what was entered for the acquisition while the vehicle is a Draft) for S3-FIN-04.
- [x] **S3-FIN-02** — VehicleFinanceAgreement table with terms, one-active-per-vehicle guard · `DB` · 3d · deps: S2-VH-01, S1-BP-01 · §18.2, BR-VH-004
  - Table eh.VehicleFinanceAgreements. Unique index UX_VehicleFinance_Open (filter on Status IN ('Draft','Active')) allows one open agreement per vehicle; UX_VehicleFinance_BankAgreement keeps a bank's agreement number unique even after settlement. Amounts are behind VEH.FIELD.FINANCE.VIEW. Markup rate is stored for reference only (OQ-07).
- [x] **S3-FIN-03** — VehicleInstallment table with status and paid tracking · `DB` · 2d · deps: S3-FIN-02 · §18.3
  - Table eh.VehicleInstallments: expected and paid amount, PaidOn, status (Pending, PartiallyPaid, Paid), IsResidual for the final balloon row; unique on agreement and number. Outstanding is worked out from these rows, never stored (FR-VH-006).
- [x] **S3-FIN-04** — Acquisition block API and validation, including amount-paid ceiling · `API` · 3d · deps: S3-FIN-01 · §18.1, BR-VH-021
  - GET and PUT api/vehicles/{id}/acquisition. GET needs VEH.VIEW; PUT needs VEH.ACQUISITION.EDIT and VEH.FIELD.COST.VIEW (refused with 403 without it, and the amounts are stripped from the GET). Draft vehicles only: after activation a mistake is corrected by a reversing entry. Held in eh.VehicleAcquisitions (the acquisition date and type stay on the vehicle); saving posts nothing (FR-VH-012). Guarded by the vehicle's row version. **Left for activation (S2-VH-09):** the purchase price is required for Self Owned and Bank Leased, because the category of a Draft is not known to the server until then.
- [x] **S3-FIN-05** — Finance block API with down-payment equality check and reconciliation warning · `API` · 3d · deps: S3-FIN-02 · BR-VH-009/010
  - GET, PUT and DELETE api/vehicles/{id}/finance (Draft only; PUT needs VEH.ACQUISITION.EDIT plus both field permissions). The acquisition block must be saved first (the 90-day agreement-date rule and the down-payment check read it). Down payment must equal the amount paid (VAL-VH-012, blocks). Finance amount plus down payment not equal to the purchase price comes back as VAL-VH-013 and is saved when the request is resent with confirmMismatch: true. Fully Paid is refused as a finance type: it means no agreement, so the screen removes the block instead. Bank must hold the Bank role; the agreement number is unique per bank, settled ones included. FinanceService.CrossCheck is internal to the module so activation can run the same check again.
- [x] **S3-FIN-06** — Installment schedule generator: frequency stepping, month-end fallback, residual row · `API` · 4d · deps: S3-FIN-03 · FR-VH-004
  - `ScheduleGenerator` (pure, in `Services/ScheduleGenerator.cs`): one row per period from the first due date, each date worked out from the first (31 Jan, 28 Feb, 31 Mar; a leap February is 29), monthly, quarterly or half-yearly, tenure 1 to 120, and a residual as a final row numbered after the last installment and due with it. Installments are a single amount (OQ-07, answered 2026-09-21). `GET api/vehicles/{id}/finance/schedule` previews it for a Draft's saved agreement and writes nothing; activation writes it, applying any corrected due dates (they must not go backwards or fall before the agreement date, VAL-VH-026).
- [x] **S3-FIN-07** — Opening posting engine: acquisition, initial payment, registration cost, deposit, item costs · `API` · 5d · deps: S3-FIN-04, S3-FIN-06 · §19
  - `OpeningPostings` (`Services/OpeningPostings.cs`): Acquisition (price), InitialPayment (amount paid; for a bank lease it is the down payment and is tagged `DownPayment`, posted once and counted as a cost of the vehicle, OQ-08 answered), MajorExpense/Registration, and Deposit (a rented vehicle's deposit to the lessor, a bank lease's to the bank), all dated by the acquisition date, `VehicleCreation`, system-generated. Written by activation. An attached item's cost posts a MajorExpense (subtype = item type) in the same save as the item, dated by its installation date; a cost the caller may not enter posts nothing. The bank lease itself posts nothing.
- [x] **S3-FIN-08** — Derived totals service: paid-to-date, outstanding, total payable · `API` · 3d · deps: S3-FIN-07 · FR-VH-006, BR-VH-003 · *test directly from AC-VH-003/004/005*
  - `GET api/vehicles/{id}/financial-summary` (`VEH.VIEW`, each figure behind its own field permission): acquisition cost, major expenses, total cost, deposits and **paid to date** (initial payment plus installments) as one aggregate query over the ledger, plus the agreement in force (or the latest): total payable, residual, **outstanding = total payable + residual − paid on the schedule** (FR-VH-006), installments paid, next due, overdue count and amount (worked from today, never stored). Tested against AC-VH-003, 004 and 005 directly (5,000,000 paid 2,000,000 reads 2,000,000, then 2,150,000 after a 150,000 installment; 75,000 x 36 from 10-Oct-2026 ends 10-Sep-2029 with 2,700,000 payable) and mutation-proven. A reversing entry counts under the type of the entry it reverses. **BR-VH-007 is now answered here:** the module registers the real `IVehicleFinanceGuard` (any unpaid installment of an active agreement locks the category to Bank Leased), so the finance module no longer has to.
- [x] **S3-FIN-09** — Record installment payment API updating schedule and posting a transaction · `API` · 3d · deps: S3-FIN-03 · source §8
  - `GET api/vehicles/{id}/installments` (schedule in due order, settled agreements included so the payment history stays) and `POST api/vehicles/{id}/installments/{installmentId}/payments` (`FIN.INSTALLMENT.PAY` plus both field permissions). One database transaction updates the installment (paid amount, paid on, status Pending, PartiallyPaid or Paid) and posts one `Installment` ledger entry to the bank; when nothing is left due the agreement becomes Settled. A part payment is allowed, a payment above what is left on the installment is refused, and the date cannot be in the future or before the acquisition. Guarded by the installment's row version, so two payments entered at once cannot together overpay (tested with four concurrent requests). `VehicleInstallments` gained a `RowVersion` for this (still in the one `VehicleFinance` migration). **Tests write the schedule directly, because generating it is S3-FIN-06.** Receipt upload is S3-FIN-14.
- [x] **S3-FIN-10** — Adjustment and reversal route with reason, audit, no physical delete · `API` · 4d · deps: S3-FIN-07 · BR-VH-011, source §13
  - `GET api/vehicles/{id}/transactions` (the ledger, newest first, amounts behind `VEH.FIELD.COST.VIEW`) and `POST api/vehicles/{id}/transactions/{transactionId}/reverse` (`FIN.ADJUSTMENT.POST` and the cost permission): posts an `Adjustment` of the negative amount, linked to the entry, with a required reason (up to 500), dated today or later than the entry, never in the future. The entry is untouched and there is no delete route. An entry is reversed once (unique index; four simultaneous reversals post one), a reversal cannot be reversed (post it again). Reversing an installment payment reopens that installment (a part payment leaves the other part paid) and an agreement it had settled. New columns `Reason` and `InstallmentId` on `VehicleTransactions` (still the one `VehicleFinance` migration). Maker-checker on adjustments (OQ-19) is not built: OQ-19 is unanswered.
- [x] **S3-FIN-11** — Step 3 UI: acquisition fields, finance block, live total payable · `UI` · 4d · deps: S3-FIN-05 · §18
  - `vehicle-finance-step.component.ts` (rules in `vehicle-logic.ts`, forms in `vehicle-finance-form.ts`), shown as step 3 of the wizard for a saved Draft when the person has `VEH.ACQUISITION.EDIT`, `VEH.FIELD.COST.VIEW` and `VEH.FIELD.FINANCE.VIEW`; anyone else sees a note that finance completes this step. It has its own Save button (acquisition first, then the agreement) and tells the wizard when the vehicle row changed, so saving step 1 afterwards is not refused and does not undo the date. **A Draft no longer shows Acquisition date and type on step 1** (they belong to step 3; an in-fleet vehicle still shows them). The bank block appears for a Lease, when an agreement is saved, or when the box is ticked; unticking with a saved agreement asks, then removes it. Down payment follows the amount paid and finance amount suggests the balance until typed over; total payable is installment times tenure; the VAL-VH-013 question is a confirm dialog that resends with `confirmMismatch`. Leaving step 3 with unsaved edits asks first. **Left out:** the schedule preview with editable due dates (S3-FIN-12, needs the generator). Checked in a real browser (`shots/finance-ui.mjs`, 33 checks) and `check:vehicles` (21 tests, now also parity with the API's payment modes and frequencies, and that every API call carries the `${api}` prefix).
  - **Fixed on the way:** the Export button of the vehicle list (S2-VHU-16) was calling `/vehicles/export` without the API prefix, so it would have failed in use. It is corrected and the new test guards it.
- [x] **S3-FIN-12** — Schedule preview grid with editable due dates before save · `UI` · 3d · deps: S3-FIN-06 · FR-VH-005
  - The schedule preview is on the Review step: every row with its due date, the residual marked, a native date input per row (so 36 or 120 rows stay light) and a "changed" marker; a corrected date is re-checked at once (not before the agreement date, never backwards) and sent with the activation.
- [x] **S3-FIN-13** — Vehicle Finance tab: agreement summary, schedule, payment history, outstanding · `UI` · 4d · deps: S3-FIN-08 · source §5
  - Finance tab (`vehicle-finance-tab.component.ts`): the bank agreement, the installment schedule (overdue rows marked from the date), and the ledger with reversed entries struck through and each reversal showing its reason. Checked in a real browser: `shots/fintab-ui.mjs`, 21 checks.
- [x] **S3-FIN-14** — Record installment payment dialog with receipt upload · `UI` · 2d · deps: S3-FIN-09 · source §7
  - The dialog (`PayInstallmentDialogComponent`) records the payment, then offers a receipt step: `vms-file-upload` posts to `POST api/vehicles/{id}/installments/{installmentId}/payments/{transactionId}/receipt` (`FIN.INSTALLMENT.PAY`, PDF/PNG/JPEG up to 10 MB, via `IFileStore`). A receipt is attached once — attaching a second is refused (VAL-VH-029); a wrong one is corrected the same way a wrong amount is, by reversing the payment. `GET api/vehicles/{id}/transactions/{transactionId}/receipt` (`VEH.FIELD.COST.VIEW`) streams it back, checked against its recorded SHA-256 on top of the file store's own tamper check on the encrypted bytes (both are mutation-tested, with the DB-only case covered by tampering the recorded hash while leaving the file alone). `VehicleTransaction.AttachmentFileId` (never wired to anything) is replaced by `ReceiptStorageKey/Sha256/ContentType/FileName/SizeBytes`, in a new `VehicleReceipts` migration (the `VehicleFinance` migration had already reached the user's database, so this is additive rather than a rewrite). The Finance tab's ledger shows a receipt as a link by its file name. Checked in a real browser (`shots/receipt-ui.mjs`, 5 checks) and the API (7 new tests in `VehicleLedgerTests`, 570 in total).

**Stage 3 progress: 14 / 14 · 46 of 46 days**

---

## Stage 4 — Recurring charges

- [x] **S4-REC-01** — VehicleRecurringCharge table with effective dating for amount changes · `DB` · 3d · deps: S2-VH-01 · §19A.1, BR-VH-033
  - Table `veh.VehicleRecurringCharges`, migration `RecurringCharges`, script `database/scripts/005_veh_schema.sql`. A `SeriesId` (Guid) is stable across an amendment's rows; a filtered unique index allows one row with `EffectiveTo IS NULL` per series (the same idiom as `VehicleRelation`). Amending ends the current row (`EffectiveTo`) and inserts a new one from the effective date; an entry already generated keeps referencing the specific version row that was in force when it was made, so its terms never change retroactively.
- [x] **S4-REC-02** — VehicleRecurringChargeEntry table with period key and unique constraint · `DB` · 2d · deps: S4-REC-01 · BR-VH-030
  - Table `veh.VehicleRecurringChargeEntries`, same migration. Unique on `(VehicleRecurringChargeId, PeriodKey)` (`UX_VehicleRecurringChargeEntries_Period`); mutation-proven together with the generator's own pre-check (both had to be removed at once before a duplicate appeared). The FSD's "Scheduled" status is not materialised: nothing in the mechanics creates a row before its lead-day window opens, so a row is born Due.
- [x] **S4-REC-03** — Charge configuration API with payee role filtering and posting-mode rules · `API` · 4d · deps: S4-REC-01 · §19A.1, BR-VH-027
  - `GET/POST api/vehicles/{id}/recurring-charges`, `PUT .../recurring-charges/{chargeId}` (amend), `POST .../recurring-charges/{chargeId}/end` — all `FIN.RECURRING.MANAGE`. New lookup list `RECURRING_CHARGE_TYPE` (12 values, §19A.1) in `PlatformLookups`. The payee's role is checked only where the FSD names one (Bank for Bank Installment, Tracker Company for Tracker Fee); the other ten types accept any active partner, on purpose — the FSD gives those two as examples, not an exhaustive rule.
- [x] **S4-REC-04** — Auto-post permission gate and fixed-amount-only restriction · `API` · 2d · deps: S4-REC-03, S0-FND-06 · BR-VH-027/028
  - OQ-12 answered 2026-09-22: Fixed-amount charges default to Auto-post (Variable and Percentage of income can never be, BR-VH-027, and default to Generate as Due). `FIN.RECURRING.AUTOPOST` is required to save a charge with Auto-post (BR-VH-028, 403 otherwise); both rules mutation-proven.
- [x] **S4-REC-05** — Nightly generation job: idempotent on (charge, period), lead-day windowing · `JOB` · 5d · deps: S4-REC-02 · BR-VH-030
  - `RecurringChargeGenerator` (pure date math in `ChargeSchedule`, tested with no database). No job scheduler existed in the repo, so this is the pattern for every job from here on: a tenant-scoped, directly testable `IRecurringChargeGenerator.RunAsync()`, an hourly `BackgroundService` that loops every active tenant through it (new `ITenantDirectory` in `VMS.Shared.Tenancy`, implemented by the Tenancy module; a new `BackgroundTenantScope` ambient (`AsyncLocal`) lets `ITenantContext` resolve a tenant with no HTTP request, so the same scoped services and query filters a real request would use apply unchanged), and `POST api/admin/jobs/recurring-charges/run` (`ADM.CONFIG.MANAGE`) to trigger it on demand — which is also how the job's idempotency is checked without a scheduler in the test harness. Polling hourly instead of literally once a night is safe precisely because it is idempotent: a charge only produces something new once its lead-day window opens.
- [x] **S4-REC-06** — Bank installment surfacing: read VehicleInstallment as Due, no parallel schedule · `JOB` `API` · 3d · deps: S4-REC-05, S3-FIN-03 · BR-VH-029
  - `IPayablesService` reads `VehicleInstallment` rows of an Active agreement directly — never a `VehicleRecurringCharge` row — and shows them the same shape as a generated entry (`Kind: "Installment"`), on the same default 7-day lead window a charge starts with (installments have no lead-days field of their own). Confirming one still calls the existing `InstallmentService.PayAsync`, not a new posting path.
- [x] **S4-REC-07** — Duplicate-charge guard blocking a manual Bank Installment charge on a financed vehicle · `API` · 1d · deps: S4-REC-06 · VAL-VH-030
  - The FSD's own text (§19A.3) cites VAL-VH-019 for this, but that id was already given to `VhCategoryLockedByFinance` in Stage 2; used VAL-VH-030 instead (noted inline in `Msg.cs`). Refused only once the vehicle has an Active agreement, matching the rule's literal wording. Mutation-proven.
- [x] **S4-REC-08** — Confirm payment API: post one transaction, retain expected vs actual, link back · `API` · 4d · deps: S4-REC-02, S3-FIN-01 · BR-VH-031
  - `POST api/vehicles/{id}/recurring-charges/entries/{entryId}/confirm` (`FIN.DUE.CONFIRM` + `VEH.FIELD.COST.VIEW`). Posts a new `VehicleTransaction` (`Type=RecurringCharge`, `Source=RecurringCharge`, `SubType`=charge type code); shared with Auto-post via `RecurringChargePosting.PostAsync`, which differs only in `IsSystemGenerated` and a null `ConfirmedBy` (BR-VH-028).
- [x] **S4-REC-09** — Waive and cancel actions with reason and audit · `API` · 2d · deps: S4-REC-08 · §19A.4
  - `POST .../entries/{entryId}/waive` (`FIN.DUE.WAIVE`) and `POST .../entries/{entryId}/cancel` (`FIN.RECURRING.MANAGE`), each requiring a reason and refusing anything not Due or Overdue (a Paid entry cannot be waived or cancelled, BR-VH-032).
- [x] **S4-REC-10** — Overdue transition job and escalation flagging · `JOB` · 2d · deps: S4-REC-05 · §19A.4
  - The same `RecurringChargeGenerator.RunAsync()` call moves every passed-due Due entry to Overdue after generating, in the same run — one nightly pass, not two competing jobs. Escalation (a second notification tier) is left to Stage 7, which owns the notification rule engine; nothing here blocks it.
- [x] **S4-REC-11** — End-dating cascade on vehicle disposal and category change · `API` · 3d · deps: S4-REC-01, S2-VH-12 · BR-VH-034/035
  - `IRecurringChargeService.EndDateForDisposalAsync` (called from `LifecycleService.DisposeAsync`, BR-VH-034: every active charge end-dated at the disposal date; a Due or Overdue entry not yet due is cancelled, one already due stays payable — mutation-proven, both halves) and `.EndDateForClosedCategoryAsync` (called from `CategoryService.ChangeAsync`, BR-VH-035: only the charge type tied to the category being left — Vehicle Rent Payable for Rented, Shared Partner Payout for Shared — is end-dated with the relation; the rest of the vehicle's charges are untouched). The "prompted to set up replacements" half is left to the UI (S4-REC-12): the API does not block the category change on it.
  - 24 new API tests (`RecurringChargeTests.cs` + `ChargeScheduleTests`, pure date math with no database), 615 in total; mutation-proven: the duplicate Bank Installment guard, the generation job's idempotency (both the application pre-check and the database's own unique index, removed together), the Auto-post permission gate, and the disposal cascade's future-vs-already-due split.
- [x] **S4-REC-12** — Recurring Charges grid in wizard step 4 and on the vehicle screen · `UI` · 4d · deps: S4-REC-03 · §19A.5
  - One component (`app-recurring-charges-tab`) draws both the "Recurring charges" grid and the "Due & Payments" panel (S4-REC-13), so it is written once and used in two places: the vehicle screen's Recurring charges tab (shown once a vehicle is not a Draft) and wizard step 4, under Attached items, once the vehicle is saved (a charge needs a real `VehicleId`, unlike items, which stay in `draftData` until activation). Add, Amend and End dialogs (`recurring-charge-dialogs.component.ts`) follow the same pattern as every other vehicle dialog (`ActionDialog`). Ended and superseded rows collapse under a "N ended or superseded" disclosure so the current list stays short.
- [x] **S4-REC-13** — Due and Payments panel on the vehicle screen · `UI` · 3d · deps: S4-REC-08 · §19A.5
  - Built together with S4-REC-12 above, in the same tab. Confirm and Waive/Cancel dialogs are in `payable-dialogs.component.ts`. A Bank Installment item's Confirm action reuses the existing `PayInstallmentDialogComponent` (fetching that installment's own row version first) rather than a second payment flow — one dialog, one set of rules, matching BR-VH-029's "one schedule, two views".
- [x] **S4-REC-14** — Payables Due workbench: fleet-wide list, filters, sorting · `UI` · 4d · deps: S4-REC-08 · FR-VH-016
  - New page `/payables` (`app-payables`, `FIN.DUE.CONFIRM`): filters by charge type, payee, branch and due window (`vms-lookup-picker`, `vms-partner-picker`, `vms-branch-picker`, `vms-date-picker`), an "Overdue only" toggle, and a plain sortable-by-due-date table (the API returns the whole filtered list, not a server page, since a Finance user is meant to see everything due at once, not page through it).
- [x] **S4-REC-15** — Bulk confirm with common payment date and mode · `UI` `API` · 3d · deps: S4-REC-14 · §19A.5
  - Row checkboxes plus "select all" and a sticky bulk-confirm bar (common payment date and mode); each item is still checked and posted through the same single-item routes (`InstallmentService.PayAsync` or the charge confirm), so one bad row is reported and skipped rather than stopping the batch, and an item with no amount override pays the figure the workbench showed (§19A.5's "clear twelve tracker fees in one action").
- [x] **S4-REC-16** — Dashboard tile: due within 7 days, overdue count and total · `UI` · 2d · deps: S4-REC-14 · §19A.5
  - A live tile (not the static per-page nav tiles the dashboard otherwise draws from the route table) shown only to someone with `FIN.DUE.CONFIRM`, linking to `/payables`; turns to the danger colour once anything is overdue.
  - Front end: 24 logic tests (`check:vehicles`, parity of the three new vocabularies against `VehicleConstants.cs`), full Angular build and `npm test`/`check:colors`/`check:contrast` green; browser check `shots/recurring-charges-ui.mjs` (17 checks: configure, generate, confirm, amend, end, the fleet-wide workbench, bulk confirm, the dashboard tile). **Known simplification:** a recurring charge entry's confirm/waive/cancel has no row-version concurrency guard (unlike an installment payment, which has one) — two people confirming the exact same entry at the same moment could both post; low-probability for these charge types and correctable via the existing reversal route if it ever happens. Revisit if a client reports it.

> **S4-REC-05 and S4-REC-06 together are the subtle part.** The generation job must treat the lease schedule as a source it *reads*, not a schedule it owns — otherwise the fleet ends up with two competing installment records per month. Test with a financed vehicle that also carries insurance and tracker charges.

**Stage 4 progress: 16 / 16 · 47 of 47 days — Stage 4 done**

---

## Stage 5 — Document management

- [x] **S5-DOC-01** — DocumentType master: applies-to, expirable, validity, periodic, lead days, mandatory, retention · `DB` `CFG` · 3d · deps: S0-FND-12 · §23A.1
  - New `VMS.Modules.Documents` module, schema `doc`. `DocumentType` refines the FSD's single mandatory `bit` into `MandatoryLevels` (None/Warn/Required), matching §23A.2's own seed table (some types are Warn, not a hard Required) — a deliberate, documented refinement, not a deviation.
- [x] **S5-DOC-02** — Seed the twelve document types with their periodicity settings · `CFG` · 1d · deps: S5-DOC-01 · §23A.2
  - Lazy per-tenant seed (S0-FND-12's race-safe raw-SQL idiom), `PlatformDocumentTypes.Defaults`, 12 rows matching §23A.2 exactly (applies-to, role, expirable, validity, periodic, mandatory level, document-number/cost flags).
- [x] **S5-DOC-03** — Document table serving both owners: version number, IsCurrent, supersedes link, status · `DB` · 4d · deps: S5-DOC-01, S0-FND-10 · §23A.3
  - `Document` (owner type + id, no physical FK — `IPartnerDirectory`/`IVehicleDirectory` validate), `IAuditRooted` so every version's audit trail rolls up to its owner. BR-DOC-001 (one current version) enforced twice: app-level `AnyAsync` pre-check in `UploadAsync`, and a filtered unique index `UX_Documents_Current` on (TenantId,OwnerType,OwnerId,DocumentTypeId) WHERE IsCurrent=1 as the DB backstop; both confirmed by mutation test.
- [x] **S5-DOC-04** — Upload API: type-driven validation, format and size checks, hash, scan, audit · `API` · 4d · deps: S5-DOC-03 · §23A.5, BR-DOC-007
  - `POST /api/{vehicles|partners}/{id}/documents`, `IFileStore`/`FileRules` (S0-FND infrastructure) for the format/size/hash checks; first real consumer of the token-link download flow built in Stage 0.
- [x] **S5-DOC-05** — Renewal API: create version n+1, flip IsCurrent in one transaction · `API` · 3d · deps: S5-DOC-04 · BR-DOC-001
  - `POST .../documents/{typeId}/renew`; refuses with `VAL-DOC-002` if there is nothing current to renew (use upload instead).
- [x] **S5-DOC-06** — Expiry computation job: nightly recalculation to Active / Expiring Soon / Expired · `JOB` · 2d · deps: S5-DOC-03 · BR-DOC-003
  - `DocumentExpiryRecalculator`, run hourly by `DocumentsHostedService` (S4-REC-05's no-scheduler-in-repo pattern, reused) across every active tenant via `BackgroundTenantScope` + `ITenantDirectory`; also reachable on demand via `POST /api/admin/jobs/documents/run`. Status-transition boundary confirmed by mutation test.
- [x] **S5-DOC-07** — Mandatory-document check feeding vehicle activation and partner save · `API` · 2d · deps: S5-DOC-03, S2-VH-10 · BR-VH-015, BR-BP-005 · OQ-04 answered: warn only, never blocks a partner save; OQ-15 moot for this phase (no trip assignment module yet)
  - `DocumentCheckService` implements both `IDocumentCheck` (generic, feeds `PartnerService.GetAsync`'s `documentWarnings`) and `IVehicleDocumentCheck` (the real registration-book check, replacing Vehicles' `NoDocumentCheck` stand-in — module registration order in `Program.cs` matters here). Role-narrowing (a Driving-Licence gap only warns a Driver, never a Workshop) confirmed by mutation test.
- [x] **S5-DOC-08** — Reject action with reason, excluded from mandatory satisfaction · `API` · 2d · deps: S5-DOC-04 · BR-DOC-008
  - `POST /api/documents/{id}/reject`; empties the slot (`IsCurrent=false`, `Status=Rejected`) so the mandatory check reports it missing again and the next upload is a fresh version, not a renewal.
- [x] **S5-DOC-09** — Retention job removing superseded versions past their type's retention, with a log · `JOB` · 2d · deps: S5-DOC-03 · BR-DOC-002
  - `DocumentRetentionCleaner`, same hosted service as S5-DOC-06; ages from `CreatedOn` (when the version was superseded), not from the document's own expiry date. Deletes the stored file first, then the row; keeps the row if the file delete fails so the job retries. Age-cutoff boundary confirmed by mutation test.
- [x] **S5-DOC-10** — Download permission split and audited download endpoint · `API` · 2d · deps: S5-DOC-04, S0-FND-06 · §23A.5
  - `POST /api/documents/{id}/download-link` issues a short-lived `IFileDownloadLinks` token; needs `DOC_DOWNLOAD_SENSITIVE` for a CNIC or Driving Licence, `DOC_DOWNLOAD` otherwise — decided from the document's own type, so the endpoint carries `[AuthenticatedOnly]` rather than a single fixed `[RequirePermission]`, and the service picks the permission it actually needs. A download has no row change of its own: `audit.Note(...)` rides the next `SaveChangesAsync` (S0-FND-08's existing machinery already handles a note with zero entity changes — no new `IAuditLog.RecordAsync` interface was needed, unlike this section's earlier note above assumed).
- [x] **S5-DOC-11** — Documents tab for partner and vehicle: current by type, status chips, history toggle · `UI` · 5d · deps: S5-DOC-04 · §23A.4
  - `app-documents-tab` (`pages/documents/documents-tab.component.ts`), one component embedded on both the vehicle screen (new "Documents" tab) and the partner screen (new "Documents" tab, gated by `DOC.VIEW` like `canSeeVehicles`), only differing by an `ownerType`/`ownerId` input. A slot per applicable type; an empty slot offers Upload, a current version offers Download/Renew/Reject; earlier versions sit behind a per-slot `<details>` toggle, never a separate screen.
- [x] **S5-DOC-12** — Renew dialog with pre-filled expiry and optional expense posting linked to a recurring charge · `UI` · 4d · deps: S5-DOC-05, S4-REC-08 · BR-DOC-005/006 · **the link was gotten right: see below**
  - `app-upload-renew-dialog` (`document-dialogs.component.ts`) handles both Upload and Renew (one form, a `current` input distinguishes them); the metadata fields gate the drop zone (`vms-file-upload`'s `disabled`/visibility), so the file and its fields always go up together in one multipart request. Renewing pre-fills the computed expiry (previous expiry + the type's default validity) as an editable value, not just a hint, per BR-DOC-005's own wording ("computes... the user confirms the dates").
  - **BR-DOC-006, done with no double-counting risk:** required a small backend addition first — `RenewDocumentRequest.LinkedTransactionId` (nullable, trusted at face value: it only tags the new version for traceability and never itself posts anything) and `DocumentTypeModel.LinkedChargeTypeCode` (from the already-existing `DocumentChargeLink` map, previously unconsumed). The dialog looks up the vehicle's Due/Overdue payables for the type's linked charge type (via the `RECURRING_CHARGE_TYPE` lookup's code, cross-referenced against `Payable.chargeTypeId`) and, if the person opts in, **confirms that recurring-charge entry first** (the existing, already-idempotent Stage 4 posting path — the only posting path touched) and only then uploads the renewal tagged with the resulting transaction id. No second, ad-hoc "post an expense" path was added, which is what would have risked the premium appearing twice.
- [x] **S5-DOC-13** — Document Register screen with full filter set · `UI` · 4d · deps: S5-DOC-06 · §23A.4
- [x] **S5-DOC-14** — Missing Documents report · `UI` `API` · 3d · deps: S5-DOC-07 · §23A.4
- [x] **S5-DOC-15** — Expiry Calendar month view · `UI` · 3d · deps: S5-DOC-06 · §23A.4
  - S5-DOC-13/14/15 are one routed page, `/documents` (`app-documents-reports`, nav entry "Documents", permission `DOC.REGISTER.VIEW`): three tabs — Register (owner type/document type/status/expiring-within filters), Missing documents, Expiry calendar (a real Monday-first month grid, not a list, with prev/next navigation) — one screen answering three related questions rather than three separate nav entries for the same data.
  - Real download links use a synchronous `window.open('', '_blank')` at the moment of the click, filled in with the actual URL once the short-lived link comes back — opening the tab only after the async round-trip is complete gets silently blocked as a popup by Chrome, since it falls outside the click's user-activation window. Caught and fixed via the browser check, not the API tests (which cannot see a popup blocker).
  - Front end: `npm test` (24), `check:colors`, `check:contrast` green, full Angular build clean; browser check `shots/documents-ui.mjs`, 18/18 (upload into an empty slot, reject and re-upload as a fresh version, a Has-cost/expirable type's full metadata + BR-DOC-006 panel with nothing to link, the pre-filled renewal expiry, a popup-safe download, the BR-BP-005 warning banner appearing and clearing, and all three reports).

**Stage 5 progress: 15 / 15 · 44 of 44 days — Stage 5 done**

---

## Stage 6 — Role-based access control

Enforcement primitives were built in S0-FND-05 to S0-FND-07. This stage delivers the configuration surface and applies permissions across every screen already built.

- [x] **S6-SEC-01** — Role CRUD API with system-role protection and last-admin guard · `API` · 3d · deps: S0-FND-04 · BR-SEC-008
  - Role CRUD, system-role protection (`RoleGuard`) and privilege-escalation prevention already existed from S0's foundation work. Added the two things BR-SEC-008 still needed: the built-in Administrator role (`TENANT_ADMIN`/`SUPER_ADMIN`) can never be stripped of `ROLE_MANAGE` or deactivated (`RoleService.ReplacePermissionsAsync`/`DeactivateAsync`), and — the real "last-admin guard" — the tenant can never be left with zero active users able to manage roles, checked before every deactivation, deletion, role reassignment or role removal that could cause it (`UserService.EnsureAdministratorRemainsAsync`, `RoleService.EnsureSomeOtherRoleGrantsAdminAsync`). **Explicitly scoped to the affected user's own tenant, never the ambient one** — a real bug caught by testing: a Super Admin's request bypasses the tenant query filter entirely (by design, for cross-tenant administration), which silently let the guard see every tenant's administrators and never refuse anything.
- [x] **S6-SEC-02** — RolePermission assignment API with audit of every grant and revoke · `API` · 3d · deps: S6-SEC-01 · BR-SEC-010
  - Assignment API existed from S0. Its audit trail needed no new plumbing: `AuthDbContext` already auto-audits every `SaveChangesAsync()` (`SaveAuditedAsync`), and neither `Role`, `RolePermission` nor `UserRole` is `[NotAudited]` — a grant or revoke was already written to `core.AuditEntries` with before/after values. Confirmed, not built.
- [x] **S6-SEC-03** — User CRUD, role assignment, branch scope, link to a Driver partner · `API` · 4d · deps: S6-SEC-01, S1-BP-03 · §23B.1 · OQ-20 answered: same role system
  - User CRUD existed from S0. Added: `PUT /api/users/{id}/scope` (settable `ScopeType`/`BranchId`, validated against `ScopeTypes.All` and `IBranchDirectory`) and `PUT /api/users/{id}/driver-link` (`UserAccount.LinkedPartnerId`, new nullable column, validated via `IPartnerDirectory` — must carry the Driver role). Both cross-module references follow the established no-FK pattern.
- [x] **S6-SEC-04** — Effective permission resolution: union across roles, session re-evaluation on change · `API` · 3d · deps: S6-SEC-02 · BR-SEC-009
  - `UserRole` already allowed several distinct roles per user (unique on (UserID,RoleID)), but the service layer only ever used one at a time. Added genuine multi-role: `POST /api/users/{id}/roles` (add a role with its own scope), `DELETE /api/users/{id}/roles/{roleId}` (remove one, never the last), `GET /api/users/{id}/effective-permissions` (the flattened union, tagged with which role(s) grant each one — the "why can they see that" data). Token issuance (`AuthService.LoadRoleAsync`) now unions permissions across every active role a user holds, not just their primary one; the effective scope is the widest across them (`ScopeTypes.Widest`). **Known trade-off, unchanged from before this stage:** the 30-minute access token is not re-checked mid-life; a permission or scope change ends the refresh session (forces a new token within that window) rather than invalidating an already-issued one instantly.
- [x] **S6-SEC-05** — Data scope filters applied to partner, vehicle and transaction queries · `API` · 4d · deps: S0-FND-15, S6-SEC-03 · §23B.4
  - New `ICallerScope` (`VMS.Shared.Authorization`, mirrors `ITenantContext`'s JWT-claim pattern, registered once centrally) exposes the caller's scope to any module. Applied to the Partners list/export/picker and the Vehicles list/export (`ApplyScope` in each `*QueryService.Filter`): Own branch filters by `BranchId`; Own vehicles filters by `DefaultDriverId == LinkedPartnerId` (the driver app's scope); Own records filters by `CreatedBy`. **Known simplification:** transaction/ledger-level queries (a vehicle's own finance tab) are not separately scope-filtered — access to them already requires the vehicle to appear in the scoped list first, so the practical exposure is the same; a true per-transaction filter is left for if a client's use of Own-records/Own-branch scope on finance data specifically turns out to need it.
- [x] **S6-SEC-06** — Apply field permissions to every existing response: cost, finance, profit, salary, credit · `API` · 4d · deps: S0-FND-07, S3-FIN-08 · BR-SEC-001/002
  - Audited, not rebuilt: every cost/finance/profit/salary/credit field across every module's response models already carries `[FieldPermission]` (applied incrementally, correctly, as each stage was built) — grepped every `decimal`/`decimal?` property in every `Models/*.cs`; the unmarked ones are all on *request* models (input, which `[FieldPermission]` does not govern), never on a response. No gaps found.
- [x] **S6-SEC-07** — Derived-value suppression: hide totals, charts and exports that would reveal a hidden field · `API` · 3d · deps: S6-SEC-06 · BR-SEC-002
  - Same audit: every derived total already carries its own `[FieldPermission]` (`FinancialSummaryModels.cs`'s `TotalCost`/`Outstanding`/etc., the Payables dashboard tile's `DueWithin7DaysAmount`). The Partner export already redacts columns via `FieldPermissionRules.HiddenProperties` (BR-SEC-003); the Vehicle export was already designed to include no cost, finance or profit column at all, so there is nothing in it to hide.
- [!] **S6-SEC-08** — Separation-of-duties switch for approvals and adjustments · `API` · 2d · deps: S6-SEC-04 · BR-SEC-011 · OQ-19 answered: not required for Phase 1 — deferred, not built
- [x] **S6-SEC-09** — Role management screen with cloning · `UI` · 3d · deps: S6-SEC-01 · FR-SEC-001
  - The role list and detail screens already existed from S0. Added cloning (FR-SEC-001): a dialog on the role detail page creates a new tenant-owned role, then copies the source role's own currently-allowed permissions onto it (client-orchestrated — create, then `PUT .../permissions` — no new backend endpoint needed). Cloning reads a role the caller may only *view* (e.g. one of the six global templates); the copy always lands as the caller's own tenant-owned role, which they can then edit.
- [x] **S6-SEC-10** — Permission tree editor grouped by module and level, with search and select-all · `UI` · 4d · deps: S6-SEC-02 · §23B.6
  - Needed `PermissionItemModel.Level` (Interface/Operation/Field) on the wire, which the role's own permission list did not expose before (`PermissionGroupModel` had it grouped by module only) — added. The role detail screen now groups client-side by module, then by level within it; a search box filters by name/code/description; a "Select all" checkbox at both the module and the level-within-module grain, which never grants a permission the caller does not themselves hold (mirrors the existing per-checkbox rule).
- [x] **S6-SEC-11** — User management screen with role and scope assignment · `UI` · 3d · deps: S6-SEC-03 · §23B.6
  - New "Manage access" dialog on the Users screen: the roles a user holds (with remove, disabled on the last one), an "add a role" form (role + its own scope), the primary role's scope editor (`vms-branch-picker` for Own branch), and the driver-link editor (`vms-partner-picker` filtered to the Driver role). The users list itself gained a Scope column.
- [x] **S6-SEC-12** — Effective permissions viewer showing which role granted each capability · `UI` · 3d · deps: S6-SEC-04 · §23B.6
  - A dialog from the Users screen (the eye icon): the flattened union of everything a user holds, grouped by module, each permission tagged with which role(s) grant it — the "why can they see that" screen §23B.6 describes.
- [x] **S6-SEC-13** — Seed the six default role templates · `CFG` · 2d · deps: S6-SEC-02 · §23B.7
  - `TENANT_ADMIN` (already seeded, "Administrator: everything") plus five new ones matching §23B.7's table exactly: `FLEET_MANAGER`, `FINANCE_USER`, `OPERATIONS_USER`, `DRIVER` (no permissions in this phase — trip/fuel entry, its stated screens, are not part of Phase 1's built feature set), `READ_ONLY`. Left the pre-existing generic `MANAGER`/`STAFF` templates alone (unrelated bootstrap roles, still used by earlier tests).

> S6-SEC-06 and S6-SEC-07 touch code written in stages 1 to 5. The alternative is applying field permissions as each screen is built, which spreads the same work earlier. Either sequencing works; leaving it until after UAT does not.

Front end: `npm test` (24), `check:colors`, `check:contrast` green, full Angular build clean; browser check `shots/rbac-ui.mjs`, 19/19 (the six role templates listed; the permission tree's module/level grouping, search and select-all; a role clone starting with its source's permissions; the users list's Scope column; adding and removing a second role for a user; setting Own branch scope; linking a user to a Driver partner; the effective-permissions viewer showing the correct granting role). **A tenant admin cannot edit a global (platform) role** — only a Super Admin can (`RoleGuard.EnsureCanModify`, from S0) — so the permission-tree edit checks use a freshly created custom role, and cloning one of the six templates is exercised as the view-only read it actually is.

**Stage 6 progress: 12 / 13 · S6-SEC-08 `[!]` deferred (OQ-19: not required for Phase 1) — every other task done, mutation-tested and browser-verified — Stage 6 complete**

---

## Stage 7 — Notifications

Delivery channels beyond in-app are a separate FSD. This stage builds the rule engine and the subscriptions the two modules create.

- [x] **S7-NOT-01** — NotificationRule master: event type, lead days, recipients, escalation, active flag · `DB` `CFG` · 3d · deps: S0-FND-12 · source §9
- [x] **S7-NOT-02** — Subscription creation at save time for document expiry, installment due, charge due · `API` · 3d · deps: S5-DOC-04, S4-REC-05 · FR-BP-016, FR-VH-008
- [x] **S7-NOT-03** — Nightly evaluation job producing notifications against the configured lead times · `JOB` · 3d · deps: S7-NOT-01 · §23.4, §19A.6
- [x] **S7-NOT-04** — Operating time zone for the batch, so a lead time means the same day for every user · `CFG` `JOB` · 1d · deps: S7-NOT-03 · NFR-DT-06
- [x] **S7-NOT-05** — Notification rule admin screen · `UI` · 3d · deps: S7-NOT-01 · source §9
- [x] **S7-NOT-06** — In-app notification list and unread indicator · `UI` · 3d · deps: S7-NOT-03 · §23.4

No separate Subscription table: FR-BP-016/FR-VH-008's "generated at save, not at the next batch run" is met by having Document upload/renew (Stage 5), RecurringCharge create/amend (Stage 4) and vehicle activation's installment-schedule generation (Stage 3) each call the same evaluator synchronously right after their own save — a candidate is always recomputed fresh from the live source tables (`IDocumentNotificationSource`, `IVehicleNotificationSource`), the same idiom `DocumentExpiryRecalculator`/`RecurringChargeGenerator` already use, so there is nothing to keep in sync and nothing to clean up when a document is renewed or a charge is paid. S7-NOT-04 needed no code of its own: the evaluator already reads "today" through `IOperatingClock.TodayAsync`, the same NFR-DT-06 pattern Stage 4/5's jobs use.

Six event types, matching FSD §19A.6/§23.4's two tables exactly: `DocumentExpiry`, `ChargeDue`, `ChargeOverdue` (with escalation), `ChargeEnding`, `InstallmentDue`, `ItemWarrantyEnd`. "Rent due day (Rented)" is not a rule of its own — Vehicle Rent Payable is itself a recurring charge (Stage 4), so it is already `ChargeDue`. A recipient is a permission code, not a role (Stage 6 made roles fully dynamic), resolved to whoever currently holds it via a new cross-module `IUserDirectory` (implemented by the Auth module, mirroring `IPartnerDirectory`/`IVehicleDirectory`'s pattern). Deduplication is keyed on `(user, source row, event type, escalation-or-not)`, enforced by a genuine unique DB index — not only an in-memory check — because the hourly job and an admin's "run now" can race, the exact same BR-VH-030 pattern `RecurringChargeGenerator` already uses; found by mutation-testing the naive version, which silently double-notified when the escalation and ordinary recipients were the same person.

Front end: `check:colors`, `check:access`, full Angular build clean; browser check `shots/notifications-ui.mjs`, 11/11 (bell badge appears immediately for a document uploaded within its lead window with no job run — S7-NOT-02's own hook fired it; the dropdown lists it and marks it read on click, navigating to the vehicle it is about; the rule admin screen lists all six seeded rules, an edit persists, and only the ChargeOverdue rule's dialog offers escalation fields).

**Stage 7 progress: 6 / 6 · 16 of 16 days — Stage 7 complete**

---

## Stage 8 — Testing, migration and go-live

- [x] **S8-QA-01** — Automated acceptance suite covering all 24 criteria in §25 of the FSD · `QA` · 6d · deps: S6-SEC-07 · §25
- [x] **S8-QA-02** — Migration tooling for existing partner and vehicle records, with dry-run and reconciliation report · `API` `QA` · 5d · deps: S2-VH-09 · closed by the answer to OQ-09: no existing records to migrate — nothing to build
- [x] **S8-QA-03** — Bulk document upload screen for go-live loading · `UI` · 3d · deps: S5-DOC-04 · §23A.4
- [!] **S8-QA-04** — UAT support, defect triage and fixes · `QA` · 6d · deps: S8-QA-01 · — (a UAT phase with real client feedback has not happened yet; nothing to triage until it does)
- [!] **S8-QA-05** — Go-live: environment setup, master data load, user and role creation, cutover · `CFG` · 3d · deps: S8-QA-04 · — (a cutover to a real production environment is the client's own event, not something built ahead of it; see the note below)

`tests/VMS.Tests/Acceptance/AcceptanceCriteriaTests.cs`, one test per §25 row (AC-BP-001..010, AC-VH-001..014), named by its own ID so the FSD's own acceptance table can be checked against the code directly — this file's job is traceability, not new coverage the stage-specific test files don't already have more thoroughly. Writing it caught one genuine, small gap against the FSD: AC-BP-001 says a Company's CNIC is "hidden," but the General tab showed it unconditionally (just not marked mandatory) for every party type — fixed (`partner-tabs.component.ts`, wrapped in `@if (partyType !== 'Company')`), with the pre-existing `shots/partners-ui.mjs` browser check's own assertion updated to match (it had encoded the pre-fix behaviour as correct) and re-run clean (55/55). 24/24 acceptance tests pass; full suite 677/677.

S8-QA-03: `VMSFrontend/src/app/pages/admin/bulk-documents.component.ts` (`/admin/bulk-documents`, `DOC.UPLOAD`). No dedicated backend endpoint — each row is client-orchestrated onto the same single-document upload the Documents tab itself already uses (matching the precedent set by role cloning in Stage 6: reuse what already exists and is already tested, rather than duplicate its validation in a new bulk endpoint), sent one row at a time so a bad row's error names exactly that row without stopping the rest (`POST /api/payables/bulk-confirm`'s own "one bad item does not stop the batch" principle, Stage 4). New `vms-vehicle-picker` shared component (`shared/vehicle-picker.component.ts`), mirroring `vms-partner-picker`'s own server-search pattern, since a vehicle owner needed the same kind of picker a partner owner already had. Browser check `shots/bulk-docs-ui.mjs`, 10/10 (a complete row uploads and the document is genuinely readable back from the vehicle's own Documents tab afterward; an incomplete row is left alone, not silently sent; the summary line counts correctly).

**S8-QA-04 and S8-QA-05 left `[!]`, not built**, and this is a boundary of what "executing the task register" can mean, not an oversight: both describe activities that only make sense once this software meets real people and a real environment — UAT feedback from the client, an actual production server to deploy to, actual go-live master data. Everything this register calls "build" is now done (S0 through S8-QA-03: every task that produces code, a screen, an API, or a test); what is left is process that happens *around* the software, at a time the client controls, not a gap in what was built. `VMS-Phase1-GoLive-Runbook.md` (this folder) is the concretely-buildable part of S8-QA-05 — environment configuration, migration order, first-tenant and first-admin steps, a cutover checklist — written so S8-QA-05 itself becomes "follow the runbook," not a blank page, whenever the client's own go-live date arrives. S8-QA-04 genuinely needs a live UAT phase before there is anything to triage.

**Stage 8 progress: 4 / 5 (S8-QA-02 closed by OQ-09, S8-QA-04/05 deferred — not code) · 14 of 23 days**

---

## Deferred — pending client answers

Specified in the FSD but excluded from the 372-day estimate. Each becomes a task only if confirmed for V1.

- [ ] **D-01** — Merge Partners: re-point children and transactions, tombstone the duplicate · 6d · gate: OQ-06
- [ ] **D-02** — Partner import from Excel · 4d · gate: client request, currently Phase 1.1
- [ ] **D-03** — Maker-checker workflow on expenses and adjustments · 5d · gate: OQ-19
- [ ] **D-04** — Principal and markup split on lease installments · 4d · gate: OQ-07
- [ ] **D-05** — Driver salary as a vehicle-level charge with allocation · 3d · gate: OQ-14

If all five are confirmed: **+22 days**, roughly two more weeks on the critical path.

---

## Critical path

```
S0 Foundation → S2A Vehicle backend → S3 Finance → S2B Vehicle wizard → S6 RBAC → S8 QA
```

Each stage consumes something the previous one creates. The wizard's posting preview needs the posting engine; the field-permission pass needs the screens it hides fields on.

**Runs in parallel:**

- **S1B** alongside S2A once S1A's APIs are stable
- **S5** from stage 0 onward, holding only S5-DOC-12 until S4-REC-08 lands
- **S7** once S4 and S5 have created their subscriptions
- **S8-QA-02** from S2-VH-09 onward, well before UAT

One developer: ~74 weeks. Three: ~17–19 weeks, limited by the critical path rather than hands. A fourth adds little.

---

## Milestones

- [ ] **M1 — Platform ready** (S0) · Log in, permission-driven menu, lookup masters manageable
- [ ] **M2 — Partners live** (S1A, S1B) · Every partner type with multi-role, contacts, addresses, documents; duplicate detection working. **Client data entry can begin here, in parallel with the rest of the build**
- [ ] **M3 — Vehicles inducted** (S2A, S2B, S3) · Full five-step wizard including a bank lease with generated schedule; the §8 worked example produces PKR 2,150,000 after one installment
- [ ] **M4 — Payables running** (S4) · This month's due items across the fleet on one workbench, clearable in bulk
- [ ] **M5 — Compliance visible** (S5, S7) · Document register shows what expires when; reminders fire on configured lead times
- [ ] **M6 — Controlled and accepted** (S6, S8) · Each role sees only its permitted screens, operations and amounts; all 24 acceptance criteria pass

**M2 is the one to pull forward** — partner data entry is the client's largest manual effort at go-live, and it converts from sequential to parallel the moment M2 lands.

**M3 is the client's confidence point** — the §8 worked example is their own stated requirement. Demonstrate it end to end before four more stages are built on top of it.

---

## Blocked by open questions

OQ-02, OQ-03, OQ-05, OQ-07, OQ-08, OQ-10, OQ-12 and OQ-04 were answered by the user (see the FSD's open-questions log); the tasks they blocked (S1-BP-13, S1-BPU-05, S2-VHU-04, S3-FIN-06, S3-FIN-07, S0-FND-15, S4-REC-04, S5-DOC-07) are all done. OQ-15 turned out moot for this phase — S5-DOC-07 was built without it (no trip-assignment module exists yet to block). OQ-20 was answered (same role system) and S6-SEC-03 is done. OQ-19 was answered (not required for Phase 1) and S6-SEC-08 is deferred, not blocked — nothing left needs it. OQ-09 was answered (no existing records to migrate) and S8-QA-02 is closed, nothing to build. **Nothing in the plan is currently blocked by an open question.**

---

## Delivery risks

| Risk | Likelihood | Effect | Response |
| --- | --- | --- | --- |
| S2-VH-09 atomic activation harder than estimated | Medium | Slips the critical path directly | Rollback failure tests before implementation; budget the full 6d |
| Lookup masters not populated by the client in time | High | Blocks testing of every screen, not just seeding | Request the lists at M1, not go-live; ship the §24.2 seeds as defaults |
| Field-permission pass uncovers leaks in earlier screens | Medium | Rework across stages 1–5 | Apply field permissions per screen as built, not only in S6 |
| Client changes categories or roles after seeing M3 | Medium | S2-VHU-04 rework | Demo the category screens at M3 specifically to surface it early |
| Recurring charges and lease schedule produce duplicate due items | Low | Wrong payables figures, hard to spot | S4-REC-06/07 exist for this; test a financed vehicle with insurance and tracker charges |
| Estimates assume stack familiarity | Medium | Uniform overrun | Re-baseline after M1, when 46 days of actuals are known |

---

## Start here

1. Send OQ-01 to OQ-20 to the client. Flag **OQ-07, OQ-08 and OQ-10** as needed within the week.
2. Request the lookup master lists in §24.2 of the FSD, plus the branch list.
3. Begin S0-FND-01 through S0-FND-11 — none depend on any open question except OQ-10.
