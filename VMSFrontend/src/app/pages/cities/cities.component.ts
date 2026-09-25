import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { CitiesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { CityModel } from '../../core/city.models';
import { CityDialogComponent } from './city-dialog.component';
import { statusSeverity } from './city-logic';

/**
 * City master list (FSD §16, §48.3 screen 8): "Master list + modal". Not paginated server-side — the endpoint
 * returns the whole list (a tenant's cities number in the dozens, not thousands) — so search and "show
 * inactive" both filter it client-side, the same shape as the platform's own Master data screen.
 */
@Component({
  selector: 'app-cities',
  standalone: true,
  imports: [FormsModule, ButtonModule, InputTextModule, TagModule, CityDialogComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Cities</h1><div class="sub">The city master behind every address, route and trip configuration.</div></div>
        <div class="header-actions">
          @if (canEdit()) { <p-button label="Add city" icon="pi pi-plus" (onClick)="addOpen.set(true)" /> }
        </div>
      </div>

      @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

      <div class="card">
        <div class="toolbar">
          <input pInputText [ngModel]="search()" (ngModelChange)="search.set($event)" placeholder="Search name, abbreviation or province" style="max-width: 22rem" />
          <label class="check"><input type="checkbox" [ngModel]="showInactive()" (ngModelChange)="showInactive.set($event)" /> Show inactive</label>
        </div>

        <table>
          <thead><tr><th>Name</th><th>Abbreviation</th><th>Province/State</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (c of filtered(); track c.cityId) {
              <tr>
                <td>{{ c.cityName }}</td><td class="mono">{{ c.abbreviation }}</td><td>{{ c.provinceState || '—' }}</td>
                <td><p-tag [value]="c.status" [severity]="severity(c.status)" /></td>
                <td class="row-actions">@if (canEdit()) { <p-button label="Edit" [text]="true" size="small" (onClick)="editing.set(c)" /> }</td>
              </tr>
            }
            @if (filtered().length === 0) { <tr><td colspan="5" class="muted">{{ loading() ? 'Loading…' : 'No cities found.' }}</td></tr> }
          </tbody>
        </table>
      </div>
    </div>

    <app-city-dialog [adding]="addOpen()" [city]="editing()" (closed)="addOpen.set(false); editing.set(null)" (saved)="addOpen.set(false); editing.set(null); load()" />
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
export class CitiesComponent {
  private readonly api = inject(CitiesApi);
  private readonly auth = inject(AuthService);

  readonly canEdit = computed(() => this.auth.hasPermission('TRP.CITY.EDIT'));
  readonly cities = signal<CityModel[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly search = signal('');
  readonly showInactive = signal(false);
  readonly addOpen = signal(false);
  readonly editing = signal<CityModel | null>(null);
  protected readonly severity = statusSeverity;

  readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.cities()
      .filter((c) => this.showInactive() || c.status === 'Active')
      .filter((c) => !term || c.cityName.toLowerCase().includes(term) || c.abbreviation.toLowerCase().includes(term) || (c.provinceState ?? '').toLowerCase().includes(term));
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list(true).subscribe({
      next: (rows) => { this.loading.set(false); this.cities.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Cities could not be loaded.')); },
    });
  }
}
