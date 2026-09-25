import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { RoutesApi, TripConfigurationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { RouteModel } from '../../core/route.models';
import { TripConfigurationModel } from '../../core/trip-configuration.models';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { configStatusSeverity, words } from './trip-configuration-logic';

/**
 * Trip Configuration list (FSD §18, §48.3 screen 10): "Customer-first list" — there is deliberately no
 * all-customers endpoint (§18's own note), so a customer must be chosen before anything else loads.
 *
 * The FSD's own grid also lists "vehicles count" and "current rate" per row; neither is part of the list
 * response (`TripConfigurationModel` carries neither), and computing them here would mean a fetch per row for
 * every configuration on screen. Left out rather than built as an N+1 fan-out — both are one click away on the
 * configuration's own Vehicles and Rates tabs.
 */
@Component({
  selector: 'app-trip-configurations',
  standalone: true,
  imports: [FormsModule, ButtonModule, SelectModule, TagModule, TripCustomerPickerComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Trip Configurations</h1><div class="sub">A customer's fixed trips: route, allowed vehicles and rates.</div></div>
        <div class="header-actions">
          @if (canEdit() && customerId()) { <p-button label="Add configuration" icon="pi pi-plus" (onClick)="create()" /> }
        </div>
      </div>

      <div class="card">
        <div class="toolbar">
          <div class="field"><label>Customer *</label><vms-trip-customer-picker [ngModel]="customerId()" (ngModelChange)="customerId.set($event)" /></div>
          <div class="field"><label>Route</label><p-select [ngModel]="routeId()" (ngModelChange)="routeId.set($event)" [options]="routeOptions()" optionLabel="label" optionValue="value" [showClear]="true" placeholder="All routes" [fluid]="true" appendTo="body" /></div>
          <label class="check"><input type="checkbox" [ngModel]="showInactive()" (ngModelChange)="showInactive.set($event)" /> Show inactive</label>
        </div>

        @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

        @if (!customerId()) {
          <p class="muted">Choose a customer to see its trip configurations.</p>
        } @else {
          <table>
            <thead><tr><th>Trip code</th><th>Name</th><th>Route</th><th>Direction</th><th>Status</th><th></th></tr></thead>
            <tbody>
              @for (c of filtered(); track c.tripConfigurationId) {
                <tr>
                  <td class="mono">{{ c.tripCode }}</td><td>{{ c.name }}</td><td>{{ routeLabel(c.routeId) }}</td><td>{{ words(c.directionType) }}</td>
                  <td><p-tag [value]="c.status" [severity]="severity(c.status)" /></td>
                  <td class="row-actions"><p-button [label]="canEdit() ? 'Edit' : 'View'" [text]="true" size="small" (onClick)="open(c)" /></td>
                </tr>
              }
              @if (filtered().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'This customer has no trip configurations yet.' }}</td></tr> }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: [
    `
      .header-actions { display: flex; gap: .5rem; }
      .toolbar { display: flex; align-items: flex-end; gap: 1.25rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .field { display: flex; flex-direction: column; gap: .3rem; min-width: 16rem; } .field label { font-size: .8rem; color: var(--vms-muted); }
      .check { display: inline-flex; align-items: center; gap: .4rem; font-size: .9rem; padding-bottom: .5rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { text-align: right; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripConfigurationsComponent {
  private readonly api = inject(TripConfigurationsApi);
  private readonly routesApi = inject(RoutesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly canEdit = computed(() => this.auth.hasPermission('TRP.TRIPCONFIG.EDIT'));
  readonly customerId = signal<number | null>(null);
  readonly routeId = signal<number | null>(null);
  readonly showInactive = signal(false);
  readonly configs = signal<TripConfigurationModel[]>([]);
  readonly routes = signal<RouteModel[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  protected readonly severity = configStatusSeverity;
  protected readonly words = words;

  readonly routeOptions = computed(() => this.routes().filter((r) => r.status === 'Active').map((r) => ({ label: `${r.routeCode} — ${r.routeName}`, value: r.routeId })));
  readonly filtered = computed(() => {
    const rid = this.routeId();
    return this.configs().filter((c) => (rid === null || c.routeId === rid));
  });

  constructor() {
    this.routesApi.list(true).subscribe({ next: (rows) => this.routes.set(rows), error: () => undefined });
    effect(() => { this.customerId(); this.showInactive(); untracked(() => this.load()); });
  }

  load(): void {
    const id = this.customerId();
    if (!id) { this.configs.set([]); return; }
    this.loading.set(true);
    this.error.set(null);
    this.api.list(id, this.showInactive()).subscribe({
      next: (rows) => { this.loading.set(false); this.configs.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Trip configurations could not be loaded.')); },
    });
  }

  protected routeLabel(routeId: number): string {
    const r = this.routes().find((x) => x.routeId === routeId);
    return r ? `${r.routeCode} — ${r.routeName}` : `#${routeId}`;
  }

  create(): void {
    const id = this.customerId();
    if (id) void this.router.navigate(['/trip-configurations/new'], { queryParams: { customerId: id } });
  }

  open(c: TripConfigurationModel): void {
    void this.router.navigate(['/trip-configurations', c.tripConfigurationId]);
  }
}
