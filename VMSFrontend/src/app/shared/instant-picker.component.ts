import { Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { DatePickerModule } from 'primeng/datepicker';
import { instantToApi, parseInstant } from '../core/datetime/datetime';

/**
 * A date-and-time field for an INSTANT the person chooses (when fuel was bought, when something actually
 * happened) — as opposed to {@link DatePickerComponent}'s business date, which has no time at all. Its value
 * is the ISO 8601 instant with a UTC offset the API requires (FSD NFR-DT-05), built from what the picker
 * shows on the viewer's own clock:
 * `<vms-instant-picker formControlName="fuelDateTime" [notFuture]="true" />`.
 * `null` means "not set" (a screen may default that to now on the server, never here — a blank field must
 * stay blank until the person actually opens the picker).
 */
@Component({
  selector: 'vms-instant-picker',
  standalone: true,
  imports: [FormsModule, DatePickerModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => InstantPickerComponent), multi: true }],
  template: `
    <p-datepicker
      [ngModel]="value()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      dateFormat="dd-M-yy"
      [showTime]="true"
      [showIcon]="true"
      iconDisplay="input"
      [showButtonBar]="true"
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
export class InstantPickerComponent implements ControlValueAccessor {
  /** Refuse a moment after right now. */
  readonly notFuture = input(false);
  readonly placeholder = input('dd-mmm-yyyy hh:mm');
  readonly inputId = input<string | undefined>(undefined);
  readonly ariaLabel = input<string | undefined>(undefined);

  protected readonly value = signal<Date | null>(null);
  protected readonly disabled = signal(false);
  protected readonly maxDate = computed(() => (this.notFuture() ? new Date() : null));

  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  protected pick(value: Date | null): void {
    this.value.set(value);
    this.onChange(value ? instantToApi(value) : null);
  }

  writeValue(value: string | null): void {
    this.value.set(value ? parseInstant(value) : null);
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
