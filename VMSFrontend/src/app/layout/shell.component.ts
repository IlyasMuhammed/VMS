import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { TenantsApi } from '../core/api.services';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  visible: boolean;
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <aside class="sidebar">
        <div class="brand">
          <span class="logo">VMS</span>
          <span class="brand-text">Vehicle Management</span>
        </div>
        <nav>
          @for (item of nav(); track item.route) {
            <a [routerLink]="item.route" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.route === '/' }">
              <i [class]="'pi ' + item.icon"></i>
              <span>{{ item.label }}</span>
            </a>
          }
        </nav>
      </aside>

      <div class="main">
        <header class="topbar">
          <div class="tenant">
            <i class="pi pi-building"></i>
            <span>{{ tenantName() }}</span>
          </div>
          <div class="user">
            <a routerLink="/profile" class="who">
              <i class="pi pi-user"></i>
              <span>{{ fullName() }}</span>
              <small>{{ auth.user()?.role?.value }}</small>
            </a>
            <button type="button" class="logout" (click)="auth.logout()" title="Sign out">
              <i class="pi pi-sign-out"></i>
            </button>
          </div>
        </header>
        <main class="content"><router-outlet /></main>
      </div>
    </div>
  `,
  styles: [
    `
      .shell { display: flex; min-height: 100vh; }
      .sidebar { width: 240px; background: var(--vms-sidebar); color: #cfd8f3; display: flex; flex-direction: column; flex-shrink: 0; }
      .brand { display: flex; align-items: center; gap: .6rem; padding: 1.1rem 1.25rem; border-bottom: 1px solid rgba(255,255,255,.08); }
      .logo { background: var(--vms-brand); color: #fff; font-weight: 700; border-radius: 8px; padding: .35rem .5rem; font-size: .9rem; }
      .brand-text { color: #fff; font-weight: 600; }
      nav { display: flex; flex-direction: column; padding: .75rem; gap: .15rem; }
      nav a { display: flex; align-items: center; gap: .75rem; padding: .65rem .85rem; border-radius: 8px; color: inherit; text-decoration: none; }
      nav a:hover { background: rgba(255,255,255,.07); }
      nav a.active { background: var(--vms-brand); color: #fff; }
      .main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
      .topbar { height: 56px; background: var(--vms-surface); border-bottom: 1px solid var(--vms-border); display: flex; align-items: center; justify-content: space-between; padding: 0 1.5rem; }
      .tenant { display: flex; align-items: center; gap: .5rem; font-weight: 600; }
      .user { display: flex; align-items: center; gap: .75rem; }
      .who { display: flex; align-items: center; gap: .5rem; color: inherit; text-decoration: none; }
      .who small { color: var(--vms-muted); background: var(--vms-bg); border-radius: 999px; padding: .1rem .55rem; }
      .logout { border: 0; background: transparent; cursor: pointer; font-size: 1.1rem; color: var(--vms-muted); }
      .logout:hover { color: var(--vms-danger); }
      .content { flex: 1; }
      @media (max-width: 800px) { .sidebar { width: 64px; } .brand-text, nav a span { display: none; } }
    `,
  ],
})
export class ShellComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly tenants = inject(TenantsApi);

  readonly tenantName = signal('');
  readonly fullName = computed(() => {
    const u = this.auth.user();
    return u ? [u.firstName, u.lastName].filter(Boolean).join(' ') : '';
  });

  readonly nav = computed<NavItem[]>(() =>
    [
      { label: 'Dashboard', icon: 'pi-home', route: '/', visible: true },
      { label: 'Users', icon: 'pi-users', route: '/users', visible: this.auth.hasPermission('USER_VIEW') },
      { label: 'Roles', icon: 'pi-shield', route: '/roles', visible: this.auth.hasPermission('ROLE_VIEW') },
      { label: 'Tenants', icon: 'pi-building', route: '/tenants', visible: this.auth.isSuperAdmin() },
    ].filter((i) => i.visible),
  );

  ngOnInit(): void {
    this.tenants.current().subscribe({ next: (t) => this.tenantName.set(t.tenantName), error: () => this.tenantName.set('') });
  }
}
