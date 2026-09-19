import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Welcome, {{ auth.user()?.firstName }}</h1>
          <div class="sub">Signed in as {{ auth.user()?.role?.value }}</div>
        </div>
      </div>

      <div class="tiles">
        @if (auth.hasPermission('USER_VIEW')) {
          <a class="card tile" routerLink="/users"><i class="pi pi-users"></i><h3>Users</h3><p>Invite people and manage their access.</p></a>
        }
        @if (auth.hasPermission('ROLE_VIEW')) {
          <a class="card tile" routerLink="/roles"><i class="pi pi-shield"></i><h3>Roles</h3><p>Define what each role is allowed to do.</p></a>
        }
        @if (auth.isSuperAdmin()) {
          <a class="card tile" routerLink="/tenants"><i class="pi pi-building"></i><h3>Tenants</h3><p>Create and manage customer tenants.</p></a>
        }
        <a class="card tile" routerLink="/profile"><i class="pi pi-user"></i><h3>My profile</h3><p>View your details and change your password.</p></a>
      </div>
    </div>
  `,
  styles: [
    `
      .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 1rem; }
      .tile { text-decoration: none; color: inherit; transition: box-shadow .15s; }
      .tile:hover { box-shadow: 0 4px 14px rgba(15, 27, 61, .12); }
      .tile i { font-size: 1.6rem; color: var(--vms-brand); }
      .tile h3 { margin: .6rem 0 .25rem; }
      .tile p { margin: 0; color: var(--vms-muted); }
    `,
  ],
})
export class DashboardComponent {
  readonly auth = inject(AuthService);
}
