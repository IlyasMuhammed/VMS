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
| S0 | Foundation | 18 | 46 | ☐ |
| S1 | Business Partner | 31 | 88 | ☐ |
| S2 | Vehicle | 32 | 105 | ☐ |
| S3 | Acquisition & finance | 14 | 46 | ☐ |
| S4 | Recurring charges | 16 | 47 | ☐ |
| S5 | Document management | 15 | 44 | ☐ |
| S6 | Role-based access | 13 | 41 | ☐ |
| S7 | Notifications | 6 | 16 | ☐ |
| S8 | QA, migration, go-live | 5 | 23 | ☐ |

---

## Stage 0 — Foundation

Nothing else can start without this. None of it is visible to the client, which is why it is usually underestimated.

- [ ] **S0-FND-01** — Solution scaffolding: project structure, DI, logging, configuration, error-handling middleware · `API` · 3d · deps: — · §24.3
- [ ] **S0-FND-02** — Database project, connection management, migration tooling, seed runner · `DB` · 2d · deps: S0-FND-01 · §24.3
- [ ] **S0-FND-03** — Authentication: login, password hashing, token issue, refresh, logout · `API` · 4d · deps: S0-FND-01 · §23B.1
- [ ] **S0-FND-04** — User and Role tables, UserRole with branch scope · `DB` · 2d · deps: S0-FND-02 · §23B.1
- [ ] **S0-FND-05** — Permission catalogue table; seed all ~55 permission codes · `DB` `CFG` · 2d · deps: S0-FND-04 · §23B.2
- [ ] **S0-FND-06** — Authorisation middleware: endpoint permission attributes, default-deny, denial logging · `API` · 4d · deps: S0-FND-05 · BR-SEC-005/006/007
- [ ] **S0-FND-07** — Field-level permission filter: server-side stripping of restricted fields from responses · `API` · 3d · deps: S0-FND-06 · BR-SEC-001/002
- [ ] **S0-FND-08** — Audit log table, append-only enforcement, change-capture interceptor with old/new values · `DB` `API` · 4d · deps: S0-FND-02 · §13.2, BR-BP-022
- [ ] **S0-FND-09** — Numbering series master, gap-free allocator inside the save transaction · `DB` `API` · 3d · deps: S0-FND-02 · §24.1
- [ ] **S0-FND-10** — File storage service: upload, virus-scan hook, SHA-256 hash, generated naming, short-lived download URL · `API` · 4d · deps: S0-FND-01 · §23A.5
- [ ] **S0-FND-11** — Date/time handling: UTC storage, client time-zone rendering, date-only business dates, ISO 8601 API contract · `API` `UI` · 3d · deps: S0-FND-01 · §24.4
- [ ] **S0-FND-12** — Generic lookup master framework: table, CRUD API, admin screen, active flag, sort order · `DB` `API` `UI` · 4d · deps: S0-FND-02 · §24.2
- [ ] **S0-FND-13** — Seed all lookup masters listed in §24.2 · `CFG` · 2d · deps: S0-FND-12 · §24.2
- [ ] **S0-FND-14** — City and Province master with mapping · `DB` `CFG` · 1d · deps: S0-FND-12 · §24.2
- [ ] **S0-FND-15** — Branch master and branch-scoped query filter · `DB` `API` · 2d · deps: S0-FND-12 · §23B.4 · ⚠ needs OQ-10 answered
- [ ] **S0-FND-16** — Frontend shell: routing, layout, permission-driven menu, auth guards · `UI` · 4d · deps: S0-FND-06 · §23B
- [ ] **S0-FND-17** — Shared UI components: data grid with server paging, filter bar, lookup picker, file upload, date picker, confirm dialog · `UI` · 5d · deps: S0-FND-16 · §9.1, §21
- [ ] **S0-FND-18** — Message resource file, validation display pattern, toast and error conventions · `UI` `API` · 2d · deps: S0-FND-17 · §12.2

