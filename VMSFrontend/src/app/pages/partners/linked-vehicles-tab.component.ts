import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { VehiclesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { LinkedVehicle } from '../../core/vehicle.models';

const LINKS: Record<string, string> = { Owner: 'Other side of the ownership', Driver: 'Default driver', FuelCardCompany: 'Fuel card company', TrackerCompany: 'Tracker company', Supplier: 'Supplier of an item' };
const words = (v: string | null | undefined): string => (v ?? '').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/(?<=.) ([A-Z])/g, (m) => m.toLowerCase());

/** The vehicles a partner has to do with, now and before (FSD §9.2 Linked Vehicles tab). Read-only. */
@Component({
  selector: 'app-linked-vehicles-tab',
  standalone: true,
  imports: [RouterLink, ButtonModule, BusinessDatePipe],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    @if (rows().length === 0 && !error()) { <p class="muted">{{ loading() ? 'Loading…' : 'No vehicle is linked to this partner.' }}</p> }
    @else {
      <table>
        <thead><tr><th>Vehicle</th><th>Status</th><th>How</th><th>From</th><th>To</th></tr></thead>
        <tbody>
          @for (r of rows(); track $index) {
            <tr [class.past]="!r.isCurrent">
              <td><a class="mono" [routerLink]="['/vehicles', r.vehicleId]">{{ r.registrationNo }}</a><div class="muted small mono">{{ r.vehicleCode }}</div></td>
              <td>{{ word(r.vehicleStatus) }}</td>
              <td>{{ label(r.link) }}@if (r.detail) { <div class="muted small">{{ word(r.detail) }}</div> }</td>
              <td>{{ r.from ? (r.from | vmsDate) : '—' }}</td>
              <td>{{ r.isCurrent ? 'Now' : r.to ? (r.to | vmsDate) : '—' }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: [`table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; } .small { font-size: .8rem; } tr.past td { color: var(--vms-muted); } a.mono { font-weight: 600; }`],
})
export class LinkedVehiclesTabComponent {
  private readonly api = inject(VehiclesApi);
  readonly partnerId = input.required<number>();
  readonly rows = signal<LinkedVehicle[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  protected readonly label = (link: string): string => LINKS[link] ?? link;
  protected readonly word = words;

  constructor() {
    effect(() => { this.partnerId(); untracked(() => this.load()); });
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.linkedTo(this.partnerId()).subscribe({
      next: (rows) => { this.loading.set(false); this.rows.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'The linked vehicles could not be loaded.')); },
    });
  }
}
