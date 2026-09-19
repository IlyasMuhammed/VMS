# VMS — Vehicle Management System

Multi-tenant user management for VMS: sign-in, users, roles and permissions, and tenants.
This is the foundation the rest of VMS (vehicles, drivers, trips, …) will be built on.

| Part | Path | Stack |
|---|---|---|
| API | `src/VMS.API` | ASP.NET Core 8, JWT bearer auth |
| Shared kernel | `src/VMS.Shared` | tenant context, query filters, permissions, ApiResponse |
| Auth module | `src/VMS.Modules.Auth` | users, roles, permissions, sessions (EF Core, schema `auth`) |
| Tenancy module | `src/VMS.Modules.Tenancy` | tenants, super admins (EF Core, schema `tenancy`) |
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

# 2. API — http://localhost:5000  (Swagger at /swagger)
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/VMS.API --urls http://localhost:5000

# 3. Web app — http://localhost:4200
cd VMSFrontend
npm install
npm start
```

On first start the API seeds the platform tenant (`VMS-PLATFORM`), the four permissions, the four
platform roles and the first Super Admin. The API also applies pending EF migrations at startup, so
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

**Permissions** live in `VMS.Shared/Authorization/PermissionCodes.cs` (`USER_VIEW`, `USER_MANAGE`,
`ROLE_VIEW`, `ROLE_MANAGE`). Add a new module's permissions there; the seeder picks them up. Platform
administration is *not* a permission — it is the `is_super_admin` claim, so it can never be granted by
editing a role. You can only assign a role, or grant a permission, that you hold yourself.

## API

| | |
|---|---|
| `POST /api/auth/login` `refresh` `logout` `forgot-password` `reset-password` `accept-invite` | anonymous |
| `GET /api/auth/me`, `PUT /api/auth/password`, `GET /api/tenant`, `GET /api/tenant/logos/{light\|dark}` | signed in |
| `GET/POST/PATCH/DELETE /api/users`, `PUT /api/users/{id}/role`, `POST /api/users/{id}/reset-password`, `GET /api/users/assignable-roles` | `USER_VIEW` / `USER_MANAGE` |
| `GET/POST/PUT /api/roles`, `PUT /api/roles/{id}/permissions`, `PATCH /api/roles/{id}/deactivate`, `GET /api/roles/permissions` | `ROLE_VIEW` / `ROLE_MANAGE` |
| `GET/POST/PUT /api/system/tenants`, `PATCH /api/system/tenants/{id}/status`, `GET/PUT/DELETE /api/system/tenants/{id}/logos/{light\|dark}` | Super Admin |

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

## Changing the database model

```powershell
dotnet ef migrations add <Name> --project src/VMS.Modules.Auth --output-dir Data/Migrations      # or VMS.Modules.Tenancy / Migrations
dotnet ef migrations script --idempotent --project src/VMS.Modules.Auth --output database/scripts/002_auth_schema.sql
```

`dotnet ef` reads the connection string from `src/VMS.API/appsettings.json` (override with
`VMS_DB_CONNECTION`). Regenerate the matching `database/scripts/*.sql` after every migration.

## Rules worth knowing before adding modules

* Never call `IgnoreQueryFilters()` inside a projection or subquery. It switches filters off for the
  **whole** query, not one table, and silently leaks other tenants' rows. Use it only as a deliberate,
  top-level lookup (e.g. the global email-uniqueness check).
* New tenant-owned entities implement `ITenantScopedEntity`; their DbContext implements
  `ITenantScopedDbContext` and calls `ApplyTenantQueryFilters` / `StampTenantScopedEntities` (see
  `AuthDbContext`).
