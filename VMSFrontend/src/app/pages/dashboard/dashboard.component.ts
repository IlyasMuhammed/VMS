import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { buildNav } from '../../core/access';
import { PayablesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DecimalPipe, RouterLink],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Welcome, {{ auth.user()?.firstName }}</h1>
        </div>
      </div>

      @if (payablesSummary(); as s) {
        <a class="card summary-tile" routerLink="/payables" [class.alert-tile]="s.overdueCount > 0">
          <div>
            <div class="label">Due within 7 days</div>
            <div class="value">{{ s.dueWithin7Days }}@if (s.dueWithin7DaysAmount !== undefined) { <span class="amount"> · PKR {{ s.dueWithin7DaysAmount | number: '1.2-2' }}</span> }</div>
          </div>
          @if (s.overdueCount > 0) {
            <div>
              <div class="label">Overdue</div>
              <div class="value">{{ s.overdueCount }}@if (s.overdueAmount !== undefined) { <span class="amount"> · PKR {{ s.overdueAmount | number: '1.2-2' }}</span> }</div>
            </div>
          }
          <i class="pi pi-arrow-right" aria-hidden="true"></i>
        </a>
      }

      <div class="tiles">
        @for (t of tiles(); track t.route) {
          <a class="card tile" [routerLink]="t.route"><i [class]="'pi ' + t.icon" aria-hidden="true"></i><h3>{{ t.label }}</h3><p>{{ t.description }}</p></a>
        }
        <a class="card tile" routerLink="/profile"><i class="pi pi-user" aria-hidden="true"></i><h3>My profile</h3><p>View your details and change your password.</p></a>
      </div>
    </div>
  `,
  styles: [
    `
      .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 1rem; }
      .tile { text-decoration: none; color: inherit; transition: box-shadow .15s; }
      .tile:hover { box-shadow: var(--vms-shadow-md); }
      .tile i { font-size: 1.6rem; color: var(--vms-brand); }
      .tile h3 { margin: .6rem 0 .25rem; }
      .tile p { margin: 0; color: var(--vms-muted); }
      .summary-tile { display: flex; align-items: center; gap: 2rem; text-decoration: none; color: inherit; margin-bottom: 1rem; transition: box-shadow .15s; }
      .summary-tile:hover { box-shadow: var(--vms-shadow-md); }
      .summary-tile.alert-tile { border-color: var(--vms-danger-border); background: var(--vms-danger-bg); color: var(--vms-danger-text); }
      .summary-tile .label { font-size: .8rem; color: var(--vms-muted); } .summary-tile.alert-tile .label { color: inherit; }
      .summary-tile .value { font-size: 1.25rem; font-weight: 600; } .summary-tile .amount { font-size: .95rem; font-weight: 500; font-variant-numeric: tabular-nums; }
      .summary-tile .pi-arrow-right { margin-left: auto; color: var(--vms-muted); }
    `,
  ],
})
export class DashboardComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly payablesApi = inject(PayablesApi);

  /** A tile for every page the user may open that describes itself (see app.routes.ts), so new pages appear here by declaring it. */
  readonly tiles = computed(() =>
    buildNav(this.router.config, this.auth.user())
      .flatMap((g) => g.items)
      .filter((i) => i.description && i.route !== '/payables'),
  );

  /** The Payables Due tile (§19A.5): due within 7 days and overdue, fleet-wide. Only for someone who may see the workbench. */
  readonly payablesSummary = signal<{ dueWithin7Days: number; dueWithin7DaysAmount?: number; overdueCount: number; overdueAmount?: number } | null>(null);

  constructor() {
    if (this.auth.hasPermission('FIN.DUE.CONFIRM')) this.payablesApi.summary().subscribe({ next: (s) => this.payablesSummary.set(s), error: () => undefined });
  }
}
