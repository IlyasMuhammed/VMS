import { Component, forwardRef, inject, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { BankAccountsApi } from '../core/api.services';

interface Option {
  label: string;
  value: number;
}

/**
 * Choose one of the company's own bank/cash accounts (FSD §37) — Record Payment, Refund and Record Advance all
 * point at one. Active accounts only; loaded once, the same shape as `vms-trip-city-picker`.
 *
 * ```html
 * <vms-bank-account-picker formControlName="bankCashAccountId" />
 * ```
 */
@Component({
  selector: 'vms-bank-account-picker',
  standalone: true,
  imports: [FormsModule, SelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => BankAccountPickerComponent), multi: true }],
  template: `
    <p-select
      [options]="options()"
      optionLabel="label"
      optionValue="value"
      [ngModel]="value()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      [filter]="options().length > 8"
      placeholder="Choose a bank account…"
      [showClear]="true"
      [disabled]="disabled()"
      [loading]="loading()"
      ariaLabel="Bank account"
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class BankAccountPickerComponent implements ControlValueAccessor {
  private readonly api = inject(BankAccountsApi);

  protected readonly value = signal<number | null>(null);
  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly disabled = signal(false);

  private onChange: (value: number | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    this.loading.set(true);
    this.api.list(true).subscribe({
      next: (accounts) => {
        this.loading.set(false);
        this.options.set(accounts.map((a) => ({ label: `${a.accountTitle} — ${a.bankName} (…${a.accountNumberLast4})${a.isActive ? '' : ' — Inactive'}`, value: a.bankCashAccountId })));
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