> **S0-FND-07 matters more than its size suggests.** Field-level stripping is what makes the cost and profit restrictions real rather than cosmetic. Skip it here and every later screen has to re-solve it, and the exports will leak the amounts.

**Stage 0 progress: 0 / 18 · 0 of 46 days**

---

## Stage 1A — Business Partner backend

- [ ] **S1-BP-01** — BusinessPartner table: BPCode series, Party Type, identity and tax fields, status, indexes · `DB` · 3d · deps: S0-FND-09 · §5, §6
- [ ] **S1-BP-02** — BusinessPartnerRole table with effective dating and IsActive · `DB` · 2d · deps: S1-BP-01 · §5, BR-BP-001
- [ ] **S1-BP-03** — Role detail tables: BPDriverDetail, BPVendorDetail, BPCustomerDetail · `DB` · 2d · deps: S1-BP-02 · §5, §7
- [ ] **S1-BP-04** — Child tables: BPContact, BPAddress, BPBankAccount with IsPrimary constraints · `DB` · 3d · deps: S1-BP-01 · §8
- [ ] **S1-BP-05** — Create partner API: one transaction across master, roles, details, children, opening balance, audit · `API` · 5d · deps: S1-BP-03, S1-BP-04 · §10, FR-BP-013
- [ ] **S1-BP-06** — Update partner API with field-level change capture · `API` · 3d · deps: S1-BP-05 · §13.2
- [ ] **S1-BP-07** — Core field validation: CNIC/NTN format, mobile format, conditional mandatories by role · `API` · 3d · deps: S1-BP-05 · §6, §12.2
- [ ] **S1-BP-08** — Uniqueness for CNIC, NTN, STRN, licence number with friendly messages · `API` `DB` · 2d · deps: S1-BP-07 · BR-BP-013
- [ ] **S1-BP-09** — Duplicate detection service: exact hard checks plus name-similarity scoring · `API` · 4d · deps: S1-BP-08 · §12.1
- [ ] **S1-BP-10** — Soft-duplicate override capture and audit · `API` · 1d · deps: S1-BP-09 · BR-BP-021
- [ ] **S1-BP-11** — Role add/remove with in-use guard and role change log · `API` · 4d · deps: S1-BP-06 · BR-BP-010/011/016
- [ ] **S1-BP-12** — Status change API: deactivate, reactivate, blacklist with reason and open-record guard · `API` · 3d · deps: S1-BP-06 · §13.1, BR-BP-014
- [ ] **S1-BP-13** — Opening balance posting at creation; field lock after save · `API` · 2d · deps: S1-BP-05 · BR-BP-018 · ⚠ needs OQ-02 answered
- [ ] **S1-BP-14** — Partner list API: search across code, name, CNIC, NTN, mobile; filters; server paging and sorting · `API` · 4d · deps: S1-BP-05 · §9.1
- [ ] **S1-BP-15** — Role-filtered picker API used by every partner dropdown elsewhere · `API` · 2d · deps: S1-BP-02 · FR-BP-003, BR-BP-020
- [ ] **S1-BP-16** — Linked Vehicles read API for the partner screen · `API` · 2d · deps: S2-VH-03 · §9.2
- [ ] **S1-BP-17** — Partner export to Excel honouring field permissions · `API` · 2d · deps: S1-BP-14, S0-FND-07 · BR-SEC-003

**Stage 1A progress: 0 / 17 · 0 of 47 days**

---

## Stage 1B — Business Partner frontend

