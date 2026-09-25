# VMS Trip, Billing, Invoicing & Customer Ledger — Implementation Notes

Companion to `Document/TASKS.md` (task CC-00). Spec: `Document/VMS — Trip Management, Customer Billing, Invoicing & Customer Ledger — FSD (Phase 1).md` (v1.1). This file is the register's own `docs/vms/IMPLEMENTATION-NOTES.md` deliverable — placed under this repo's real `Document/` convention instead, since that is where every other FSD, task register and runbook in this repo already lives (see the note under "Path deviations" below).

## 1. Stack (as found, not guessed)

- **Backend:** ASP.NET Core 8 Web API, C# 12, modular monolith. One class library project per bounded module (`VMS.Modules.Auth`, `.Tenancy`, `.Core`, `.BusinessPartners`, `.Documents`, `.Vehicles`, `.Notifications`), a shared kernel (`VMS.Shared`) for cross-module contracts, and one host (`VMS.API`) that wires every module together in `Program.cs`.
- **ORM / DB:** EF Core 8, SQL Server (LocalDB in Development via `Data:mainOrg`). Each module owns one schema and one `DbContext`, migrated independently (`__EFMigrationsHistory` per schema). No cross-schema foreign keys — modules reference each other only through small interfaces.
- **Auth/RBAC:** JWT bearer, permission-claim-based (`[RequirePermission(code)]`), a fully dynamic role system (Stage 6) with row-level data scope (`ScopeTypes`) and a `LinkedPartnerId` for driver-scoped users already in place.
- **Tests:** xUnit, `tests/VMS.Tests`, hitting the real API in-process (`ApiFactory`/`ApiClient`/`TestTokens`) against a throwaway LocalDB database created, migrated, seeded and dropped per run. `dotnet test tests\VMS.Tests\VMS.Tests.csproj --artifacts-path $env:TEMP\vms-artifacts`.
- **Frontend:** Angular 19 + PrimeNG 19, `VMSFrontend/`, its own logic-test suites (`npm run check:*`) plus real-browser Puppeteer scripts (scratchpad `shots/*.mjs`) against a scratch API + scratch DB.
- **No Docker, no existing `docs/` folder** — every project document lives under `Document/` at the repo root; this file follows that convention.

## 2. Reused entities and how the new module attaches to them

| FSD reference | Real entity | How this module reaches it |
| --- | --- | --- |
| Vehicle | `VMS.Modules.Vehicles.Domain.Vehicle` (internal) | `IVehicleDirectory` (already in `VMS.Shared.Vehicles`) — read-only lookup by id, no FK. |
| Driver | `BusinessPartner` with the `Driver` role | `IPartnerDirectory` (already in `VMS.Shared.Partners`) — same pattern Vehicles already uses for its own `DefaultDriverId`. |
| "DriverVehicleAssignment" | There is no separate table — `Vehicle.DefaultDriverId` **is** the current assignment, kept as a copy of the open relation (BR-VH rules already enforce it). This module reads it through `IVehicleDirectory`; it does not need its own assignment table. |
| Fuel card company / expense vendor / workshop | `BusinessPartner` (existing roles) | `IPartnerDirectory`. |
| `VehicleTransaction` (FSD: "optional roll-up of trip fuel/expense") | `VMS.Modules.Vehicles.Domain.VehicleTransaction` (internal, the vehicle ledger) | **Not wired in this pass.** No CC task's acceptance criteria requires posting trip fuel/expense into the vehicle ledger. Left as a documented extension point: if a later task needs it, add `IVehicleTransactionRecorder` to `VMS.Shared` the same way `INotificationTrigger` was added for Vehicles → Notifications, implemented by Vehicles, called by this module. Do not build it speculatively. |
| Driver app login | Existing `UserAccount` + Driver role + `ScopeTypes` (Stage 6, OQ-20 answered: driver-app users are the same role system) | No new auth mechanism. A driver's JWT already carries a permission set and `LinkedPartnerId`; "driver scope" in §44 is `ScopeTypes.OwnVehicles`/a driver-specific data filter on trip queries, not a new identity system. |

## 3. New module: `VMS.Modules.Trips`, schema `trp`

**One module, one schema**, not several — deliberately, even though it covers Customer/City/Route/TripConfig/Rates/Trip/Fuel/Expense/Income/Invoice/Payment/Ledger. Reasons:

1. The FSD's own non-negotiable rule — "ledger entries are posted in the same DB transaction as their cause" (§40A) — is trivial with one `DbContext`/one transaction and painful across schema boundaries (the pattern used everywhere else in this repo for cross-module work, small `I*Directory` interfaces, is for **read-only lookups**, not multi-entity writes in one transaction).
2. Every entity in this FSD is mutually load-bearing within one lifecycle (Trip → Invoice → Ledger, all versioned/immutable together) — it mirrors how the Vehicles module already bundles Vehicle + Finance + RecurringCharges + Documents-check + Notifications-source as one project, not five.
3. It still exposes the same handful of thin `VMS.Shared` interfaces other modules already use to reach in — nothing here breaks the "no project reference between peer modules" rule.

