import { Component, effect, forwardRef, inject, input, signal, untracked } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { LookupsApi } from '../core/api.services';

interface Option {
  label: string;
  value: number | string;
  disabled?: boolean;
}

/**
 * A dropdown filled from a master list (Vehicle Type, City, …), for forms: `<vms-lookup-picker type="VEHICLE_TYPE" formControlName="typeId" />`.
 * It offers only values that can still be chosen, but if the record already holds a value that has since been
 * retired it shows it, marked "(retired)", instead of an empty box. Use `[cascade]` to depend on another field:
 * `<vms-lookup-picker type="CITY" valueField="code" [cascade]="{ province: form.value.province }" />` offers the cities of
 * that province and clears the choice when the province changes.
 */
@Component({
  selector: 'vms-lookup-picker',
  standalone: true,
  imports: [FormsModule, SelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => LookupPickerComponent), multi: true }],
  template: `
    <p-select
      [options]="options()"
      optionLabel="label"
      optionValue="value"
      optionDisabled="disabled"
      [ngModel]="value()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      [placeholder]="placeholder()"
      [showClear]="showClear()"
      [filter]="options().length > 8"
      [disabled]="disabled()"
      [loading]="loading()"
      [inputId]="inputId()"
      [ariaLabel]="ariaLabel()"
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class LookupPickerComponent implements ControlValueAccessor {
  private readonly api = inject(LookupsApi);

  /** The master list, for example `CITY`. */
  readonly type = input.required<string>();
  /** Store the value's `id` (default) or its `code`. */
  readonly valueField = input<'id' | 'code'>('id');
  /** Narrow the list by a field of its values, for example `{ province: 'PUNJAB' }`. Null or undefined values are ignored. */
  readonly cascade = input<Record<string, string | null | undefined> | null>(null);
  readonly placeholder = input('Choose…');
  readonly showClear = input(true);
  readonly inputId = input<string | undefined>(undefined);
  readonly ariaLabel = input<string | undefined>(undefined);

  protected readonly value = signal<number | string | null>(null);
  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly disabled = signal(false);

  private onChange: (value: number | string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;
  private generation = 0;

  constructor() {
    // Loads again when the list or the field it depends on changes; only the latest answer is used.
    effect(() => {
      const type = this.type();
      const filter = this.cascadeFilter();
      untracked(() => this.loadOptions(type, filter));
    });
  }

  private cascadeFilter(): Record<string, string> {
    const filter: Record<string, string> = {};
    for (const [key, value] of Object.entries(this.cascade() ?? {})) if (value) filter[key] = value;
    return filter;
  }

  private loadOptions(type: string, filter: Record<string, string>): void {
    const generation = ++this.generation;
    this.loading.set(true);
    this.api.active(type, filter).subscribe({
      next: (items) => {
        if (generation !== this.generation) return;
        const field = this.valueField();
        this.options.set(items.map((i) => ({ label: i.description, value: field === 'id' ? i.id : i.code })));
        this.loading.set(false);
        this.reconcile(filter);
      },
      error: () => {
        if (generation === this.generation) this.loading.set(false);
      },
    });
  }

  /**
   * After the options change: keep a chosen value that is still on offer; show one that was retired (marked);
   * drop one that is merely outside the current cascade (another province's city), so the form never holds a
   * value the screen cannot show.
   */
  private reconcile(filter: Record<string, string>): void {
    const current = this.value();
    if (current === null || this.options().some((o) => o.value === current)) return;

    if (this.valueField() === 'id' && typeof current === 'number') {
      this.api.one(this.type(), current).subscribe({
        next: (item) => {
          if (item.isActive) this.clear(); // exists, but not in this cascade
          else this.options.update((o) => [...o, { label: `${item.description} (retired)`, value: current, disabled: true }]);
        },
        error: () => undefined,
      });
    } else if (Object.keys(filter).length > 0) {
      this.clear();
    } else {
      this.options.update((o) => [...o, { label: `${current} (retired)`, value: current, disabled: true }]);
    }
  }

  private clear(): void {
    this.value.set(null);
    this.onChange(null);
  }

  protected pick(value: number | string | null): void {
    this.value.set(value);
    this.onChange(value);
  }

  writeValue(value: number | string | null): void {
    this.value.set(value ?? null);
    if (this.options().length > 0) this.reconcile(this.cascadeFilter());
  }

  registerOnChange(fn: (value: number | string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}