- [ ] **S1-BPU-01** — Partner list screen: columns, quick search, filters, row actions, empty state · `UI` · 4d · deps: S1-BP-14, S0-FND-17 · §9.1
- [ ] **S1-BPU-02** — Partner form shell: tab layout, header with code/name/role chips/status, dirty-state guard · `UI` · 3d · deps: S0-FND-17 · §9.2, FR-BP-011
- [ ] **S1-BPU-03** — General tab: core fields, Party Type switching CNIC/NTN, live mandatory indicators · `UI` · 4d · deps: S1-BPU-02 · §6, FR-BP-009
- [ ] **S1-BPU-04** — Role multi-select driving panel visibility without reload · `UI` · 2d · deps: S1-BPU-03 · FR-BP-008
- [ ] **S1-BPU-05** — Role panels ×9: Driver, Workshop, Bank, Vendor, Customer, Running Customer, Tracker, Body Maker, Fuel Card · `UI` · 6d · deps: S1-BPU-04 · §7 · ⚠ needs OQ-03 answered · *split across two developers by role group*
- [ ] **S1-BPU-06** — Duplicate warning panel with candidate comparison and override tick · `UI` · 3d · deps: S1-BP-09 · §12.1
- [ ] **S1-BPU-07** — Contacts and Addresses grids with inline add, edit, primary toggle · `UI` · 3d · deps: S1-BPU-02 · §8.1, §8.2
- [ ] **S1-BPU-08** — Bank Accounts grid with IBAN validation · `UI` · 2d · deps: S1-BPU-02 · §8.3
- [ ] **S1-BPU-09** — Client-side holding of child rows until first save, then single-transaction submit · `UI` · 3d · deps: S1-BPU-07 · FR-BP-010
- [ ] **S1-BPU-10** — Compact partner dialog for inline creation from any picker, role pre-selected and locked · `UI` · 3d · deps: S1-BPU-03 · FR-BP-014, §10
- [ ] **S1-BPU-11** — Status change dialog with reason capture and blocking-record list · `UI` · 2d · deps: S1-BP-12 · §13.1
- [ ] **S1-BPU-12** — History tab: audit entries, role change log, status change log · `UI` · 3d · deps: S0-FND-08 · §9.2
- [ ] **S1-BPU-13** — Linked Vehicles tab · `UI` · 2d · deps: S1-BP-16 · §9.2
- [ ] **S1-BPU-14** — Save and New retaining Party Type, roles, city, branch · `UI` · 1d · deps: S1-BPU-09 · FR-BP-015

**Stage 1B progress: 0 / 14 · 0 of 41 days**

---

## Stage 2A — Vehicle backend

- [ ] **S2-VH-01** — Vehicle table: identity, technical, operational fields, status, current-category copies, indexes · `DB` · 4d · deps: S0-FND-09 · §15, §16
- [ ] **S2-VH-02** — Registration number normalisation; filtered unique index across non-disposed vehicles · `DB` `API` · 2d · deps: S2-VH-01 · BR-VH-017
- [ ] **S2-VH-03** — VehicleBusinessPartnerRelation with filtered unique index on one open relation · `DB` · 3d · deps: S2-VH-01, S1-BP-01 · BR-VH-002
- [ ] **S2-VH-04** — VehicleLifecycleHistory table and status transition service · `DB` `API` · 3d · deps: S2-VH-01 · §23.1
- [ ] **S2-VH-05** — VehicleAttachedItem table with attach, detach, transfer chain · `DB` `API` · 4d · deps: S2-VH-01 · §20.1, BR-VH-013
- [ ] **S2-VH-06** — OdometerReading table with floor validation against the opening reading · `DB` `API` · 2d · deps: S2-VH-01 · BR-VH-025
- [ ] **S2-VH-07** — DriverVehicleAssignment with single-active-vehicle guard and release prompt · `DB` `API` · 3d · deps: S2-VH-01, S1-BP-03 · BR-VH-022
- [ ] **S2-VH-08** — Save Draft API: vehicle row only, no postings · `API` · 2d · deps: S2-VH-01 · FR-VH-012, BR-VH-012
- [ ] **S2-VH-09** — Activate API: atomic write across vehicle, relation, lifecycle, postings, items, documents · `API` · 6d · deps: S2-VH-03, S3-FIN-06 · FR-VH-012, §19 · **highest-risk task — write rollback failure tests first**
- [ ] **S2-VH-10** — Activation pre-check returning the checklist of what is missing · `API` · 3d · deps: S2-VH-09 · BR-VH-019
- [ ] **S2-VH-11** — Category-specific validation for Shared, Rented, Customer Arrangement · `API` · 4d · deps: S2-VH-03 · §17.1–17.3
- [ ] **S2-VH-12** — Change Category: close relation, open new, lifecycle row, guards on open finance · `API` · 4d · deps: S2-VH-03 · BR-VH-005/006/007
- [ ] **S2-VH-13** — Dispose actions: retire, sell, transfer with counterparty, amount, reason · `API` · 3d · deps: S2-VH-04 · BR-VH-023
- [ ] **S2-VH-14** — Vehicle list API: columns, filters, quick search across reg/chassis/engine/code · `API` · 4d · deps: S2-VH-01 · §21
- [ ] **S2-VH-15** — Vehicle financial summary API computing paid-to-date and outstanding as aggregates · `API` · 3d · deps: S3-FIN-07 · BR-VH-003, §19
- [ ] **S2-VH-16** — Fuel card uniqueness and reassignment prompt · `API` · 2d · deps: S2-VH-01 · BR-VH-026

