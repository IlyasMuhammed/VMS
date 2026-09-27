import { Component, effect, forwardRef, inject, input, signal, untracked } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { TripsApi } from '../core/api.services';

interface Option {
  label: string;
  value: number;
}

/**
 * Choose one of a Customer's own Open trips — used where an advance is recorded or moved (FSD §37.4: "From an
 * Open trip's Payments tab or Receipts → New Advance"; Move: "the advance to another Open trip"). Reloads
 * whenever `customerId` changes (clearing any selection made under the previous customer); `excludeTripId`
 * drops the trip the advance is already on, for Move.
 *
 * ```html
 * <vms-open-trip-picker [customerId]="customerId()" formControlName="tripId" />
 * ```
 * The value is the trip's id.
 */
@Component({
  selector: 'vms-open-trip-picker',
  standalone: true,
  imports: [FormsModule, SelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => OpenTripPickerComponent), multi: true }],
  template: `
    <p-select
      [options]="options()"
      optionLabel="label"
      optionValue="value"
      [ngModel]="value()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      [filter]="true"
      filterPlaceholder="Type a trip number"
      [filterFields]="['label']"
      placeholder="Choose an Open trip…"
      [showClear]="true"
      [disabled]="disabled() || !customerId()"
      [loading]="loading()"
      ariaLabel="Trip"
      emptyMessage="No Open trips for this customer."
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class OpenTripPickerComponent implements ControlValueAccessor {
  private readonly api = inject(TripsApi);

  readonly customerId = input<number | null>(null);
  readonly excludeTripId = input<number | null>(null);

  protected readonly value = signal<number | null>(null);
  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly disabled = signal(false);

  private onChange: (value: number | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    effect(() => {
      const customerId = this.customerId();
      const excludeTripId = this.excludeTripId();
      untracked(() => {
        this.value.set(null);
        this.onChange(null);
        if (customerId == null) {
          this.options.set([]);
          return;
        }
        this.loading.set(true);
        this.api.search({ customerId, tripType: 'Open', isActive: true }, 1, 200).subscribe({
          next: (page) => {
            this.loading.set(false);
            this.options.set(page.items.filter((t) => t.tripId !== excludeTripId).map((t) => ({ label: t.tripNumber, value: t.tripId })));
          },
          error: () => {
            this.loading.set(false);
            this.options.set([]);
          },
        });
      });
    });
  }

  protected pick(value: number | null): void {
    this.value.set(value);
    this.onChange(value);
  }

  writeValue(value: number | null): void {
    this.value.set(value ?? null);
  }

  registerOnChange(fn: (value: number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}
