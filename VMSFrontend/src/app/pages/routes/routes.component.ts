import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { CitiesApi, RoutesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { CityModel } from '../../core/city.models';
import { RouteModel } from '../../core/route.models';
import { statusSeverity } from './route-logic';
import { cityDisplay } from '../cities/city-logic';

/** Route list (FSD §17, §48.3 screen 9): "List + editor". Not paginated server-side, like City — filtered
 * client-side instead. */
@Component({
  selector: 'app-routes',
  standalone: true,
  imports: [FormsModule, ButtonModule, InputTextModule, TagModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Routes</h1><div class="sub">The origin, stops and destination every trip configuration is built on.</div></div>
        <div class="header-actions">
          @if (canEdit()) { <p-button label="Add route" icon="pi pi-plus" (onClick)="create()" /> }
        </div>
      </div>

      @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

      <div class="card">
        <div class="toolbar">
          <input pInputText [ngModel]="search()" (ngModelChange)="search.set($event)" placeholder="Search code or name" style="max-width: 22rem" />
          <label class="check"><input type="checkbox" [ngModel]="showInactive()" (ngModelChange)="showInactive.set($event)" /> Show inactive</label>
        </div>

        <table>
          <thead><tr><th>Code</th><th>Name</th><th>Origin → Destination</th><th>Distance</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (r of filtered(); track r.routeId) {
              <tr>
                <td class="mono">{{ r.routeCode }}</td><td>{{ r.routeName }}</td>
                <td>{{ cityLabel(r.originCityId) }} → {{ cityLabel(r.destinationCityId) }}@if (r.isRoundTrip) { <span class="muted"> (round trip)</span> }</td>
                <td>{{ r.distanceKm ? r.distanceKm + ' km' : '—' }}</td>
                <td><p-tag [value]="r.status" [severity]="severity(r.status)" /></td>
                <td class="row-actions"><p-button [label]="canEdit() ? 'Edit' : 'View'" [text]="true" size="small" (onClick)="open(r)" /></td>
              </tr>
            }
            @if (filtered().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'No routes found.' }}</td></tr> }
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: [
    `
      .header-actions { display: flex; gap: .5rem; }
      .toolbar { display: flex; align-items: center; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .check { display: inline-flex; align-items: center; gap: .4rem; font-size: .9rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { text-align: right; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class RoutesComponent {
  private readonly api = inject(RoutesApi);
  private readonly citiesApi = inject(CitiesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly canEdit = computed(() => this.auth.hasPermission('TRP.ROUTE.EDIT'));
  readonly routes = signal<RouteModel[]>([]);
  readonly cityMap = signal<Map<number, CityModel>>(new Map());
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly search = signal('');
  readonly showInactive = signal(false);
  protected readonly severity = statusSeverity;

  readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.routes()
      .filter((r) => this.showInactive() || r.status === 'Active')
      .filter((r) => !term || r.routeCode.toLowerCase().includes(term) || r.routeName.toLowerCase().includes(term));
  });

  constructor() {
    this.citiesApi.list(true).subscribe({ next: (rows) => this.cityMap.set(new Map(rows.map((c) => [c.cityId, c]))), error: () => undefined });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list(true).subscribe({
      next: (rows) => { this.loading.set(false); this.routes.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Routes could not be loaded.')); },
    });
  }

  protected cityLabel(cityId: number): string {
    const c = this.cityMap().get(cityId);
    return c ? cityDisplay(c.cityName, c.abbreviation) : `#${cityId}`;
  }

  create(): void {
    void this.router.navigate(['/routes/new']);
  }

  open(r: RouteModel): void {
    void this.router.navigate(['/routes', r.routeId]);
  }
}