**Stage 2A progress: 0 / 16 · 0 of 52 days**

---

## Stage 2B — Vehicle frontend

- [ ] **S2-VHU-01** — Wizard framework: five steps, progress bar, per-step error counts, step gating · `UI` · 4d · deps: S0-FND-17 · §21, FR-VH-009
- [ ] **S2-VHU-02** — Step 1 Vehicle details: identity and technical fields, conditional capacity mandatories · `UI` · 4d · deps: S2-VHU-01 · §16.1, §16.2
- [ ] **S2-VHU-03** — Copy from existing vehicle pre-fill · `UI` · 2d · deps: S2-VHU-02 · FR-VH-014
- [ ] **S2-VHU-04** — Step 2 Ownership: category selector driving five conditional field sets · `UI` · 5d · deps: S2-VHU-01, S2-VH-11 · §17 · ⚠ needs OQ-05 answered
- [ ] **S2-VHU-05** — Counterparty picker filtered by role, with inline partner creation · `UI` · 2d · deps: S2-VHU-04, S1-BPU-10 · FR-VH-013, BR-VH-020
- [ ] **S2-VHU-06** — Step 4 Items and documents grids · `UI` · 4d · deps: S2-VHU-01 · §20
- [ ] **S2-VHU-07** — Step 5 Review: read-only summary plus the list of postings about to be written · `UI` · 4d · deps: S2-VHU-01, S3-FIN-07 · FR-VH-011
- [ ] **S2-VHU-08** — Save Draft and Activate actions with the missing-items checklist · `UI` · 3d · deps: S2-VH-10 · FR-VH-012, BR-VH-019
- [ ] **S2-VHU-09** — Vehicle list screen: columns, filters, quick search, row actions · `UI` · 4d · deps: S2-VH-14 · §21
- [ ] **S2-VHU-10** — Vehicle screen shell: header, tab layout, financial summary panel · `UI` · 5d · deps: S2-VH-15 · §19
- [ ] **S2-VHU-11** — Change Category dialog with dated close and open · `UI` · 3d · deps: S2-VH-12 · BR-VH-005
- [ ] **S2-VHU-12** — Attached Items tab with attach, detach, transfer · `UI` · 3d · deps: S2-VH-05 · §20.1
- [ ] **S2-VHU-13** — Driver assignment with release prompt · `UI` · 2d · deps: S2-VH-07 · BR-VH-022
- [ ] **S2-VHU-14** — Dispose dialogs: retire, sell, transfer · `UI` · 3d · deps: S2-VH-13 · BR-VH-023
- [ ] **S2-VHU-15** — Vehicle History tab replaying the induction as one dated group · `UI` · 3d · deps: S0-FND-08 · §23.2
- [ ] **S2-VHU-16** — Vehicle export honouring field permissions · `UI` `API` · 2d · deps: S2-VH-14, S0-FND-07 · BR-SEC-003

