# VMS — Trip, Billing, Invoicing & Customer Ledger: Claude Code Task Register

Spec: `docs/vms/VMS-FSD.md` (FSD v1.1, 25-Sep-2026). Section numbers below (`§`) refer to it.
Scope: Phase 1 build of the trip-to-cash module with Customer Ledger and payment tracking, inside the existing SCM/VMS codebase.

---

## How Claude Code works this register (read first, every session)

You run this register **autonomously**. Do not stop to ask for confirmation between tasks. Stop only for the reasons listed under *When to stop*.

### Loop

1. Read this file top to bottom and `docs/vms/VMS-FSD.md` sections referenced by the task you pick.
2. Pick the **first** task in document order whose `Status` is `TODO` and whose every `Depends on` task is `DONE`.
3. Set its `Status` to `IN_PROGRESS` in this file.
4. Implement it following the repo's existing conventions (architecture, naming, ORM/migrations, test framework, lint). Reuse existing entities (`Vehicle`, `BusinessPartner`, `DriverVehicleAssignment`, `VehicleTransaction`, auth, RBAC, audit, blob storage) instead of duplicating them (§8, §46).
5. Write or update automated tests covering the task's **Acceptance** items. Run the full relevant test suite and linters. Fix failures.
6. Set `Status` to `DONE`, fill `Done on` (YYYY-MM-DD) and `Notes` (files touched, migration names, anything a reviewer must know). Tick the acceptance boxes.
7. Commit with message `VMS <task id>: <task title>` (one commit per task; do not push unless the repo's workflow says so).
8. Go back to step 2.

### When a task needs input

- If a task is marked `Needs input: YES` and the matching question in **Questions for the user** has no answer, set `Status` to `BLOCKED_INPUT` and continue with the next available task.
- If you discover a gap while building (the FSD is silent or contradictory and the choice is hard to reverse, e.g. a data format, a money rule, a customer-visible layout), add a question to **Questions for the user** with the task id, set the task `BLOCKED_INPUT`, and move on.
- If the FSD marks something **Recommended Design**, build it as written — that is not a gap.
- Never invent values for open client items (invoice number format, customer templates, bank accounts, opening balances, GPS provider).
- When the user writes an answer under a question, change the task back to `TODO` on your next pass.

### When to stop

Stop and report only when:

- no task is `TODO` with all dependencies `DONE` (everything left is `DONE`, `BLOCKED_INPUT`, or waits on a blocked task); or
- the build, test suite or migrations are broken in a way you cannot fix without a decision from the user; or
- a step would be destructive outside this module (dropping or rewriting existing tables/data, changing shared auth, force-pushing).

Your final report lists: tasks completed this session, tasks blocked with their questions, and the next task that will start once they are answered.

### Non-negotiable rules from the FSD (apply to every task)

- Customer is its own master; trips and invoices use `CustomerId`, never `BusinessPartnerId` (§10).
- History is immutable: trips snapshot rates; invoices snapshot lines, bill-to address, tax lines, template version (§46.1, §49 #65).
- No physical deletes of financial or operational history; use status/`IsActive`/reversal (§45).
- Ledger entries are append-only and posted in the **same DB transaction** as their cause (§40A).
- Money is `decimal(18,2)`, two decimals, half-up rounding (§13A, §35).
- Dates stored UTC; business dates as `date` (§1).
- Every mutating API checks permissions server-side and writes audit (§44, §45).

### Status values

`TODO` · `IN_PROGRESS` · `DONE` · `BLOCKED_INPUT`

---

## Questions for the user

Answer directly under each question (replace `_answer:_` text). Claude Code re-reads this on every pass.

| Q | For task | Question | Answer |
| --- | --- | --- | --- |
| Q1 | CC-26 | Invoice number format: prefix, year, running digits, per-customer prefix, yearly reset? (e.g. `INV-2026-00001`) (§58 #9) | _answer:_ |
| Q2 | CC-27 | Customer invoice samples/proformas and addenda for customers needing their own layout (§15, §58 #18). Until provided, only the standard layout is built. | _answer:_ |
| Q3 | CC-31 | Company bank accounts that receive customer payments (bank, branch, title, last 4 digits) for seed data (§37). | _answer:_ |
| Q4 | CC-40 | Opening balances per customer (amount, Dr/Cr, currency) and go-live cut-off date (§40A L16). | _answer:_ |
| Q5 | CC-45 | Driver app technology (PWA / native Android / iOS) and login method (OTP or PIN); may drivers start a trip before office review? (§43, §58 #17) | _answer:_ |

---

## Task summary

| ID | Title | Depends on | Status |
| --- | --- | --- | --- |
| CC-00 | Repo discovery and module plan | — | DONE |
| CC-01 | Module scaffold, permissions catalogue, audit hooks | CC-00 | TODO |
| CC-02 | Currency master and tenant currency settings | CC-01 | TODO |
| CC-03 | Customer master | CC-01 | TODO |
| CC-04 | Customer contacts and billing addresses | CC-03 | TODO |
| CC-05 | Customer billing configuration | CC-03, CC-02 | TODO |
| CC-06 | Customer tax/deduction rules with auto-inactivation | CC-03 | TODO |
| CC-07 | Customer invoice templates (registry + versioning) | CC-03 | TODO |
| CC-08 | City master | CC-01 | TODO |
| CC-09 | Route and route stops | CC-08 | TODO |
| CC-10 | Trip configurations, stops, allowed vehicles | CC-09, CC-03 | TODO |
| CC-11 | Trip rates: effective dating, one-day split, open-ended auto-close | CC-10, CC-02 | TODO |
| CC-12 | Trip entity, numbering, fixed trip creation with rate snapshot | CC-11 | TODO |
| CC-13 | Open trips and Other Location | CC-12 | TODO |
| CC-14 | Driver default and override | CC-12 | TODO |
| CC-15 | Trip lifecycle state machine, completion date, active flag | CC-12 | TODO |
| CC-16 | Trip events, documents, POD, issues | CC-15 | TODO |
| CC-17 | Rate re-resolution and re-pricing | CC-12 | TODO |
| CC-18 | Fuel cards and assignments | CC-01 | TODO |
| CC-19 | Trip fuel | CC-15, CC-18 | TODO |
| CC-20 | Trip expenses with approval | CC-15 | TODO |
| CC-21 | Trip income (billable flag) | CC-15 | TODO |
| CC-22 | Trip operational P&L | CC-19, CC-20, CC-21 | TODO |
| CC-23 | Invoice schema, versioning fields, trip link lock | CC-05, CC-06, CC-07, CC-15 | TODO |
| CC-24 | Eligible-trip search and overlap check | CC-23 | TODO |
| CC-25 | Invoice creation: lines, adjustments, deductions, concurrency | CC-24 | TODO |
| CC-26 | Invoice numbering | CC-25 | TODO |
| CC-27 | Invoice PDF rendering (standard layout; customer layouts later) | CC-25 | TODO |
| CC-28 | Invoice evidence: standard layout, vehicle pagination | CC-25 | TODO |
| CC-29 | Invoice submit and cancel (no approval step) | CC-25, CC-28 | TODO |
| CC-30 | Customer Ledger core and submit postings | CC-29 | TODO |
| CC-31 | Bank accounts and payment receipts | CC-30 | TODO |
| CC-32 | Payment reversal | CC-31 | TODO |
| CC-33 | Write-off and discount | CC-31 | TODO |
| CC-34 | Advances on Open trips | CC-30, CC-13 | TODO |
| CC-35 | Invoice regeneration and overlap replacement | CC-31, CC-34 | TODO |
| CC-36 | Payment transfer on regeneration | CC-35 | TODO |
| CC-37 | Carry forward and refund of customer credit | CC-36 | TODO |
| CC-38 | Ledger statement, invoice ledger, customer balances APIs | CC-30 | TODO |
| CC-39 | Ledger reconciliation job and period lock | CC-30 | TODO |
| CC-40 | Opening balances import | CC-30 | TODO |
| CC-41 | Ledger and receivables reports (LED-01..15) | CC-38, CC-33, CC-34, CC-37 | TODO |
| CC-42 | Trip, invoice, P&L, master and audit reports | CC-22, CC-28, CC-35 | TODO |
| CC-43 | Back-office UI: masters and trips | CC-17, CC-22 | TODO |
| CC-44 | Back-office UI: invoicing, payments, ledger | CC-37, CC-38, CC-29 | TODO |
| CC-45 | Driver app API and trip creation review queue | CC-16, CC-19, CC-20 | TODO |
| CC-46 | Acceptance test suite AC-01..AC-67 and hardening | CC-41, CC-42, CC-44, CC-45, CC-39, CC-32 | TODO |

---

## Tasks

### CC-00 — Repo discovery and module plan
- **Status:** DONE · **Depends on:** — · **Needs input:** NO · **FSD:** §8, §46, §47, §54
- **Scope:** Identify stack (language, framework, ORM, migration tool, DB engine, test runner, UI framework, auth/RBAC/audit/blob services) and where existing VMS entities live. Decide module/folder placement.
- **Deliverables:** `docs/vms/IMPLEMENTATION-NOTES.md` with stack, folder layout, mapping of FSD entities to existing tables, conventions to follow, and any deviations from FSD §46 needed by the stack (e.g. filtered unique index equivalent if not SQL Server).
- **Acceptance:**
  - [x] Notes list reused entities and how FKs attach to them.
  - [x] Test command and lint command recorded and run green on the untouched repo.
- **Done on:** 2026-09-25 · **Notes:** Written as `Document/VMS-TripBilling-Implementation-Notes.md` (this repo's `Document/` convention used instead of `docs/vms/` — see that file's own "path deviations" note; nothing under `docs/` exists anywhere in this repo). Backend confirmed green 677/677 and frontend suites green as of 2026-09-24 (pre-existing verification, re-confirmed applicable). Key decisions recorded there: one new module `VMS.Modules.Trips` (schema `trp`, not several modules — the ledger's same-transaction rule needs one DbContext); reused entities/interfaces mapped (`IPartnerDirectory`, `IVehicleDirectory`; no separate DriverVehicleAssignment table — `Vehicle.DefaultDriverId` already is it); three documented, non-breaking deviations from FSD §47.1's literal API conventions (body `rowVersion` kept instead of `If-Match` header; new `IIdempotencyStore` built as specified, scoped to this module's own schema; `ApiResponse` gets additive nullable `Code`/`CorrelationId` fields rather than a second error-shape pipeline); permission codes mapped to the existing `PermissionCodes` catalog convention (`TRP_*`, one generic `TRP_REPORT_VIEW` rather than 80 per-report permissions).

### CC-01 — Module scaffold, permissions catalogue, audit hooks
- **Status:** TODO · **Depends on:** CC-00 · **Needs input:** NO · **FSD:** §44, §45, §47.1
- **Scope:** Module skeleton, API conventions (error shape, codes, `If-Match` row version, `Idempotency-Key` store), permission constants for every action in §44 (incl. `Payment.Advance`, `Payment.WriteOff`, `Payment.Discount`, `Payment.CarryForward`, `Payment.Refund`, `Trip.Review`, `Ledger.*`), audit interceptor writing AuditLog rows with CorrelationId.
- **Acceptance:**
  - [ ] Unauthorized call returns 403 with FSD error shape.
  - [ ] Replayed POST with same Idempotency-Key returns the first response.
  - [ ] Stale `If-Match` returns 409 `CONCURRENCY_CONFLICT`.
  - [ ] AC-50: every mutating call writes an audit row (user, time, old/new, reason); re-verified per module in CC-46.
- **Done on:** · **Notes:**

### CC-02 — Currency master and tenant currency settings
- **Status:** TODO · **Depends on:** CC-01 · **Needs input:** NO · **FSD:** §13A
- **Scope:** `Currency`, `ExchangeRate`, tenant `BaseCurrencyCode` (seed PKR) and `MultiCurrencyEnabled` (default off). Helper to stamp `CurrencyCode`, `ExchangeRate`, `BaseAmount` on transactions.
- **Acceptance:**
  - [ ] AC-64, AC-65 (API level).
  - [ ] Base currency change blocked once transactions exist; multi-currency off blocked while foreign-currency open items exist.
- **Done on:** · **Notes:**

### CC-03 — Customer master
- **Status:** TODO · **Depends on:** CC-01 · **Needs input:** NO · **FSD:** §10
- **Scope:** Customer entity, CRUD, Draft/Active/Inactive, activation checklist, unique code, APIs.
- **Acceptance:** [ ] AC-01 [ ] AC-02 [ ] AC-03 [ ] Inactive customer's completed trips remain invoiceable (§58 #26).
- **Done on:** · **Notes:**

### CC-04 — Customer contacts and billing addresses
- **Status:** TODO · **Depends on:** CC-03 · **Needs input:** NO · **FSD:** §11, §12
- **Acceptance:** [ ] One active default address/primary contact enforced [ ] Deactivate, never delete [ ] AC-04 prepared (snapshot fields exposed for invoice use).
- **Done on:** · **Notes:**

### CC-05 — Customer billing configuration
- **Status:** TODO · **Depends on:** CC-03, CC-02 · **Needs input:** NO · **FSD:** §13
- **Scope:** Effective-dated settings exactly as §13 table (no approval flag, no overpayment flag).
- **Acceptance:** [ ] Settings history kept [ ] Values readable "as of" a date.
- **Done on:** · **Notes:**

### CC-06 — Customer tax/deduction rules with auto-inactivation
- **Status:** TODO · **Depends on:** CC-03 · **Needs input:** NO · **FSD:** §14, §35
- **Scope:** Adding a rule with the same tax name auto-sets the existing one `EffectiveTo = new.From − 1`, `Inactive`, `SupersedesRuleId`. Overlap rejection. Resolution by invoice date.
- **Acceptance:** [ ] AC-05 [ ] AC-06 [ ] Multiple active rules per customer computed independently.
- **Done on:** · **Notes:**

### CC-07 — Customer invoice templates (registry + versioning)
- **Status:** TODO · **Depends on:** CC-03 · **Needs input:** NO · **FSD:** §15
- **Acceptance:** [ ] AC-07 [ ] AC-08 [ ] New version on file change; invoice keeps its version.
- **Done on:** · **Notes:**

### CC-08 — City master
- **Status:** TODO · **Depends on:** CC-01 · **Needs input:** NO · **FSD:** §16
- **Scope:** Seed LHR, ISL, FSD, SKP, MUL, KHI, DGK.
- **Acceptance:** [ ] AC-09 [ ] Display `Name (ABBR)`.
- **Done on:** · **Notes:**

### CC-09 — Route and route stops
- **Status:** TODO · **Depends on:** CC-08 · **Needs input:** NO · **FSD:** §17
- **Acceptance:** [ ] ≥ 2 stops, unique sequence [ ] Inactive city rejected [ ] Route used by a configuration is not re-sequenced in place.
- **Done on:** · **Notes:**

### CC-10 — Trip configurations, stops, allowed vehicles
- **Status:** TODO · **Depends on:** CC-09, CC-03 · **Needs input:** NO · **FSD:** §18, §19
- **Acceptance:** [ ] AC-10 [ ] AC-11 [ ] Vehicle effective ranges cannot overlap per configuration.
- **Done on:** · **Notes:**

### CC-11 — Trip rates: effective dating, one-day split, open-ended auto-close
- **Status:** TODO · **Depends on:** CC-10, CC-02 · **Needs input:** NO · **FSD:** §26
- **Scope:** Overlap check under serializable isolation (+ trigger or equivalent); blank `EffectiveTo` = open-ended, auto-closed when a later rate is added; split helper; resolver service.
- **Acceptance:** [ ] AC-12 [ ] AC-13 [ ] AC-14 [ ] AC-16 [ ] AC-56 [ ] Concurrent inserts cannot create overlaps (test with two parallel transactions).
- **Done on:** · **Notes:**

### CC-12 — Trip entity, numbering, fixed trip creation with rate snapshot
- **Status:** TODO · **Depends on:** CC-11 · **Needs input:** NO · **FSD:** §21, §23, §46.4
- **Acceptance:** [ ] AC-15 [ ] AC-20 [ ] `TRP-YYYY-NNNNNN` from sequence [ ] RateMissing path stores no amount.
- **Done on:** · **Notes:**

### CC-13 — Open trips and Other Location
- **Status:** TODO · **Depends on:** CC-12 · **Needs input:** NO · **FSD:** §22
- **Acceptance:** [ ] AC-19 [ ] Amount editable before invoicing only, with permission and audit.
- **Done on:** · **Notes:**

### CC-14 — Driver default and override
- **Status:** TODO · **Depends on:** CC-12 · **Needs input:** NO · **FSD:** §20
- **Acceptance:** [ ] AC-17 [ ] AC-18.
- **Done on:** · **Notes:**

### CC-15 — Trip lifecycle state machine, completion date, active flag
- **Status:** TODO · **Depends on:** CC-12 · **Needs input:** NO · **FSD:** §24
- **Scope:** Transition table incl. On Hold/Cancelled/Reopen; `CompletionDate` set on Completed; inactivate/reactivate.
- **Acceptance:** [ ] AC-21 [ ] Invalid transitions return 422 [ ] Cancelled never invoiceable.
- **Done on:** · **Notes:**

### CC-16 — Trip events, documents, POD, issues
- **Status:** TODO · **Depends on:** CC-15 · **Needs input:** NO · **FSD:** §23, §25
- **Scope:** Event log with `Source` (Manual/DriverApp/GPS/System), `ClientEventId` dedupe, blob upload for documents/POD, POD approval, issues with hold.
- **Acceptance:** [ ] AC-54 (server side) [ ] Status transitions auto-create events.
- **Done on:** · **Notes:**

### CC-17 — Rate re-resolution and re-pricing
- **Status:** TODO · **Depends on:** CC-12 · **Needs input:** NO · **FSD:** §26 ("When is a trip rate re-resolved?")
- **Acceptance:** [ ] Resolve-missing-rates updates only RateMissing trips [ ] Re-price preview/commit writes `TripRateHistory` and skips invoiced trips.
- **Done on:** · **Notes:**

### CC-18 — Fuel cards and assignments
- **Status:** TODO · **Depends on:** CC-01 · **Needs input:** NO · **FSD:** §28
- **Acceptance:** [ ] No overlapping assignments [ ] Nightly expiry sets Expired [ ] Card number masked in responses.
- **Done on:** · **Notes:**

### CC-19 — Trip fuel
- **Status:** TODO · **Depends on:** CC-15, CC-18 · **Needs input:** NO · **FSD:** §27
- **Acceptance:** [ ] AC-23 [ ] Currency fields when multi-currency on.
- **Done on:** · **Notes:**

### CC-20 — Trip expenses with approval
- **Status:** TODO · **Depends on:** CC-15 · **Needs input:** NO · **FSD:** §29
- **Acceptance:** [ ] AC-24 [ ] Driver-app expenses start Pending [ ] Void, never delete.
- **Done on:** · **Notes:**

### CC-21 — Trip income (billable flag)
- **Status:** TODO · **Depends on:** CC-15 · **Needs input:** NO · **FSD:** §30
- **Acceptance:** [ ] Billable income exposed for invoice lines [ ] Billed income locked.
- **Done on:** · **Notes:**

### CC-22 — Trip operational P&L
- **Status:** TODO · **Depends on:** CC-19, CC-20, CC-21 · **Needs input:** NO · **FSD:** §31
- **Acceptance:** [ ] AC-22 [ ] Rejected expenses excluded [ ] Unpriced trips excluded with count.
- **Done on:** · **Notes:**

### CC-23 — Invoice schema, versioning fields, trip link lock
- **Status:** TODO · **Depends on:** CC-05, CC-06, CC-07, CC-15 · **Needs input:** NO · **FSD:** §33, §36, §46.4, §46.5
- **Scope:** Invoice (no Approved status), InvoiceLine (immutable), InvoiceTripLink with filtered unique index on active TripId, InvoiceAdjustment, InvoiceTaxLine, InvoiceHistory, InvoiceReplacement.
- **Acceptance:** [ ] DB rejects a second active link for a trip [ ] App role cannot UPDATE/DELETE InvoiceLine.
- **Done on:** · **Notes:**

### CC-24 — Eligible-trip search and overlap check
- **Status:** TODO · **Depends on:** CC-23 · **Needs input:** NO · **FSD:** §32.1, §39, §47.3
- **Scope:** Eligibility by **CompletionDate**, POD rule, currency match, categories (Available/Already invoiced/Blocked + reason), summary tiles, overlap list.
- **Acceptance:** [ ] AC-26 [ ] AC-57 [ ] Response matches §47.3 example shape.
- **Done on:** · **Notes:**

### CC-25 — Invoice creation: lines, adjustments, deductions, concurrency
- **Status:** TODO · **Depends on:** CC-24 · **Needs input:** NO · **FSD:** §32.2–32.4, §33, §35, §52
- **Scope:** Transaction per §52 (app lock per customer, UPDLOCK, revalidate, insert, link). Missing-rate hard stop (no deselect bypass). No adjustment-only invoices. Billable income lines. Snapshot bill-to, template version, tax lines.
- **Acceptance:** [ ] AC-25 [ ] AC-27 [ ] AC-28 [ ] AC-29 [ ] AC-30 [ ] AC-31 [ ] AC-32 (parallel test).
- **Done on:** · **Notes:**

### CC-26 — Invoice numbering
- **Status:** TODO · **Depends on:** CC-25 · **Needs input:** YES (Q1) · **FSD:** §46.6, §58 #9
- **Scope:** Configurable number generator per the answered format. Until answered, CC-25 may use a temporary `INV-YYYY-NNNNN` sequence behind the generator interface.
- **Acceptance:** [ ] Format per Q1 [ ] Unique under concurrency.
- **Done on:** · **Notes:**

### CC-27 — Invoice PDF rendering (standard layout; customer layouts later)
- **Status:** TODO · **Depends on:** CC-25 · **Needs input:** YES (Q2, customer layouts only) · **FSD:** §15, §34
- **Scope:** Build the `SystemStandard` HTML→PDF template now (header, bill-to, NTN/STRN, lines by vehicle, adjustments, deductions, net, amount in words PKR, advance-received line). Store as `InvoiceDocument` with hash. Mark customer-specific layouts as the blocked part.
- **Acceptance:** [ ] Re-print returns stored file [ ] Uses snapshots only.
- **Done on:** · **Notes:**

### CC-28 — Invoice evidence: standard layout, vehicle pagination
- **Status:** TODO · **Depends on:** CC-25 · **Needs input:** NO · **FSD:** §41
- **Scope:** Background job; one standard layout for all customers (no template); columns Sr, Trip Date, Route, Customer Reference, Bill Amount; page size from config; vehicle grouping with continuation; SHA-256; write-once storage.
- **Acceptance:** [ ] AC-33 [ ] AC-34 [ ] AC-67.
- **Done on:** · **Notes:**

### CC-29 — Invoice submit and cancel (no approval step)
- **Status:** TODO · **Depends on:** CC-25, CC-28 · **Needs input:** NO · **FSD:** §36, §47.3
- **Acceptance:** [ ] AC-35 [ ] AC-55 [ ] Submit requires evidence when configured [ ] Cancel blocked with active payments/settlements.
- **Done on:** · **Notes:**

### CC-30 — Customer Ledger core and submit postings
- **Status:** TODO · **Depends on:** CC-29 · **Needs input:** NO · **FSD:** §40A
- **Scope:** `CustomerLedgerEntry` (immutable; check constraint one of Dr/Cr; unique SourceType+SourceId+EntryType), `CustomerBalance`, per-customer sequence, posting service. On submit post L1 trip debit, L2 one entry per adjustment (Dr/Cr by sign), L3 one credit per deduction — all dated **submission date**, narration includes invoice date. Cancel posts mirrors (L11).
- **Acceptance:** [ ] 40A acceptance block (submit three entries; double submit = one set) [ ] AC-37 [ ] AC-38 [ ] AC-47 [ ] AC-58 [ ] Posting failure rolls back submit.
- **Done on:** · **Notes:**

### CC-31 — Bank accounts and payment receipts
- **Status:** TODO · **Depends on:** CC-30 · **Needs input:** YES (Q3, seed data only) · **FSD:** §37
- **Scope:** `BankCashAccount` (build the master now; seed from Q3 when answered), `CustomerReceipt`, `InvoicePayment` allocations, methods **Direct to Account** and **Bank Cheque** only, overpayment allowed with confirmation, duplicate instrument warning, L4 credit per allocation, balance/payment status recompute under UPDLOCK.
- **Acceptance:** [ ] AC-36 [ ] AC-39 [ ] AC-63 [ ] AC-46 [ ] Σ allocations = receipt amount.
- **Done on:** · **Notes:**

### CC-32 — Payment reversal
- **Status:** TODO · **Depends on:** CC-31 · **Needs input:** NO · **FSD:** §37 BR-P5, §40A L5
- **Acceptance:** [ ] AC-40 [ ] Transferred payment must be reversed on the active invoice.
- **Done on:** · **Notes:**

### CC-33 — Write-off and discount
- **Status:** TODO · **Depends on:** CC-31 · **Needs input:** NO · **FSD:** §37.5, §40A L6–L7
- **Scope:** `InvoiceSettlement`; standalone and inside payment (`settleRemaining`); reversal.
- **Acceptance:** [ ] AC-61 [ ] 40A write-off example [ ] Amount ≤ balance; reason required.
- **Done on:** · **Notes:**

### CC-34 — Advances on Open trips
- **Status:** TODO · **Depends on:** CC-30, CC-13 · **Needs input:** NO · **FSD:** §37.4, §40A L8–L10
- **Scope:** `CustomerAdvance` on Open trips only; L8 credit linked to trip; auto-apply on submit of the invoice containing the trip (L9 pair, uncapped); move to another Open trip; refund; reversal.
- **Acceptance:** [ ] AC-59 [ ] AC-60 [ ] 40A advance example.
- **Done on:** · **Notes:**

### CC-35 — Invoice regeneration and overlap replacement
- **Status:** TODO · **Depends on:** CC-31, CC-34 · **Needs input:** NO · **FSD:** §38, §39
- **Scope:** No approval; reason required; fully paid (incl. fully settled) rejected; version chain; optional re-price; old submitted invoice gets L12 mirror entries; trips outside new period released with warning.
- **Acceptance:** [ ] AC-41 [ ] AC-44 [ ] Overlap with fully paid invoice blocks Proceed.
- **Done on:** · **Notes:**

### CC-36 — Payment transfer on regeneration
- **Status:** TODO · **Depends on:** CC-35 · **Needs input:** NO · **FSD:** §40, §40A L13
- **Scope:** 100% transfer (payments + applied advances), uncapped, chained regenerations.
- **Acceptance:** [ ] AC-42 [ ] AC-43 [ ] AC-45 [ ] 40A.3 worked example reproduces to the rupee.
- **Done on:** · **Notes:**

### CC-37 — Carry forward and refund of customer credit
- **Status:** TODO · **Depends on:** CC-36 · **Needs input:** NO · **FSD:** §40, §40A L14–L15
- **Acceptance:** [ ] AC-62 [ ] Refund posts debit [ ] Amount ≤ available credit.
- **Done on:** · **Notes:**

### CC-38 — Ledger statement, invoice ledger, customer balances APIs
- **Status:** TODO · **Depends on:** CC-30 · **Needs input:** NO · **FSD:** §40A.4, §47.2
- **Acceptance:** [ ] AC-48 [ ] Statement PDF export [ ] Per-currency statements when multi-currency on.
- **Done on:** · **Notes:**

### CC-39 — Ledger reconciliation job and period lock
- **Status:** TODO · **Depends on:** CC-30 · **Needs input:** NO · **FSD:** §40A.5 LR-4, LR-7
- **Acceptance:** [ ] AC-49 [ ] Mismatch reported [ ] Closed-period posting rejected except Admin with reason.
- **Done on:** · **Notes:**

### CC-40 — Opening balances import
- **Status:** TODO · **Depends on:** CC-30 · **Needs input:** YES (Q4, data only) · **FSD:** §40A L16
- **Scope:** Build importer (CSV: customer code, amount, Dr/Cr, currency, as-of date) and `OB-{CustomerCode}` pseudo-invoice now; loading real data waits for Q4.
- **Acceptance:** [ ] Debit and credit balances post once per customer/currency [ ] Re-import rejected.
- **Done on:** · **Notes:**

### CC-41 — Ledger and receivables reports (LED-01..15)
- **Status:** TODO · **Depends on:** CC-38, CC-33, CC-34, CC-37 · **Needs input:** NO · **FSD:** §42.2, §42.9
- **Acceptance:** [ ] AC-51 [ ] AC-52 [ ] Each report filter/sort/page/export.
- **Done on:** · **Notes:**

### CC-42 — Trip, invoice, P&L, master and audit reports
- **Status:** TODO · **Depends on:** CC-22, CC-28, CC-35 · **Needs input:** NO · **FSD:** §42.1, §42.3–42.7
- **Acceptance:** [ ] All listed report codes return data per their column lists [ ] Background export above 50k rows.
- **Done on:** · **Notes:**

### CC-43 — Back-office UI: masters and trips
- **Status:** TODO · **Depends on:** CC-17, CC-22 · **Needs input:** NO · **FSD:** §48.1–48.4, §48.8
- **Acceptance:** [ ] Screens 1–18 and Currency Setup, Pending Review per §48 [ ] Common patterns §48.1 applied.
- **Done on:** · **Notes:**

### CC-44 — Back-office UI: invoicing, payments, ledger
- **Status:** TODO · **Depends on:** CC-37, CC-38, CC-29 · **Needs input:** NO · **FSD:** §48.5–48.8
- **Acceptance:** [ ] Invoice wizard, detail, Record Payment (with settle remaining), Receipts, Regeneration, Evidence, Customer Ledger, Balances, Advance, Write-off/Discount, Carry Forward/Refund screens.
- **Done on:** · **Notes:**

### CC-45 — Driver app API and trip creation review queue
- **Status:** TODO · **Depends on:** CC-16, CC-19, CC-20 · **Needs input:** YES (Q5, client app only) · **FSD:** §43, §47.2 (driver endpoints)
- **Scope:** Build the driver-scoped API now (my trips, steps, fuel, expenses, issues, POD, sync, trip creation → Draft + Pending review; amounts never returned) and the office review endpoints. The mobile client waits for Q5.
- **Acceptance:** [ ] AC-53 [ ] AC-54 [ ] AC-66.
- **Done on:** · **Notes:**

### CC-46 — Acceptance test suite AC-01..AC-67 and hardening
- **Status:** TODO · **Depends on:** CC-41, CC-42, CC-44, CC-45, CC-39, CC-32 · **Needs input:** NO · **FSD:** §59, §52, §54
- **Scope:** One automated test per acceptance criterion (tag with AC id); performance checks from §54 on seeded volume; security checks (driver scope, financial permissions).
- **Acceptance:** [ ] All AC tests green [ ] Report of any AC not automatable with reason.
- **Done on:** · **Notes:**

---

## Out of scope for Claude Code

Client/business actions tracked elsewhere: FSD sign-off, UAT sign-off, go-live decision, GPS provider selection (Phase 2, §56).
