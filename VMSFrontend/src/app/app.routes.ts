import { Routes } from '@angular/router';
import { Access, NavMeta } from './core/access';
import { accessGuard, authGuard, guestGuard } from './core/guards';
import { unsavedChangesGuard } from './core/unsaved-changes.guard';

/**
 * How every page beyond sign-in declares itself. `access` is enforced by the route guard; `nav`, if given,
 * puts the page in the menu, and a `description` on it adds a dashboard tile. Both are read from here, so
 * adding a page is one entry and the guard, the menu and the tile cannot disagree.
 */
const page = (title: string, access?: Access, nav?: NavMeta) => ({
  title,
  canActivate: access ? [accessGuard] : [],
  data: { access, nav },
});

export const routes: Routes = [
  {
    path: 'auth',
    canActivate: [guestGuard],
    children: [
      { path: 'login', title: 'Sign in', loadComponent: () => import('./pages/auth/login.component').then((m) => m.LoginComponent) },
      { path: 'forgot-password', title: 'Forgot password', loadComponent: () => import('./pages/auth/forgot-password.component').then((m) => m.ForgotPasswordComponent) },
      { path: 'reset-password', title: 'Reset password', loadComponent: () => import('./pages/auth/reset-password.component').then((m) => m.ResetPasswordComponent) },
      { path: 'accept-invite', title: 'Accept invitation', loadComponent: () => import('./pages/auth/accept-invite.component').then((m) => m.AcceptInviteComponent) },
      { path: '', pathMatch: 'full', redirectTo: 'login' },
    ],
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      {
        path: '',
        pathMatch: 'full',
        ...page('Dashboard', undefined, { label: 'Dashboard', icon: 'pi-home' }),
        loadComponent: () => import('./pages/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'partners',
        ...page('Business partners', { permission: 'BP.VIEW' }, { label: 'Business partners', icon: 'pi-address-book', section: 'Business', description: 'Customers, drivers, workshops, banks and vendors.' }),
        loadComponent: () => import('./pages/partners/partners.component').then((m) => m.PartnersComponent),
      },
      {
        // Before `partners/:id`, so "new" is not read as an id.
        path: 'partners/new',
        ...page('New partner', { permission: 'BP.CREATE' }),
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./pages/partners/partner-form.component').then((m) => m.PartnerFormComponent),
      },
      {
        path: 'partners/:id',
        ...page('Business partner', { permission: 'BP.VIEW' }),
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./pages/partners/partner-form.component').then((m) => m.PartnerFormComponent),
      },
      {
        path: 'vehicles',
        ...page('Vehicles', { permission: 'VEH.VIEW' }, { label: 'Vehicles', icon: 'pi-truck', section: 'Business', description: 'The fleet, from a purchase being entered to a vehicle sold.' }),
        loadComponent: () => import('./pages/vehicles/vehicles.component').then((m) => m.VehiclesComponent),
      },
      {
        // Before `vehicles/:id`, so "new" is not read as an id.
        path: 'vehicles/new',
        ...page('New vehicle', { permission: 'VEH.CREATE' }),
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./pages/vehicles/vehicle-wizard.component').then((m) => m.VehicleWizardComponent),
      },
      {
        path: 'vehicles/:id/edit',
        ...page('Edit vehicle', { permission: 'VEH.EDIT' }),
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./pages/vehicles/vehicle-wizard.component').then((m) => m.VehicleWizardComponent),
      },
      {
        path: 'vehicles/:id',
        ...page('Vehicle', { permission: 'VEH.VIEW' }),
        loadComponent: () => import('./pages/vehicles/vehicle-detail.component').then((m) => m.VehicleDetailComponent),
      },
      {
        path: 'customers',
        ...page('Customers', { permission: 'TRP.CUSTOMER.VIEW' }, { label: 'Customers', icon: 'pi-building', section: 'Business', description: 'Who is billed for a trip: rates, tax rules, invoice templates and their balance.' }),
        loadComponent: () => import('./pages/customers/customers.component').then((m) => m.CustomersComponent),
      },
      {
        // Before `customers/:id`, so "new" is not read as an id.
        path: 'customers/new',
        ...page('New customer', { permission: 'TRP.CUSTOMER.EDIT' }),
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./pages/customers/customer-form.component').then((m) => m.CustomerFormComponent),
      },
      {
        path: 'customers/:id',
        ...page('Customer', { permission: 'TRP.CUSTOMER.VIEW' }),
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./pages/customers/customer-form.component').then((m) => m.CustomerFormComponent),
      },
      {
        path: 'cities',
        // §16: "all view" — every signed-in role, not gated on TRP.CITY.EDIT; the component itself gates Add/Edit on it.
        ...page('Cities', undefined, { label: 'Cities', icon: 'pi-map-marker', section: 'Business', description: 'The city master behind every address, route and trip configuration.' }),
        loadComponent: () => import('./pages/cities/cities.component').then((m) => m.CitiesComponent),
      },
      {
        path: 'routes',
        ...page('Routes', { permission: 'TRP.ROUTE.EDIT' }, { label: 'Routes', icon: 'pi-directions', section: 'Business', description: 'The origin, stops and destination every trip configuration is built on.' }),
        loadComponent: () => import('./pages/routes/routes.component').then((m) => m.RoutesComponent),
      },
      {
        // Before `routes/:id`, so "new" is not read as an id.
        path: 'routes/new',
        ...page('New route', { permission: 'TRP.ROUTE.EDIT' }),
        loadComponent: () => import('./pages/routes/route-form.component').then((m) => m.RouteFormComponent),
      },
      {
        path: 'routes/:id',
        ...page('Route', { permission: 'TRP.ROUTE.EDIT' }),
        loadComponent: () => import('./pages/routes/route-form.component').then((m) => m.RouteFormComponent),
      },
      {
        path: 'trip-configurations',
        ...page('Trip Configurations', { permission: 'TRP.TRIPCONFIG.VIEW' }, { label: 'Trip Configurations', icon: 'pi-sitemap', section: 'Business', description: "A customer's fixed trips: route, allowed vehicles and rates." }),
        loadComponent: () => import('./pages/trip-configurations/trip-configurations.component').then((m) => m.TripConfigurationsComponent),
      },
      {
        // Before `trip-configurations/:id`, so "new" is not read as an id.
        path: 'trip-configurations/new',
        ...page('New trip configuration', { permission: 'TRP.TRIPCONFIG.EDIT' }),
        loadComponent: () => import('./pages/trip-configurations/trip-configuration-form.component').then((m) => m.TripConfigurationFormComponent),
      },
      {
        path: 'trip-configurations/:id',
        ...page('Trip configuration', { permission: 'TRP.TRIPCONFIG.VIEW' }),
        loadComponent: () => import('./pages/trip-configurations/trip-configuration-form.component').then((m) => m.TripConfigurationFormComponent),
      },
      {
        path: 'fuel-cards',
        ...page('Fuel Cards', { permission: 'TRP.FUELCARD.EDIT' }, { label: 'Fuel Cards', icon: 'pi-credit-card', section: 'Business', description: 'Cards, their issuing company, and which vehicle or driver holds each one.' }),
        loadComponent: () => import('./pages/fuel-cards/fuel-cards.component').then((m) => m.FuelCardsComponent),
      },
      {
        path: 'payables',
        ...page('Payables due', { permission: 'FIN.DUE.CONFIRM' }, { label: 'Payables due', icon: 'pi-wallet', section: 'Business', description: 'This month’s due items across the fleet, clearable in bulk.' }),
        loadComponent: () => import('./pages/payables/payables.component').then((m) => m.PayablesComponent),
      },
      {
        path: 'documents',
        ...page('Documents', { permission: 'DOC.REGISTER.VIEW' }, { label: 'Documents', icon: 'pi-file', section: 'Business', description: 'The document register, what is missing, and what is expiring soon.' }),
        loadComponent: () => import('./pages/documents/documents-reports.component').then((m) => m.DocumentsReportsComponent),
      },
      { path: 'profile', ...page('My profile'), loadComponent: () => import('./pages/profile/profile.component').then((m) => m.ProfileComponent) },
      {
        path: 'users',
        ...page('Users', { permission: 'USER_VIEW' }, { label: 'Users', icon: 'pi-users', section: 'Administration', description: 'Invite people and manage their access.' }),
        loadComponent: () => import('./pages/users/users.component').then((m) => m.UsersComponent),
      },
      {
        path: 'roles',
        ...page('Roles', { permission: 'ROLE_VIEW' }, { label: 'Roles', icon: 'pi-shield', section: 'Administration', description: 'Define what each role is allowed to do.' }),
        loadComponent: () => import('./pages/roles/roles.component').then((m) => m.RolesComponent),
      },
      {
        path: 'roles/:id',
        ...page('Role', { permission: 'ROLE_VIEW' }),
        loadComponent: () => import('./pages/roles/role-detail.component').then((m) => m.RoleDetailComponent),
      },
      {
        path: 'admin/master-data',
        ...page('Master data', { permission: 'ADM_MASTER_MANAGE' }, { label: 'Master data', icon: 'pi-list', section: 'Administration', description: 'Maintain the lists behind every dropdown.' }),
        loadComponent: () => import('./pages/admin/lookups.component').then((m) => m.LookupsComponent),
      },
      {
        path: 'admin/bulk-documents',
        ...page('Bulk document upload', { permission: 'DOC.UPLOAD' }, { label: 'Bulk document upload', icon: 'pi-cloud-upload', section: 'Administration', description: 'Load paperwork for many vehicles and partners at once, for go-live.' }),
        loadComponent: () => import('./pages/admin/bulk-documents.component').then((m) => m.BulkDocumentsComponent),
      },
      {
        path: 'admin/notification-rules',
        ...page('Notification rules', { permission: 'ADM.NOTIFICATION.MANAGE' }, { label: 'Notification rules', icon: 'pi-bell', section: 'Administration', description: 'Lead days and recipients behind every alert.' }),
        loadComponent: () => import('./pages/admin/notification-rules.component').then((m) => m.NotificationRulesComponent),
      },
      {
        path: 'admin/ui-kit',
        ...page('UI kit', { superAdmin: true }, { label: 'UI kit', icon: 'pi-th-large', section: 'Administration', description: 'Reference for the shared components.' }),
        loadComponent: () => import('./pages/admin/ui-kit.component').then((m) => m.UiKitComponent),
      },
      {
        path: 'tenants',
        ...page('Tenants', { superAdmin: true }, { label: 'Tenants', icon: 'pi-building', section: 'Administration', description: 'Create and manage customer tenants.' }),
        loadComponent: () => import('./pages/tenants/tenants.component').then((m) => m.TenantsComponent),
      },
      { path: 'forbidden', title: 'Access denied', loadComponent: () => import('./pages/forbidden/forbidden.component').then((m) => m.ForbiddenComponent) },
      // Anything else: a proper "not found" inside the app, not a silent redirect that hides a broken link.
      { path: '**', title: 'Page not found', loadComponent: () => import('./pages/not-found/not-found.component').then((m) => m.NotFoundComponent) },
    ],
  },
];