**Stage 2B progress: 0 / 16 · 0 of 53 days**

---

## Stage 3 — Acquisition, finance and opening postings

Delivers the §8 worked example: PKR 2,000,000 at creation plus a PKR 150,000 installment showing PKR 2,150,000 paid, with no stored total anywhere.

- [ ] **S3-FIN-01** — VehicleTransaction table: type, amount, date, partner, reference, source, IsSystemGenerated, attachment · `DB` · 3d · deps: S2-VH-01 · §19
- [ ] **S3-FIN-02** — VehicleFinanceAgreement table with terms, one-active-per-vehicle guard · `DB` · 3d · deps: S2-VH-01, S1-BP-01 · §18.2, BR-VH-004
- [ ] **S3-FIN-03** — VehicleInstallment table with status and paid tracking · `DB` · 2d · deps: S3-FIN-02 · §18.3
- [ ] **S3-FIN-04** — Acquisition block API and validation, including amount-paid ceiling · `API` · 3d · deps: S3-FIN-01 · §18.1, BR-VH-021
- [ ] **S3-FIN-05** — Finance block API with down-payment equality check and reconciliation warning · `API` · 3d · deps: S3-FIN-02 · BR-VH-009/010
- [ ] **S3-FIN-06** — Installment schedule generator: frequency stepping, month-end fallback, residual row · `API` · 4d · deps: S3-FIN-03 · FR-VH-004 · ⚠ needs OQ-07 answered
- [ ] **S3-FIN-07** — Opening posting engine: acquisition, initial payment, registration cost, deposit, item costs · `API` · 5d · deps: S3-FIN-04, S3-FIN-06 · §19 · ⚠ needs OQ-08 answered
- [ ] **S3-FIN-08** — Derived totals service: paid-to-date, outstanding, total payable · `API` · 3d · deps: S3-FIN-07 · FR-VH-006, BR-VH-003 · *test directly from AC-VH-003/004/005*
- [ ] **S3-FIN-09** — Record installment payment API updating schedule and posting a transaction · `API` · 3d · deps: S3-FIN-03 · source §8
- [ ] **S3-FIN-10** — Adjustment and reversal route with reason, audit, no physical delete · `API` · 4d · deps: S3-FIN-07 · BR-VH-011, source §13
- [ ] **S3-FIN-11** — Step 3 UI: acquisition fields, finance block, live total payable · `UI` · 4d · deps: S3-FIN-05 · §18
- [ ] **S3-FIN-12** — Schedule preview grid with editable due dates before save · `UI` · 3d · deps: S3-FIN-06 · FR-VH-005
- [ ] **S3-FIN-13** — Vehicle Finance tab: agreement summary, schedule, payment history, outstanding · `UI` · 4d · deps: S3-FIN-08 · source §5
- [ ] **S3-FIN-14** — Record installment payment dialog with receipt upload · `UI` · 2d · deps: S3-FIN-09 · source §7

**Stage 3 progress: 0 / 14 · 0 of 46 days**

---

## Stage 4 — Recurring charges