Folder layout mirrors `VMS.Modules.Vehicles` exactly:

```
src/VMS.Modules.Trips/
  Domain/            entities (Customer, City, Route, TripConfiguration, TripRate, Trip, TripEvent, FuelCard, TripFuel,
                      TripExpense, TripIncome, Invoice, InvoiceLine, InvoiceTripLink, InvoiceAdjustment, InvoiceTaxLine,
                      InvoiceHistory, CustomerReceipt, InvoicePayment, InvoiceSettlement, CustomerAdvance,
                      CustomerLedgerEntry, CustomerBalance, Currency, ExchangeRate, IdempotencyRecord)
  Data/              TripsDbContext, TripsMaps.cs (Fluent API + filtered unique indexes), Migrations/
  Models/            DTOs per area (CustomerModels.cs, TripModels.cs, InvoiceModels.cs, LedgerModels.cs, ReportModels.cs, ...)
  Services/          one *Service.cs per area, following the existing one-interface-one-implementation convention
  Controllers/       one *Controller.cs per §47.2 area
  ITripsModule.cs    Add TripsModule / UseTripsModule, following IVehiclesModule.cs's exact shape
```

Registration order in `Program.cs`: **after** BusinessPartners, Documents and Vehicles (needs `IPartnerDirectory`/`IVehicleDirectory`), **before or alongside** Notifications is not required yet — this task register defines no `ITripNotificationSource`, so Trips does not need to precede or follow Notifications. Placed after Vehicles, before Notifications, to keep the dependency list monotonic and leave room for a future notification source (e.g. "invoice overdue") without reordering again.

## 4. Deviations from the FSD's literal text, and why

The register's own instruction is "follow the repo's existing conventions" — where the FSD's §47.1 API conventions genuinely conflict with 8 already-shipped stages' conventions, the decision below always favours **not breaking the 677 existing tests / the shipped API contract**, while still meeting the new FSD's intent for this module.

