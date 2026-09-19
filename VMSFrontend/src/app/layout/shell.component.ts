import { NgTemplateOutlet } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { TenantsApi } from '../core/api.services';
import { TenantBrandingService } from '../core/tenant-branding.service';
import { ThemeService } from '../core/theme/theme.service';
import { ThemeSwitcherComponent } from './theme-switcher.component';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  visible: boolean;
}

/**
 * One DOM structure, two layouts. The active theme decides whether the navigation is a sidebar
 * (theme A) or a top bar (theme B); only the tools (tenant, theme switcher, user) move between
 * containers, so switching theme never re-creates the routed page.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgTemplateOutlet, ThemeSwitcherComponent],
  template: `
    <div class="shell" [attr.data-layout]="layout()">
      <aside class="chrome">
        <div class="brand">
          <span class="logo">VMS</span>
          <span class="brand-text">Vehicle Management</span>
        </div>
        <nav aria-label="Main">
          @for (item of nav(); track item.route) {
            <a [routerLink]="item.route" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.route === '/' }">
              <i [class]="'pi ' + item.icon" aria-hidden="true"></i>
              <span>{{ item.label }}</span>
            </a>
          }
        </nav>
        @if (layout() === 'topbar') {
          <div class="tools">
            <ng-container *ngTemplateOutlet="tenantTpl" />
            <ng-container *ngTemplateOutlet="toolsTpl" />
          </div>
        }
      </aside>

      <div class="main">
        @if (layout() === 'sidebar') {
          <header class="topbar">
            <ng-container *ngTemplateOutlet="tenantTpl" />
            <div class="tools"><ng-container *ngTemplateOutlet="toolsTpl" /></div>
          </header>
        }
        <main class="content"><router-outlet /></main>
      </div>
    </div>

    <ng-template #tenantTpl>
      <div class="tenant">
        @if (logo(); as src) {
          <img class="tenant-logo" [src]="src" alt="" />
        } @else {
          <i class="pi pi-building" aria-hidden="true"></i>
        }
        <span>{{ tenantName() }}</span>
      </div>
    </ng-template>

    <ng-template #toolsTpl>
      <app-theme-switcher [onChrome]="layout() === 'topbar'" />
      <a routerLink="/profile" class="who">
        <i class="pi pi-user" aria-hidden="true"></i>
        <span>{{ fullName() }}</span>
        <small>{{ auth.user()?.role?.value }}</small>
      </a>
      <button type="button" class="logout" (click)="auth.logout()" title="Sign out" aria-label="Sign out">
        <i class="pi pi-sign-out" aria-hidden="true"></i>
      </button>
    </ng-template>
  `,
  styles: [
    `
      .shell { display: flex; min-height: 100vh; }
      .chrome { display: flex; flex-shrink: 0; background: var(--vms-chrome-bg); color: var(--vms-chrome-text); }
      .brand { display: flex; align-items: center; gap: .6rem; }
      .logo { background: var(--vms-brand); color: var(--vms-on-brand); font-weight: 700; border-radius: var(--vms-radius-sm); padding: .35rem .5rem; font-size: .9rem; }
      .brand-text { color: var(--vms-chrome-strong); font-weight: 600; white-space: nowrap; }
      nav { display: flex; gap: .15rem; }
      nav a span { white-space: nowrap; }
      nav a { display: flex; align-items: center; gap: .75rem; padding: .65rem .85rem; border-radius: var(--vms-radius-sm); color: inherit; text-decoration: none; }
      nav a:hover { background: var(--vms-chrome-hover); }
      nav a.active { background: var(--vms-chrome-active-bg); color: var(--vms-chrome-active-text); }

      .main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
      .topbar { height: 56px; background: var(--vms-surface); border-bottom: 1px solid var(--vms-border); display: flex; align-items: center; justify-content: space-between; padding: 0 1.5rem; }
      .content { flex: 1; }

      .tenant { display: flex; align-items: center; gap: .6rem; font-weight: 600; min-width: 0; }
      .tenant { --sep: var(--vms-border); }
      .tenant-logo { height: 28px; max-width: 140px; object-fit: contain; flex-shrink: 0; }
      .tenant span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 14rem; }
      .who span, .who small { white-space: nowrap; }
      /* A hairline between the logo and the name, so a wordmark logo and the name don't read as a repeat. */
      .tenant-logo + span { padding-left: .6rem; border-left: 1px solid var(--sep); }
      .tools { display: flex; align-items: center; gap: .75rem; --fg: var(--vms-text); --soft: var(--vms-muted); --chip: var(--vms-bg); }
      .who { display: flex; align-items: center; gap: .5rem; color: var(--fg); text-decoration: none; }
      .who small { color: var(--soft); background: var(--chip); border-radius: var(--vms-radius-pill); padding: .1rem .55rem; }
      .logout { border: 0; background: transparent; cursor: pointer; font-size: 1.1rem; color: var(--soft); }
      .logout:hover { color: var(--vms-danger); }

      /* Layout: sidebar (theme A) */
      .shell[data-layout='sidebar'] .chrome { width: 240px; flex-direction: column; border-right: 1px solid var(--vms-chrome-border); }
      .shell[data-layout='sidebar'] .brand { padding: 1.1rem 1.25rem; border-bottom: 1px solid var(--vms-chrome-border); }
      .shell[data-layout='sidebar'] nav { flex-direction: column; padding: .75rem; }

      /* Layout: top bar (theme B). The bar is dark in both modes. */
      .shell[data-layout='topbar'] { flex-direction: column; }
      .shell[data-layout='topbar'] .chrome { flex-direction: row; align-items: center; gap: 1.75rem; height: 56px; padding: 0 1.5rem; border-bottom: 1px solid var(--vms-chrome-border); }
      .shell[data-layout='topbar'] nav { align-self: stretch; }
      .shell[data-layout='topbar'] nav a { border-radius: 0; border-bottom: 2px solid transparent; padding: 0 .9rem; }
      .shell[data-layout='topbar'] nav a.active { background: transparent; border-bottom-color: var(--vms-chrome-accent); color: var(--vms-chrome-active-text); }
      .shell[data-layout='topbar'] .tools { margin-left: auto; --fg: var(--vms-chrome-text); --soft: var(--vms-chrome-muted); --chip: var(--vms-chrome-hover); }
      .shell[data-layout='topbar'] .tenant { --sep: var(--vms-chrome-border); color: var(--vms-chrome-muted); font-weight: 500; }

      /* The top bar holds the most items, so it drops the long brand text first. */
      @media (max-width: 1500px) {
        .shell[data-layout='topbar'] .brand-text { display: none; }
      }
      @media (max-width: 800px) {
        .shell[data-layout='sidebar'] .chrome { width: 64px; }
        .brand-text, nav a span, .who span, .who small, .tenant span { display: none; }
      }
    `,
  ],
})
export class ShellComponent implements OnInit, OnDestroy {
  readonly auth = inject(AuthService);
  private readonly tenants = inject(TenantsApi);
  private readonly branding = inject(TenantBrandingService);
  private readonly theme = inject(ThemeService);
  readonly layout = this.theme.layout;

  readonly tenantName = signal('');
  readonly fullName = computed(() => {
    const u = this.auth.user();
    return u ? [u.firstName, u.lastName].filter(Boolean).join(' ') : '';
  });

  /**
   * The tenant logo that suits the surface it sits on. A "light mode" logo is drawn for light
   * backgrounds and a "dark mode" one for dark backgrounds. In the sidebar layout the tenant sits
   * on the page surface, which follows the mode. The top bar layout is dark in both modes, so it
   * always wants the dark-mode logo. If only one logo exists it is used everywhere.
   */
  readonly logo = computed(() => {
    const wantsDark = this.layout() === 'topbar' || this.theme.resolvedMode() === 'dark';
    const [preferred, fallback] = wantsDark ? [this.branding.dark(), this.branding.light()] : [this.branding.light(), this.branding.dark()];
    return preferred ?? fallback;
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
    this.tenants.current().subscribe({
      next: (t) => {
        this.tenantName.set(t.tenantName);
        this.branding.apply(t);
      },
      error: () => this.tenantName.set(''),
    });
  }

  ngOnDestroy(): void {
    this.branding.clear();
  }
}