- [ ] **S4-REC-01** — VehicleRecurringCharge table with effective dating for amount changes · `DB` · 3d · deps: S2-VH-01 · §19A.1, BR-VH-033
- [ ] **S4-REC-02** — VehicleRecurringChargeEntry table with period key and unique constraint · `DB` · 2d · deps: S4-REC-01 · BR-VH-030
- [ ] **S4-REC-03** — Charge configuration API with payee role filtering and posting-mode rules · `API` · 4d · deps: S4-REC-01 · §19A.1, BR-VH-027
- [ ] **S4-REC-04** — Auto-post permission gate and fixed-amount-only restriction · `API` · 2d · deps: S4-REC-03, S0-FND-06 · BR-VH-027/028 · ⚠ needs OQ-12 answered
- [ ] **S4-REC-05** — Nightly generation job: idempotent on (charge, period), lead-day windowing · `JOB` · 5d · deps: S4-REC-02 · BR-VH-030
- [ ] **S4-REC-06** — Bank installment surfacing: read VehicleInstallment as Due, no parallel schedule · `JOB` `API` · 3d · deps: S4-REC-05, S3-FIN-03 · BR-VH-029
- [ ] **S4-REC-07** — Duplicate-charge guard blocking a manual Bank Installment charge on a financed vehicle · `API` · 1d · deps: S4-REC-06 · VAL-VH-019
- [ ] **S4-REC-08** — Confirm payment API: post one transaction, retain expected vs actual, link back · `API` · 4d · deps: S4-REC-02, S3-FIN-01 · BR-VH-031
- [ ] **S4-REC-09** — Waive and cancel actions with reason and audit · `API` · 2d · deps: S4-REC-08 · §19A.4
- [ ] **S4-REC-10** — Overdue transition job and escalation flagging · `JOB` · 2d · deps: S4-REC-05 · §19A.4
- [ ] **S4-REC-11** — End-dating cascade on vehicle disposal and category change · `API` · 3d · deps: S4-REC-01, S2-VH-12 · BR-VH-034/035
- [ ] **S4-REC-12** — Recurring Charges grid in wizard step 4 and on the vehicle screen · `UI` · 4d · deps: S4-REC-03 · §19A.5
- [ ] **S4-REC-13** — Due and Payments panel on the vehicle screen · `UI` · 3d · deps: S4-REC-08 · §19A.5
- [ ] **S4-REC-14** — Payables Due workbench: fleet-wide list, filters, sorting · `UI` · 4d · deps: S4-REC-08 · FR-VH-016
- [ ] **S4-REC-15** — Bulk confirm with common payment date and mode · `UI` `API` · 3d · deps: S4-REC-14 · §19A.5
- [ ] **S4-REC-16** — Dashboard tile: due within 7 days, overdue count and total · `UI` · 2d · deps: S4-REC-14 · §19A.5

> **S4-REC-05 and S4-REC-06 together are the subtle part.** The generation job must treat the lease schedule as a source it *reads*, not a schedule it owns — otherwise the fleet ends up with two competing installment records per month. Test with a financed vehicle that also carries insurance and tracker charges.

**Stage 4 progress: 0 / 16 · 0 of 47 days**

---

## Stage 5 — Document management

- [ ] **S5-DOC-01** — DocumentType master: applies-to, expirable, validity, periodic, lead days, mandatory, retention · `DB` `CFG` · 3d · deps: S0-FND-12 · §23A.1
- [ ] **S5-DOC-02** — Seed the twelve document types with their periodicity settings · `CFG` · 1d · deps: S5-DOC-01 · §23A.2
- [ ] **S5-DOC-03** — Document table serving both owners: version number, IsCurrent, supersedes link, status · `DB` · 4d · deps: S5-DOC-01, S0-FND-10 · §23A.3
- [ ] **S5-DOC-04** — Upload API: type-driven validation, format and size checks, hash, scan, audit · `API` · 4d · deps: S5-DOC-03 · §23A.5, BR-DOC-007
- [ ] **S5-DOC-05** — Renewal API: create version n+1, flip IsCurrent in one transaction · `API` · 3d · deps: S5-DOC-04 · BR-DOC-001
- [ ] **S5-DOC-06** — Expiry computation job: nightly recalculation to Active / Expiring Soon / Expired · `JOB` · 2d · deps: S5-DOC-03 · BR-DOC-003
- [ ] **S5-DOC-07** — Mandatory-document check feeding vehicle activation and partner save · `API` · 2d · deps: S5-DOC-03, S2-VH-10 · BR-VH-015, BR-BP-005 · ⚠ needs OQ-04 and OQ-15 answered
- [ ] **S5-DOC-08** — Reject action with reason, excluded from mandatory satisfaction · `API` · 2d · deps: S5-DOC-04 · BR-DOC-008
- [ ] **S5-DOC-09** — Retention job removing superseded versions past their type's retention, with a log · `JOB` · 2d · deps: S5-DOC-03 · BR-DOC-002
- [ ] **S5-DOC-10** — Download permission split and audited download endpoint · `API` · 2d · deps: S5-DOC-04, S0-FND-06 · §23A.5
- [ ] **S5-DOC-11** — Documents tab for partner and vehicle: current by type, status chips, history toggle · `UI` · 5d · deps: S5-DOC-04 · §23A.4
- [ ] **S5-DOC-12** — Renew dialog with pre-filled expiry and optional expense posting linked to a recurring charge · `UI` · 4d · deps: S5-DOC-05, S4-REC-08 · BR-DOC-005/006 · **get the link right or the premium appears twice in the vehicle P&L**
- [ ] **S5-DOC-13** — Document Register screen with full filter set · `UI` · 4d · deps: S5-DOC-06 · §23A.4
- [ ] **S5-DOC-14** — Missing Documents report · `UI` `API` · 3d · deps: S5-DOC-07 · §23A.4
- [ ] **S5-DOC-15** — Expiry Calendar month view · `UI` · 3d · deps: S5-DOC-06 · §23A.4

