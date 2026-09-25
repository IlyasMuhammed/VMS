import { Component, forwardRef, inject, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { CitiesApi } from '../core/api.services';
import { cityDisplay } from '../pages/cities/city-logic';

interface Option {
  label: string;
  value: number;
  disabled?: boolean;
}

/**
 * Choose a city from the Trips module's own city master (FSD §16) — **not** the platform's generic `CITY`
 * lookup that `vms-lookup-picker type="CITY"` reads (the one a Business Partner or Customer address uses).
 * Routes, route stops and trip configuration stops all point at this richer master instead, since it is the
 * one with the abbreviation §17's route codes and preview strings (`LHR → SKP → FSD`) are built from.
 *
 * ```html
 * <vms-trip-city-picker formControlName="cityId" />
 * ```
 */
@Component({
  selector: 'vms-trip-city-picker',
  standalone: true,
  imports: [FormsModule, SelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => TripCityPickerComponent), multi: true }],
  template: `
    <p-select
      [options]="options()"
      optionLabel="label"
      optionValue="value"
      optionDisabled="disabled"
      [ngModel]="value()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      [filter]="options().length > 8"
      filterPlaceholder="Type a city or abbreviation"
      placeholder="Choose a city…"
      [showClear]="true"
      [disabled]="disabled()"
      [loading]="loading()"
      ariaLabel="City"
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class TripCityPickerComponent implements ControlValueAccessor {
  private readonly api = inject(CitiesApi);

  protected readonly value = signal<number | null>(null);
  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly disabled = signal(false);

  private onChange: (value: number | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    this.loading.set(true);
    this.api.list(true).subscribe({
      next: (cities) => {
        this.loading.set(false);
        this.options.set(cities.map((c) => ({ label: cityDisplay(c.cityName, c.abbreviation) + (c.status === 'Active' ? '' : ' — Inactive'), value: c.cityId, disabled: c.status !== 'Active' })));
      },
      error: () => this.loading.set(false),
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
