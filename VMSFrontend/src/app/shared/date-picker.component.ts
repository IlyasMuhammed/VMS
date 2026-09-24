import { Component, OnInit, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { DatePickerModule } from 'primeng/datepicker';
import { parseDateOnly, toDateOnly, todayDateOnly } from '../core/datetime/datetime';

/**
 * A date field for business dates (acquisition date, due date, expiry). Its value is the plain `YYYY-MM-DD`
 * string the API uses, never a `Date`, so nothing can shift it by a time zone (FSD §24.4, NFR-DT-03):
 * `<vms-date-picker formControlName="acquisitionDate" [notFuture]="true" />`.
 * The calendar opens on the person's own today (NFR-DT-04), and "not in the future" is judged against it too.
 */
@Component({
  selector: 'vms-date-picker',
  standalone: true,
  imports: [FormsModule, DatePickerModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DatePickerComponent), multi: true }],
  template: `
    <p-datepicker
      [ngModel]="date()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      dateFormat="dd-M-yy"
      [showIcon]="true"
      iconDisplay="input"
      [showButtonBar]="true"
      [defaultDate]="today()"
      [minDate]="minDate()"
      [maxDate]="maxDate()"
      [placeholder]="placeholder()"
      [inputId]="inputId()"
      [ariaLabel]="ariaLabel()"
      [disabled]="disabled()"
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class DatePickerComponent implements ControlValueAccessor, OnInit {
  /** The earliest and latest day that can be chosen, as `YYYY-MM-DD`. */
  readonly min = input<string | null>(null);
  readonly max = input<string | null>(null);
  /** Refuse a day after the person's own today. */
  readonly notFuture = input(false);
  /** Start with today's date filled in, for a field that is nearly always today. */
  readonly prefillToday = input(false);
  readonly placeholder = input('dd-mmm-yyyy');
  readonly inputId = input<string | undefined>(undefined);
  readonly ariaLabel = input<string | undefined>(undefined);

  protected readonly date = signal<Date | null>(null);
  protected readonly disabled = signal(false);
  protected readonly today = computed(() => parseDateOnly(todayDateOnly()) ?? new Date());
  protected readonly minDate = computed(() => (this.min() ? parseDateOnly(this.min()!) : null));
  protected readonly maxDate = computed(() => {
    const explicit = this.max() ? parseDateOnly(this.max()!) : null;
    if (!this.notFuture()) return explicit;
    const today = this.today();
    return explicit && explicit < today ? explicit : today;
  });

  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  ngOnInit(): void {
    if (this.prefillToday() && !this.date()) queueMicrotask(() => this.pick(this.today()));
  }

  protected pick(value: Date | null): void {
    this.date.set(value);
    this.onChange(value ? toDateOnly(value) : null);
  }

  writeValue(value: string | null): void {
    this.date.set(value ? parseDateOnly(value) : null);
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}