**Stage 5 progress: 0 / 15 · 0 of 44 days**

---

## Stage 6 — Role-based access control

Enforcement primitives were built in S0-FND-05 to S0-FND-07. This stage delivers the configuration surface and applies permissions across every screen already built.

- [ ] **S6-SEC-01** — Role CRUD API with system-role protection and last-admin guard · `API` · 3d · deps: S0-FND-04 · BR-SEC-008
- [ ] **S6-SEC-02** — RolePermission assignment API with audit of every grant and revoke · `API` · 3d · deps: S6-SEC-01 · BR-SEC-010
- [ ] **S6-SEC-03** — User CRUD, role assignment, branch scope, link to a Driver partner · `API` · 4d · deps: S6-SEC-01, S1-BP-03 · §23B.1 · ⚠ needs OQ-20 answered
- [ ] **S6-SEC-04** — Effective permission resolution: union across roles, session re-evaluation on change · `API` · 3d · deps: S6-SEC-02 · BR-SEC-009
- [ ] **S6-SEC-05** — Data scope filters applied to partner, vehicle and transaction queries · `API` · 4d · deps: S0-FND-15, S6-SEC-03 · §23B.4
- [ ] **S6-SEC-06** — Apply field permissions to every existing response: cost, finance, profit, salary, credit · `API` · 4d · deps: S0-FND-07, S3-FIN-08 · BR-SEC-001/002
- [ ] **S6-SEC-07** — Derived-value suppression: hide totals, charts and exports that would reveal a hidden field · `API` · 3d · deps: S6-SEC-06 · BR-SEC-002
- [ ] **S6-SEC-08** — Separation-of-duties switch for approvals and adjustments · `API` · 2d · deps: S6-SEC-04 · BR-SEC-011 · ⚠ needs OQ-19 answered
- [ ] **S6-SEC-09** — Role management screen with cloning · `UI` · 3d · deps: S6-SEC-01 · FR-SEC-001
- [ ] **S6-SEC-10** — Permission tree editor grouped by module and level, with search and select-all · `UI` · 4d · deps: S6-SEC-02 · §23B.6
- [ ] **S6-SEC-11** — User management screen with role and scope assignment · `UI` · 3d · deps: S6-SEC-03 · §23B.6
- [ ] **S6-SEC-12** — Effective permissions viewer showing which role granted each capability · `UI` · 3d · deps: S6-SEC-04 · §23B.6
- [ ] **S6-SEC-13** — Seed the six default role templates · `CFG` · 2d · deps: S6-SEC-02 · §23B.7

