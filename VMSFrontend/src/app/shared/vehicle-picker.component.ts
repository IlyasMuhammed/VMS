import { Component, forwardRef, inject, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { VehiclesApi } from '../core/api.services';
import { VehicleListItem } from '../core/vehicle.models';

interface Option {
  label: string;
  value: number;
}

const labelOf = (v: { registrationNo: string; vehicleCode: string }): string => `${v.registrationNo} (${v.vehicleCode})`;

/**
 * Choose a vehicle by registration number or code, searched on the server as the person types — the same
 * server-search pattern as `vms-partner-picker`, for a form that points at one vehicle (the bulk document
 * loader, S8-QA-03).
 *
 * ```html
 * <vms-vehicle-picker formControlName="vehicleId" />
 * ```
 * The value is the vehicle's id.
 */
@Component({
  selector: 'vms-vehicle-picker',
  standalone: true,
  imports: [FormsModule, SelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => VehiclePickerComponent), multi: true }],
  template: `
    <p-select
      [options]="options()"
      optionLabel="label"
      optionValue="value"
      [ngModel]="value()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      (onFilter)="search.next($event.filter ?? '')"
      [filter]="true"
      filterPlaceholder="Type a registration number or code"
      [filterFields]="['label']"
      [resetFilterOnHide]="true"
      placeholder="Choose a vehicle…"
      [showClear]="true"
      [disabled]="disabled()"
      [loading]="loading()"
      ariaLabel="Vehicle"
      emptyFilterMessage="No vehicles match."
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class VehiclePickerComponent implements ControlValueAccessor {
  private readonly api = inject(VehiclesApi);

  protected readonly value = signal<number | null>(null);
  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly disabled = signal(false);
  protected readonly search = new Subject<string>();

  private onChange: (value: number | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    this.search
      .pipe(
        debounceTime(250),
        switchMap((term) => {
          this.loading.set(true);
          return this.api.list({ page: 1, pageSize: 20, search: term, filters: {} }).pipe(catchError(() => of({ items: [] as VehicleListItem[], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 })));
        }),
      )
      .subscribe((page) => {
        this.loading.set(false);
        this.setOptions(page.items);
      });
    this.search.next('');
  }

  private setOptions(found: VehicleListItem[]): void {
    const options: Option[] = found.map((v) => ({ label: labelOf(v), value: v.id }));
    const current = this.value();
    if (current !== null && !options.some((o) => o.value === current)) {
      const known = this.options().find((o) => o.value === current);
      if (known) options.unshift(known);
      else this.loadCurrent(current);
    }
    this.options.set(options);
  }

  private loadCurrent(id: number): void {
    this.api.get(id).subscribe({
      next: (v) => this.options.update((o) => (o.some((x) => x.value === id) ? o : [{ label: labelOf(v), value: id }, ...o])),
      error: () => this.options.update((o) => (o.some((x) => x.value === id) ? o : [{ label: `Vehicle #${id}`, value: id }, ...o])),
    });
  }

  protected pick(value: number | null): void {
    this.value.set(value);
    this.onChange(value);
  }

  writeValue(value: number | null): void {
    this.value.set(value ?? null);
    if (value !== null && value !== undefined && !this.options().some((o) => o.value === value)) this.loadCurrent(value);
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
