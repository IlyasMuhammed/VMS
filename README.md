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
| `GET /api/auth/me`, `PUT /api/auth/password`, `GET /api/tenant` | signed in |
| `GET/POST/PATCH/DELETE /api/users`, `PUT /api/users/{id}/role`, `POST /api/users/{id}/reset-password`, `GET /api/users/assignable-roles` | `USER_VIEW` / `USER_MANAGE` |
| `GET/POST/PUT /api/roles`, `PUT /api/roles/{id}/permissions`, `PATCH /api/roles/{id}/deactivate`, `GET /api/roles/permissions` | `ROLE_VIEW` / `ROLE_MANAGE` |
| `GET/POST/PUT /api/system/tenants`, `PATCH /api/system/tenants/{id}/status` | Super Admin |

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
