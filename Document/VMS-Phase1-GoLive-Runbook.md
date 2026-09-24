# VMS Phase 1 — Go-Live Runbook

Companion to `VMS-Phase1-Task-Register.md` (S8-QA-05). Everything in the Task Register that *builds* something is
done; this is the checklist for the one-time steps that happen when the built software meets a real server and a
real client, in order. It assumes the code in this repository as of Stage 8's completion (`VMS.API`, schemas
`core`/`tenancy`/`auth`/`bp`/`veh`/`doc`/`notif`, and `VMSFrontend`).

No existing partner or vehicle records are being migrated into VMS (OQ-09, answered) — every tenant starts empty and
is populated through the screens (or the bulk document loader, step 7) once its users can sign in.

---

## 1. Prerequisites

- A SQL Server instance the API can reach (LocalDB is Development-only; go-live needs a real instance — Azure SQL,
  a managed SQL Server, or an on-prem instance with TLS).
- A host for `VMS.API` — .NET 8 runtime, IIS/Kestrel behind a reverse proxy, or an App Service; whatever the
  client's infrastructure already runs .NET on. No Docker image exists in this repo yet; if the target is
  container-based, that image is a separate, not-yet-built task.
- A place to serve the built Angular files (`VMSFrontend/dist/vms-frontend/browser`) — a static file host, the same
  reverse proxy as the API, or a CDN. Same-origin with the API is simplest (see step 6); a different origin needs
  `Cors:AllowedOrigins` set (step 2) and `environment.prod.ts`'s `apiUrl` pointed at it.
- An SMTP relay the client's mail system allows, for invite and password-reset emails (step 5). Not strictly
  required to *start* the API, but required before inviting any user besides the first Super Admin — see step 2's
  note on `AppSettings:Smtp`.
- A folder (or mounted volume) for uploaded files (`FileStorage:RootPath`), sized for the client's document volume
  and backed up on the same schedule as the database — a document's row and its file must survive together.

## 2. Configuration

None of these live in `appsettings.json` as shipped (it ships with empty placeholders on purpose — nothing
production-sensitive belongs in source control). Set them as environment variables, an untracked
`appsettings.Production.json`, or your host's secret store. `AppSettings:Secret` under 32 characters refuses to
start (a deliberate guard, not a bug to work around).

| Key | Purpose | Example |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` — turns off Swagger and the "any localhost port" CORS allowance, turns on HTTPS redirection. | `Production` |
| `Data:mainOrg` | The SQL Server connection string every schema lives in (one database, several schemas — see step 3). | `Server=tcp:vms-sql.example.com;Database=VMS;User Id=...;Password=...;TrustServerCertificate=False` |
| `AppSettings:Secret` | JWT signing key, 32+ characters, random, unique per environment. Rotating it signs everyone out. | a generated secret, not a word |
| `AppSettings:BaseUrl` | The **frontend's** own URL — invite and reset emails link back here, not to the API. | `https://vms.example.com` |
| `AppSettings:AppSupportEmail` | Fallback "from" address if `Smtp:From` is blank. | `support@example.com` |
| `AppSettings:Smtp:Host/Port/User/Password/From/EnableSsl` | Outbound email for invites and password resets. Left blank, the API still runs — it just logs (Development) or silently drops (Production) every email, so nobody but the first Super Admin can ever get in. Set before step 5. | your relay's settings |
| `RateLimiting:AuthPermitPerMinute` | Login/forgot-password attempts allowed per IP per minute. `20` is the shipped default; tighten if this API is internet-facing. | `20` |
| `FileStorage:RootPath` | Where uploaded documents and receipts are encrypted and stored. Must exist, be writable, and be backed up. | `/var/vms/files` or `D:\VMS-Files` |
| `FileStorage:EncryptionKey` | AES-GCM key for stored files. Generate once, keep it — losing it makes every stored file unreadable. | a generated key, not a word |
| `Cors:AllowedOrigins` | Only read outside Development. Leave empty if the frontend is served same-origin (step 6); otherwise the frontend's own origin(s). | `["https://vms.example.com"]` |
| `Seed:SuperAdmin:Email` / `Seed:SuperAdmin:Password` | The platform's first Super Admin, created once on first startup if none exists yet (`AuthDataSeeder`). The API refuses to start with no Super Admin and these unset. | a real email you control, a strong password |
| `Seed:SuperAdmin:FirstName` / `LastName` | Optional; default to "Super"/"Admin". | — |
| `Operations:DefaultTimeZone` | The IANA zone every tenant's "today" defaults to (NFR-DT-06) until a tenant sets its own. Defaults to `Asia/Karachi` if unset. | `Asia/Karachi` |
| `Messages:Directory` | Optional. A folder holding `messages.en.json`/`messages.ur.json` overrides, picked up without a restart. Leave unset to use the built-in English catalogue. | — |

