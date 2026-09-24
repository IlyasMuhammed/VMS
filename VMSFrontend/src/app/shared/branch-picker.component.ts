import { Component, forwardRef, inject, input, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { BranchesApi } from '../core/api.services';

interface Option {
  label: string;
  value: string;
}

/**
 * The tenant's own locations (FSD §6 field 15): `<vms-branch-picker formControlName="branchId" />`. A tenant starts with one
 * branch, "Head Office" (OQ-10), so this is usually a formality rather than a real choice, but it is written as a proper
 * picker (loaded once from `GET api/branches`) so nothing changes here when a client opens a second one.
 */
@Component({
  selector: 'vms-branch-picker',
  standalone: true,
  imports: [FormsModule, SelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => BranchPickerComponent), multi: true }],
  template: `
    <p-select
      [options]="options()"
      optionLabel="label"
      optionValue="value"
      [ngModel]="value()"
      (ngModelChange)="pick($event)"
      (onBlur)="onTouched()"
      [placeholder]="placeholder()"
      [showClear]="showClear()"
      [disabled]="disabled()"
      [loading]="loading()"
      [inputId]="inputId()"
      [ariaLabel]="ariaLabel()"
      [fluid]="true"
      appendTo="body"
    />
  `,
})
export class BranchPickerComponent implements ControlValueAccessor {
  private readonly api = inject(BranchesApi);

  readonly placeholder = input('Choose…');
  readonly showClear = input(true);
  readonly inputId = input<string | undefined>(undefined);
  readonly ariaLabel = input<string | undefined>(undefined);

  protected readonly value = signal<string | null>(null);
  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly disabled = signal(false);

  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (branches) => { this.loading.set(false); this.options.set(branches.map((b) => ({ label: b.name, value: b.id }))); },
      error: () => this.loading.set(false),
    });
  }

  protected pick(value: string | null): void {
    this.value.set(value);
    this.onChange(value);
  }

  writeValue(value: string | null): void {
    this.value.set(value ?? null);
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
