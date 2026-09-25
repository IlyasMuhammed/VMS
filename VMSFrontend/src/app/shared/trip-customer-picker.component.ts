import { Component, forwardRef, inject, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { CustomersApi } from '../core/api.services';
import { CustomerPickerItem } from '../core/customer.models';

interface Option {
  label: string;
  value: number;
}

const labelOf = (c: { customerName: string; customerCode: string }): string => `${c.customerName} (${c.customerCode})`;

/**
 * Choose a Customer (the second FSD's own entity, §10 — not the Business Partner "Customer" role), searched on
 * the server as the person types, offering only Active customers (the picker endpoint's own scope).
 *
 * ```html
 * <vms-trip-customer-picker formControlName="customerId" />
 * ```
 * The value is the customer's id.
 */
@Component({
  selector: 'vms-trip-customer-picker',
  standalone: true,
  imports: [FormsModule, SelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => TripCustomerPickerComponent), multi: true }],
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
      filterPlaceholder="Type a customer name or code"
      [filterFields]="['label']"
      [resetFilterOnHide]="true"
      placeholder="Choose a customer…"
      [showClear]="true"
      [disabled]="disabled()"
      [loading]="loading()"
      ariaLabel="Customer"
      emptyFilterMessage="No customers match."
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class TripCustomerPickerComponent implements ControlValueAccessor {
  private readonly api = inject(CustomersApi);

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
          return this.api.picker(term).pipe(catchError(() => of([] as CustomerPickerItem[])));
        }),
      )
      .subscribe((found) => { this.loading.set(false); this.setOptions(found); });
    this.search.next('');
  }

  private setOptions(found: CustomerPickerItem[]): void {
    const options: Option[] = found.map((c) => ({ label: labelOf(c), value: c.customerId }));
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
      next: (c) => this.options.update((o) => (o.some((x) => x.value === id) ? o : [{ label: labelOf(c), value: id }, ...o])),
      error: () => this.options.update((o) => (o.some((x) => x.value === id) ? o : [{ label: `Customer #${id}`, value: id }, ...o])),
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