> S6-SEC-06 and S6-SEC-07 touch code written in stages 1 to 5. The alternative is applying field permissions as each screen is built, which spreads the same work earlier. Either sequencing works; leaving it until after UAT does not.

**Stage 6 progress: 0 / 13 · 0 of 41 days**

---

## Stage 7 — Notifications

Delivery channels beyond in-app are a separate FSD. This stage builds the rule engine and the subscriptions the two modules create.

- [ ] **S7-NOT-01** — NotificationRule master: event type, lead days, recipients, escalation, active flag · `DB` `CFG` · 3d · deps: S0-FND-12 · source §9
- [ ] **S7-NOT-02** — Subscription creation at save time for document expiry, installment due, charge due · `API` · 3d · deps: S5-DOC-04, S4-REC-05 · FR-BP-016, FR-VH-008
- [ ] **S7-NOT-03** — Nightly evaluation job producing notifications against the configured lead times · `JOB` · 3d · deps: S7-NOT-01 · §23.4, §19A.6
- [ ] **S7-NOT-04** — Operating time zone for the batch, so a lead time means the same day for every user · `CFG` `JOB` · 1d · deps: S7-NOT-03 · NFR-DT-06
- [ ] **S7-NOT-05** — Notification rule admin screen · `UI` · 3d · deps: S7-NOT-01 · source §9
- [ ] **S7-NOT-06** — In-app notification list and unread indicator · `UI` · 3d · deps: S7-NOT-03 · §23.4

**Stage 7 progress: 0 / 6 · 0 of 16 days**

---

## Stage 8 — Testing, migration and go-live

- [ ] **S8-QA-01** — Automated acceptance suite covering all 24 criteria in §25 of the FSD · `QA` · 6d · deps: S6-SEC-07 · §25
- [ ] **S8-QA-02** — Migration tooling for existing partner and vehicle records, with dry-run and reconciliation report · `API` `QA` · 5d · deps: S2-VH-09 · ⚠ needs OQ-09 answered
- [ ] **S8-QA-03** — Bulk document upload screen for go-live loading · `UI` · 3d · deps: S5-DOC-04 · §23A.4
- [ ] **S8-QA-04** — UAT support, defect triage and fixes · `QA` · 6d · deps: S8-QA-01 · —
- [ ] **S8-QA-05** — Go-live: environment setup, master data load, user and role creation, cutover · `CFG` · 3d · deps: S8-QA-04 · —

**Stage 8 progress: 0 / 5 · 0 of 23 days**

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

| OQ | Question | Blocks | Needed by |
| --- | --- | --- | --- |
| OQ-02 | Opening balances at go-live, or everyone starts at zero? | S1-BP-13 | Start of S1A |
| OQ-03 | Role-specific fields for Bank, Workshop, Body Maker, Tracker, Fuel Card? | S1-BPU-05 | Start of S1B |
| OQ-04 | Licence scan — block or warn? | S5-DOC-07 | Start of S5 |
| OQ-05 | Shared-vehicle expense split at entry or month-end? | S2-VHU-04 | Start of S2B |
| **OQ-07** | **Principal/markup split on installments?** | S3-FIN-06 | **Start of S3 — changes the schedule table** |
| **OQ-08** | **Down payment treatment for P&L?** | S3-FIN-07 | **Start of S3 — changes the posting engine** |
| OQ-09 | Records to migrate, and in what format? | S8-QA-02 | 4 weeks before go-live |
| **OQ-10** | **Branches, and default branch restriction?** | S0-FND-15 | **Before S0 finishes — scoping is wired into the query layer** |
| OQ-12 | Auto-post defaults? | S4-REC-04 | Start of S4 |
| OQ-15 | Expired document blocks trip assignment? | S5-DOC-07 | Start of S5 |
| OQ-19 | Maker-checker, and above what amount? | S6-SEC-08 | Start of S6 |
| OQ-20 | Driver app users in the same role system? | S6-SEC-03 | Start of S6 |

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