| FSD says (§47.1) | Existing repo convention | Decision |
| --- | --- | --- |
| `If-Match: "<rowVersion>"` header, mismatch → 409 `CONCURRENCY_CONFLICT` | Every existing module puts `rowVersion` (base64 of the SQL `rowversion` column) **in the request body**, compared server-side, `ConflictException` → 409, message names the last editor | Keep the body-`rowVersion` convention (consistent with all 8 already-shipped stages and every existing UI screen's save pattern) — **not** a header. Same 409 outcome the FSD asks for, different transport, documented as a deliberate, non-breaking choice. |
| `Idempotency-Key` header on money/ledger-creating POSTs, replay returns the first response | Nothing like this exists anywhere in the repo yet | **New, built as specified** (this module is the first to create irreversible financial side-effects from a POST that a client might legitimately retry) — a small `trp.IdempotencyRecords` table (this module's own schema, not Core's, so no migration touches an already-shipped stage) keyed by `(TenantId, Idempotency-Key, Route)`, storing the first response's status+body, returned verbatim on replay. Interface `IIdempotencyStore` lives in this module for now; promote to `VMS.Shared` only if a later module needs it too. |
| Error shape `{code, message, details:[...], correlationId}` | `ApiResponse { success, message, errors:[{field, code, message, params}] }` via `GlobalExceptionMiddleware` | **Additive, not a fork.** Add nullable `Code` and `CorrelationId` to the existing `ApiResponse` (default null → omitted from JSON via the existing `WhenWritingNull` policy, so all 677 existing tests keep seeing exactly what they see today). A new `BusinessRuleException(code, message, details?)` maps to 422 in `GlobalExceptionMiddleware` and populates `code`/`details`; `CorrelationId` is stamped from `HttpContext.TraceIdentifier` for every response the middleware touches, module-wide (harmless everywhere else, since it is new/nullable/additive). |
| `docs/vms/VMS-FSD.md`, `docs/vms/IMPLEMENTATION-NOTES.md` | Every FSD, task register and runbook lives under `Document/` | Kept under `Document/` (this file, and the FSD already there); no new `docs/` tree, to avoid two parallel doc locations that can drift. `TASKS.md`'s own header text is left as the user pasted it (source document, not edited); this note is the actual location record. |
| Permission names `Customer.View`, `Rate.Configure`, `Payment.WriteOff`, ... (dotted, per §44) | `PermissionCodes` catalog: flat constants, dot-containing string **values** but C# constant names in `MODULE_ACTION` form, one `Catalog` list with `(Code, Name, Module, Description, Level)` | New codes added the same way Documents/Vehicles/Notifications' FSD names were mapped: `TRP_CUSTOMER_VIEW`, `TRP_CUSTOMER_EDIT`, `TRP_TAXRULE_EDIT`, `TRP_TEMPLATE_EDIT`, `TRP_CITY_EDIT`, `TRP_ROUTE_EDIT`, `TRP_TRIPCONFIG_EDIT`, `TRP_RATE_CONFIGURE`, `TRP_RATE_REPRICE`, `TRP_TRIP_VIEW/CREATE/EDIT/STATUS/INACTIVATE/DOCUMENTS/REVIEW`, `TRP_POD_APPROVE`, `TRP_EXPENSE_EDIT/APPROVE`, `TRP_FUEL_EDIT`, `TRP_INCOME_EDIT`, `TRP_PNL_VIEW`, `TRP_FUELCARD_EDIT`, `TRP_INVOICE_GENERATE/VIEW/SUBMIT/CANCEL/REGENERATE`, `TRP_PAYMENT_CREATE/REVERSE/CARRYFORWARD/REFUND/WRITEOFF/DISCOUNT/ADVANCE`, `TRP_LEDGER_VIEW/OPENINGBALANCE/PERIODLOCK`, `TRP_REPORT_VIEW` (one generic report-view permission covering every `LED-*`/other report code in this module, the same generic-not-per-report choice already made for `FIN_REPORT_VIEW`/`DOC_REGISTER_VIEW` — §42.9's "Report.{Code}.View" per-code idea is **not** built literally; it would mean 80 near-duplicate permissions for no acceptance criterion that needs per-report granularity). |
| SQL Server filtered unique indexes for "no overlap"/"one active row" rules (§46.5) | Already the exact mechanism `UX_Documents_Current`, `UX_Notifications_Dedup`, `RecurringCharges`' open-row index use | No deviation — target DB is SQL Server, so this is a direct, literal reuse of an existing, working pattern (`HasFilter` in Fluent API), including for `InvoiceTripLink`'s "one active link per trip" and `TripRate`'s overlap prevention. |
| Money `decimal(18,2)` | Every existing money column in `bp`/`veh` schemas | No deviation. |

## 5. Test and lint commands (run green on the untouched repo before this module existed)

- Backend: `dotnet test tests\VMS.Tests\VMS.Tests.csproj --artifacts-path $env:TEMP\vms-artifacts` — 677/677 (confirmed 2026-09-24, before this module).
- Frontend: `npm test` (`check:access`, `check:shared`, `check:datetime`, `check:partners`, `check:vehicles`), `npm run check:colors`, `npm run check:contrast`, `npm run build` (must pass `--configuration development` for scratch-API browser checks only; plain `npm run build` for production is fine as-is).
- No separate lint command beyond the above logic-test suites and the Angular production build (which fails on TS errors); this repo has no standalone ESLint/StyleCop gate wired into CI.

## 5a. A deviation found later, recorded here for the same reason as §4 (CC-12)

| FSD says (§21/§23) | Existing repo convention | Decision |
| --- | --- | --- |
| Trip number `TRP-YYYY-NNNNNN` — a 4-digit year | The shared `VMS.Shared.Numbering.INumberSeries`/`core.NumberSeries` mechanism every other module's number goes through (`BP-26-00147`, `CUS-00001`, ...) hard-codes a 2-digit year in `NumberFormat.Format`'s `Yearly` case, to match the *first* FSD's own convention | A small, self-contained `trp.TripNumberCounters` table + allocator, using the identical race-safe `MERGE ... WITH (UPDLOCK, HOLDLOCK)` idiom as the shared mechanism, formatting `TRP-{yyyy}-{nnnnnn}` directly. Not routed through `INumberSeries`: making the shared format's year width configurable would need a migration on the already-shipped Core module for one format quirk only this FSD asks for — lower blast radius to keep it local. |

## 6. What CC-00 found that later tasks should know

- §42.9's "`GET /api/reports/{code}?filters`, one generic endpoint for all 80 report codes" (§47.2) maps cleanly onto one `ReportsController` with a `code` route parameter and one `TRP_REPORT_VIEW` permission (see §4 above) — CC-41/CC-42 do not need 80 controller actions or 80 permissions.
- The FSD's `Trip.Review` permission (used by CC-45's driver-created-trip review queue) is new — added as `TRP_TRIP_REVIEW`.
- `CorrelationId` — Core's existing audit mechanism already stamps a correlation id onto every audit row (`AuditEntry`); the new `ApiResponse.CorrelationId` field reuses the *same* value already available on `HttpContext`, not a second, independent correlation id generator.