## 3. Database

The API migrates its own schema on every startup (`app.Use*Module()` in `Program.cs`, in this exact order — each
depends on the one before it existing): **core → tenancy → auth → bp → doc → veh → notif**. For most deployments,
step 4 (start the API once against an empty, reachable database) is the whole of this step — nothing else to run.

If your DBA wants to review or run the SQL directly instead of trusting `dotnet ef` at deploy time (a common ask
before a database gets touched in production), the same schema is captured as idempotent scripts under
`database/scripts/`, safe to run more than once and safe to run against a database that already has some of them
applied:

```
000_create_database.sql    -- CREATE DATABASE, if the DBA is provisioning it fresh
003_core_schema.sql        -- core (audit log) — first: every other schema writes to it
001_tenancy_schema.sql     -- tenancy
002_auth_schema.sql        -- auth
004_bp_schema.sql          -- bp (Business Partners)
006_doc_schema.sql         -- doc (Documents)
005_veh_schema.sql         -- veh (Vehicles)
007_notif_schema.sql       -- notif (Notifications)
```

(The numbering is the order the scripts were first generated in, not the dependency order — run them in the order
listed above, not the order the numbers suggest.) Whichever path is used, the API's own startup migration check
still runs afterward and is a no-op if the schema is already current — it never conflicts with having applied the
scripts by hand first.

## 4. First boot

1. Start `VMS.API` with the configuration from step 2 and a reachable, empty `Data:mainOrg` database.
2. Watch the log for each module's migration and seed step (Core, Tenancy, Auth, BusinessPartners, Documents,
   Vehicles, Notifications, in that order) completing without error. Auth's own seed step creates the permission
   catalogue, the six global role templates (`TENANT_ADMIN`/`FLEET_MANAGER`/`FINANCE_USER`/`OPERATIONS_USER`/
   `DRIVER`/`READ_ONLY`) and the platform tenant, and — only if none exists yet — the first Super Admin from
   `Seed:SuperAdmin:Email`/`Password`.
3. `GET /health` returns `{"status":"healthy"}` once the app has finished starting; it does not itself prove the
   database migrated (it touches no database), so treat step 2's log, not this endpoint alone, as the real signal.
4. Sign in as the Super Admin (`POST /api/auth/login`) to confirm the seed actually worked end to end.

## 5. The first tenant

