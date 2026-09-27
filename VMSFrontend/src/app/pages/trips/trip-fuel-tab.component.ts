import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripOperationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { TripFuelListModel, TripFuelModel } from '../../core/trip-operations.models';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { TripFuelDialogComponent } from './trip-fuel-dialogs.component';
import { words } from './trip-logic';

/** Fuel tab (FSD §27, screen 16): every fuel entry, the running totals, and fuel efficiency once the trip has
 * both a start and end odometer. */
@Component({
  selector: 'app-trip-fuel-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, InstantPipe, TripFuelDialogComponent, ReasonDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Log fuel" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
      @if (list(); as l) {
        <span class="summary">Total: {{ l.totalQuantity }} L · {{ l.totalAmount }}
          @if (l.fuelEfficiencyKmPerLitre !== null && l.fuelEfficiencyKmPerLitre !== undefined) { · {{ l.fuelEfficiencyKmPerLitre }} km/L }
        </span>
      }
    </div>
    <table>
      <thead><tr><th>When</th><th>Type</th><th>Qty (L)</th><th>Rate</th><th>Amount</th><th>Method</th><th>Station</th><th></th></tr></thead>
      <tbody>
        @for (e of entries(); track e.tripFuelId) {
          <tr [class.voided]="e.isVoided">
            <td class="nowrap">{{ e.fuelDateTime | vmsInstant }}</td><td>{{ words(e.fuelType) }}</td><td>{{ e.quantity }}</td><td>{{ e.rate }}</td><td>{{ e.amount }} {{ e.currencyCode }}</td>
            <td>{{ words(e.paymentMethod) }}</td><td>{{ e.stationName || '—' }}</td>
            <td class="row-actions">
              @if (e.isVoided) { <p-tag value="Voided" severity="danger" /> }
              @else if (canEdit()) { <p-button label="Void" [text]="true" size="small" severity="danger" (onClick)="voiding.set(e)" /> }
            </td>
          </tr>
        }
        @if (entries().length === 0) { <tr><td colspan="8" class="muted">{{ loading() ? 'Loading…' : 'No fuel logged yet.' }}</td></tr> }
      </tbody>
    </table>

    <app-trip-fuel-dialog [tripId]="addOpen() ? tripId() : null" (closed)="addOpen.set(false)" (saved)="addOpen.set(false); load()" />
    <vms-reason-dialog [open]="!!voiding()" title="Void fuel entry" confirmLabel="Void" [minLength]="10" [busy]="voidBusy()" (closed)="voiding.set(null)" (confirmed)="doVoid($event)" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; } .summary { color: var(--vms-muted); font-size: .9rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      tr.voided td { color: var(--vms-muted); text-decoration: line-through; } .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripFuelTabComponent {
  private readonly api = inject(TripOperationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly tripId = input.required<number>();
  protected readonly list = signal<TripFuelListModel | null>(null);
  protected readonly entries = () => this.list()?.entries ?? [];
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.FUEL.EDIT');
  protected readonly words = words;

  protected readonly addOpen = signal(false);
  protected readonly voiding = signal<TripFuelModel | null>(null);
  protected readonly voidBusy = signal(false);

  constructor() {
    effect(() => { this.tripId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.fuel(this.tripId()).subscribe({
      next: (l) => { this.loading.set(false); this.list.set(l); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Fuel entries could not be loaded.')); },
    });
  }

  protected doVoid(reason: string): void {
    const e = this.voiding();
    if (!e) return;
    this.voidBusy.set(true);
    this.api.voidFuel(e.tripFuelId, { reason }).subscribe({
      next: () => { this.voidBusy.set(false); this.voiding.set(null); this.notify.success('Fuel entry voided'); this.load(); },
      error: (err) => { this.voidBusy.set(false); this.notify.error(err); },
    });
  }
}
