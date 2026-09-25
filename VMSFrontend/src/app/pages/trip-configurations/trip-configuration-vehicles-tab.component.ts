import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripConfigurationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { TripConfigurationVehicleModel } from '../../core/trip-configuration.models';
import { TripConfigurationVehicleDialogComponent } from './trip-configuration-vehicle-dialog.component';
import { statusSeverity } from './trip-configuration-logic';

/** Vehicles tab (FSD §19): every vehicle allowed on this configuration, effective-dated, several at once. */
@Component({
  selector: 'app-trip-configuration-vehicles-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, BusinessDatePipe, TripConfigurationVehicleDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Assign a vehicle" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Vehicle</th><th>Category</th><th>Effective</th><th>Status</th><th>Remarks</th><th></th></tr></thead>
      <tbody>
        @for (v of vehicles(); track v.tripConfigurationVehicleId) {
          <tr>
            <td>{{ v.registrationNo || '#' + v.vehicleId }}@if (v.vehicleCode) { <div class="muted small">{{ v.vehicleCode }}</div> }</td>
            <td>{{ v.category || '—' }}</td>
            <td class="nowrap">{{ v.effectiveFrom | vmsDate }}@if (v.effectiveTo) { – {{ v.effectiveTo | vmsDate }} }</td>
            <td><p-tag [value]="v.status" [severity]="severity(v.status)" /></td>
            <td>{{ v.remarks || '—' }}</td>
            <td class="row-actions">@if (canEdit()) { <p-button label="Edit" [text]="true" size="small" (onClick)="editing.set(v)" /> }</td>
          </tr>
        }
        @if (vehicles().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'No vehicles assigned yet — a configuration needs at least one active vehicle to be activated.' }}</td></tr> }
      </tbody>
    </table>

    <app-trip-configuration-vehicle-dialog [configurationId]="configurationId()" [adding]="addOpen()" [assignment]="editing()" (closed)="addOpen.set(false); editing.set(null)" (saved)="addOpen.set(false); editing.set(null); load()" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .small { font-size: .8rem; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripConfigurationVehiclesTabComponent {
  private readonly api = inject(TripConfigurationsApi);
  private readonly auth = inject(AuthService);

  readonly configurationId = input.required<number>();
  protected readonly vehicles = signal<TripConfigurationVehicleModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.TRIPCONFIG.EDIT');
  protected readonly severity = statusSeverity;

  protected readonly addOpen = signal(false);
  protected readonly editing = signal<TripConfigurationVehicleModel | null>(null);

  constructor() {
    effect(() => { this.configurationId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.vehicles(this.configurationId()).subscribe({
      next: (r) => { this.loading.set(false); this.vehicles.set(r); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Vehicles could not be loaded.')); },
    });
  }
}
