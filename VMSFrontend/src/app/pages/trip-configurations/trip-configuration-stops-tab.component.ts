import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { AuthService } from '../../core/auth.service';
import { TripConfigurationsApi } from '../../core/api.services';
import { NotifyService } from '../../core/notify.service';
import { TripConfigurationModel, TripConfigurationStopInput } from '../../core/trip-configuration.models';
import { TripCityPickerComponent } from '../../shared/trip-city-picker.component';
import { moveStop, stopProblems } from '../routes/route-logic';

let nextRowId = 1;
interface StopRow { id: number; cityId: number | null; otherLocation: string; useOther: boolean; stopType: string }
const MIDDLE_TYPES = [{ label: 'Pickup', value: 'Pickup' }, { label: 'Via', value: 'Via' }, { label: 'Delivery', value: 'Delivery' }];

/**
 * Stops tab (FSD §18): "Copied from the Route on creation, then freely adjustable for this one customer" — an
 * extra site the route master doesn't know about, so each stop is either a City or free-text `Other location`,
 * never both. Reordering is the same up/down-button approach as the Route screen's own stops editor
 * (`moveStop`/`stopProblems`, shared from `route-logic.ts`).
 */
@Component({
  selector: 'app-trip-configuration-stops-tab',
  standalone: true,
  imports: [FormsModule, ButtonModule, InputTextModule, SelectModule, TripCityPickerComponent],
  template: `
    @for (p of problemsList(); track p) { <div class="alert warning small">{{ p }}</div> }

    <table>
      <thead><tr><th style="width:3rem">#</th><th>Location</th><th>City / Other</th><th>Stop type</th><th></th></tr></thead>
      <tbody>
        @for (s of stops(); track s.id; let i = $index, first = $first, last = $last) {
          <tr>
            <td>{{ i + 1 }}</td>
            <td>
              <label class="switch"><input type="radio" [checked]="!s.useOther" (change)="setUseOther(i, false)" [disabled]="!canEdit()" /> City</label>
              <label class="switch"><input type="radio" [checked]="s.useOther" (change)="setUseOther(i, true)" [disabled]="!canEdit()" /> Other</label>
            </td>
            <td>
              @if (s.useOther) {
                <input pInputText [ngModel]="s.otherLocation" (ngModelChange)="setOther(i, $event)" [ngModelOptions]="{ standalone: true }" [disabled]="!canEdit()" placeholder="e.g. Client Warehouse #3" />
              } @else {
                <vms-trip-city-picker [ngModel]="s.cityId" (ngModelChange)="setCity(i, $event)" [ngModelOptions]="{ standalone: true }" />
              }
            </td>
            <td>
              @if (first) { Origin } @else if (last) { Destination } @else {
                <p-select [ngModel]="s.stopType" (ngModelChange)="setType(i, $event)" [ngModelOptions]="{ standalone: true }" [options]="middleTypes" optionLabel="label" optionValue="value" [disabled]="!canEdit()" [fluid]="true" appendTo="body" />
              }
            </td>
            <td class="row-actions">
              @if (canEdit()) {
                <p-button icon="pi pi-chevron-up" [text]="true" size="small" [disabled]="first" (onClick)="move(i, -1)" ariaLabel="Move up" />
                <p-button icon="pi pi-chevron-down" [text]="true" size="small" [disabled]="last" (onClick)="move(i, 1)" ariaLabel="Move down" />
                <p-button icon="pi pi-trash" [text]="true" size="small" severity="danger" [disabled]="first || last" (onClick)="remove(i)" ariaLabel="Remove stop" />
              }
            </td>
          </tr>
        }
      </tbody>
    </table>
    @if (canEdit()) {
      <p-button label="Add stop" icon="pi pi-plus" [text]="true" size="small" (onClick)="add()" />
      <div class="savebar-inline"><p-button label="Save stops" icon="pi pi-check" [loading]="saving()" (onClick)="save()" /></div>
    }
  `,
  styles: [
    `
      table { width: 100%; border-collapse: collapse; margin-bottom: .75rem; } th, td { text-align: left; padding: .4rem .5rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: middle; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .switch { margin-right: .75rem; font-size: .85rem; display: inline-flex; align-items: center; gap: .3rem; }
      .alert { margin-bottom: 1rem; } .alert.small { padding: .4rem .65rem; font-size: .85rem; } .savebar-inline { margin-top: .5rem; }
    `,
  ],
})
export class TripConfigurationStopsTabComponent {
  private readonly api = inject(TripConfigurationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly configuration = input.required<TripConfigurationModel>();
  readonly canEdit = () => this.auth.hasPermission('TRP.TRIPCONFIG.EDIT');

  readonly stops = signal<StopRow[]>([]);
  readonly saving = signal(false);
  protected readonly middleTypes = MIDDLE_TYPES;

  protected readonly problemsList = computed(() => stopProblems(this.normalized(), false, this.sameEnds()));

  constructor() {
    effect(() => { this.configuration(); untracked(() => this.load()); });
  }

  private load(): void {
    const c = this.configuration();
    this.stops.set(c.stops.map((s) => ({ id: nextRowId++, cityId: s.cityId ?? null, otherLocation: s.otherLocation ?? '', useOther: s.cityId === null || s.cityId === undefined, stopType: s.stopType })));
  }

  private normalized(): { stopType: string }[] {
    const rows = this.stops();
    return rows.map((s, i) => ({ stopType: i === 0 ? 'Origin' : i === rows.length - 1 ? 'Destination' : s.stopType === 'Origin' || s.stopType === 'Destination' ? 'Via' : s.stopType }));
  }

  private sameEnds(): boolean {
    const rows = this.stops();
    if (rows.length < 2) return false;
    const a = rows[0], b = rows[rows.length - 1];
    return !a.useOther && !b.useOther && a.cityId !== null && a.cityId === b.cityId;
  }

  add(): void {
    this.stops.update((rows) => {
      const withoutLast = rows.slice(0, -1);
      const last = rows[rows.length - 1];
      return [...withoutLast, { id: nextRowId++, cityId: null, otherLocation: '', useOther: false, stopType: 'Via' }, last];
    });
  }

  remove(index: number): void {
    this.stops.update((rows) => rows.filter((_, i) => i !== index));
  }

  move(index: number, by: -1 | 1): void {
    this.stops.update((rows) => moveStop(rows, index, by));
  }

  setUseOther(index: number, useOther: boolean): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, useOther, cityId: useOther ? null : r.cityId, otherLocation: useOther ? r.otherLocation : '' } : r)));
  }

  setCity(index: number, cityId: number | null): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, cityId } : r)));
  }

  setOther(index: number, value: string): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, otherLocation: value } : r)));
  }

  setType(index: number, stopType: string): void {
    this.stops.update((rows) => rows.map((r, i) => (i === index ? { ...r, stopType } : r)));
  }

  save(): void {
    if (this.saving()) return;
    const rows = this.stops();
    if (rows.some((s) => (s.useOther && !s.otherLocation.trim()) || (!s.useOther && !s.cityId))) { this.notify.warn('Every stop needs a city or an other location.', 'Not saved'); return; }
    const inputs: TripConfigurationStopInput[] = rows.map((s, i) => ({
      cityId: s.useOther ? null : s.cityId,
      otherLocation: s.useOther ? s.otherLocation.trim() : null,
      stopType: i === 0 ? 'Origin' : i === rows.length - 1 ? 'Destination' : s.stopType === 'Origin' || s.stopType === 'Destination' ? 'Via' : s.stopType,
    }));
    this.saving.set(true);
    this.api.updateStops(this.configuration().tripConfigurationId, { stops: inputs }).subscribe({
      next: () => { this.saving.set(false); this.notify.success('Stops saved'); },
      error: (err) => { this.saving.set(false); this.notify.error(err); },
    });
  }
}