Every client is its own tenant (row-level `TenantId`, never mixed with another client's data). The platform tenant
created in step 4 is VMS's own bookkeeping row, not a client — create the client's real tenant next, as the Super
Admin:

1. `POST /api/system/tenants` with the client's name, a short tenant code, and the first Tenant Admin's name and
   email. The response carries an `adminInviteLink` directly — useful for the very first admin even before SMTP
   (step 2) is confirmed working, since nobody has signed in yet to receive an email any other way.
2. Send that link to the client's first administrator (or open it yourself if you are standing the client up on
   their behalf), which lets them set their own password (`POST /api/auth/accept-invite`) and sign in as
   `TENANT_ADMIN` — every permission in the catalogue, by design (`AuthDataSeeder`'s "full-access role means
   everything").
3. If the client wants a light-mode and/or dark-mode logo on their sign-in screen and shell, upload it now or any
   time after (`PUT /api/system/tenants/{id}/logos/{variant}`, Super Admin) — see the tenant-logos note in this
   project's own memory if you need the exact endpoint shape again.

## 6. Master data

Nothing here needs a separate "load" step — every list in this system (lookups, document types, notification
rules, the tenant's first branch) is seeded lazily, the first time the new tenant's admin opens the screen that
needs it (`S0-FND-12`'s idiom, reused by every stage since). What the Tenant Admin should do, in the days before
real data entry starts, not as a blocking gate:

- **Master data** (`/admin/master-data`): review the seeded Vehicle Type, Make, City and other platform lists;
  add anything specific to the client's own fleet or geography.
- **Notification rules** (`/admin/notification-rules`): review the six default lead times and recipients (§19A.6,
  §23.4's own defaults); change a lead time or a recipient permission if the client wants earlier or later warning,
  or a different role told.
- **Branches**: the tenant starts with one, "Head Office" (OQ-10); add more only if the client actually operates
  from more than one location and wants users restricted to their own branch (§23B.4's data scope).
- **Roles**: the six templates (`Administrator`/`Fleet Manager`/`Finance User`/`Operations User`/`Driver`/
  `Read Only`) are starting points, fully editable, and can be cloned into the client's own custom roles
  (`/roles`) if their structure does not match one of the six.

## 7. Users and existing paperwork

1. Invite the client's real users (`/users`, "Invite user"), one role each to start (multiple roles can be added
   later, §23B.1) — every invite is the same link-based accept flow as step 5's first admin.
2. If the client has physical documents already on file for vehicles or partners being entered directly into VMS
   (not migrated records — OQ-09 ruled that out — just paperwork for whatever the client is about to key in), use
   the **bulk document upload** screen (`/admin/bulk-documents`, S8-QA-03) once those vehicles and partners exist,
   rather than opening each record's own Documents tab one at a time.

## 8. Frontend

1. Build with the **production** configuration and the real API URL: edit `VMSFrontend/src/environments/
   environment.prod.ts`'s `apiUrl` before building if the API is not served at same-origin `/api` (the shipped
   default assumes a reverse proxy puts the API under the frontend's own origin, at `/api`).
2. `npm run build` (defaults to the `production` configuration per `angular.json` — do **not** pass
   `--configuration development` here, that is only for the browser-check recipe against a scratch API).
3. Deploy `VMSFrontend/dist/vms-frontend/browser/*` to the static host or reverse-proxy path chosen in step 1's
   prerequisites.

## 9. Cutover checklist

- [ ] `Data:mainOrg` points at the real production database, not a scratch or staging one.
- [ ] `AppSettings:Secret` and `FileStorage:EncryptionKey` are freshly generated for this environment, not copied
      from Development or from another client's deployment.
- [ ] `FileStorage:RootPath` exists, is writable, and is included in the backup plan.
- [ ] SMTP is confirmed working (send a real test invite and receive it) before any user beyond the first admin is
      invited.
- [ ] `Cors:AllowedOrigins` is set correctly if the frontend is not same-origin with the API; otherwise confirmed
      empty and same-origin is genuinely how it is deployed.
- [ ] The Super Admin credentials (`Seed:SuperAdmin:*`) are stored somewhere durable and access-controlled, not
      only in the deploy pipeline's own logs.
- [ ] A smoke test after deploy: sign in as the Tenant Admin, create one Business Partner, create and activate one
      Vehicle, upload one document, confirm the notification bell and `/admin/notification-rules` both load. This
      is a fast, real walk of five of Phase 1's own modules end to end, not a health-check substitute.
- [ ] Database backups are scheduled, and a restore has actually been tested once against a non-production copy —
      "the backup job runs" and "the backup restores" are different claims.

## 10. After go-live

- **S8-QA-04 (UAT support, defect triage)** starts here, once real users are in the system: watch
  `core.AuditEntries` and `auth.AccessDenials` for anything unexpected, and the notification job's own log line
  (`NotificationsHostedService`, hourly) for whether it is finding and sending what it should.
- A defect found in production is fixed the same way every defect in this project has been: a failing test written
  first, the fix, mutation-tested if it touches a guard, then shipped — the discipline this whole build followed
  does not stop at go-live.
