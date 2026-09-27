import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { Observable } from 'rxjs';
import { CurrenciesApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { CurrencyModel } from '../../core/customer.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';

abstract class ActionDialog {
  protected readonly notify = inject(NotifyService);
  protected readonly messages = inject(MessagesService);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);

  protected run<T>(request: Observable<T>, form: AbstractControl, done: string, finished: (result: T) => void, onError?: (error: ApiFieldError) => boolean): void {
    this.busy.set(true);
    this.problems.set([]);
    request.subscribe({
      next: (result) => { this.busy.set(false); this.notify.success(done); finished(result); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        if (onError && found.length === 1 && onError(found[0])) return;
        this.problems.set(applyServerErrors(form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }

  protected invalid(form: AbstractControl): boolean {
    if (!form.invalid) return false;
    form.markAllAsTouched();
    return true;
  }
}

/** Add or edit a currency (FSD §13A, screen "Currency Setup"). The code is set once, at creation, and never
 * editable — every downstream table (trip rates, invoices, exchange rates) stamps it onto its own rows. */
@Component({
  selector: 'app-currency-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, InputNumberModule, SelectModule, FieldComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '440px' }" [header]="currency() ? 'Edit currency' : 'Add a currency'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Code" [control]="form.controls.currencyCode" for="cur-code" hint="3 letters, e.g. PKR. Cannot be changed once saved.">
          <input pInputText id="cur-code" formControlName="currencyCode" maxlength="3" style="text-transform: uppercase" [readonly]="!!currency()" />
        </vms-field>
        <vms-field label="Name" [control]="form.controls.currencyName" for="cur-name"><input pInputText id="cur-name" formControlName="currencyName" /></vms-field>
        <vms-field label="Symbol" [control]="form.controls.symbol" for="cur-symbol"><input pInputText id="cur-symbol" formControlName="symbol" maxlength="5" /></vms-field>
        <vms-field label="Decimal places" [control]="form.controls.decimalPlaces" for="cur-decimals"><p-inputnumber inputId="cur-decimals" formControlName="decimalPlaces" [min]="0" [max]="4" [useGrouping]="false" [fluid]="true" /></vms-field>
        @if (currency()) {
          <vms-field label="Status" [control]="form.controls.status" for="cur-status" [hint]="currency()?.isBase ? 'The base currency cannot be made Inactive.' : ''">
            <p-select inputId="cur-status" formControlName="status" [options]="statusOptions" optionLabel="label" optionValue="value" [disabled]="!!currency()?.isBase" [fluid]="true" appendTo="body" />
          </vms-field>
        }
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="currency() ? 'Save changes' : 'Add currency'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }`],
})
export class CurrencyDialogComponent extends ActionDialog {
  private readonly api = inject(CurrenciesApi);
  private readonly fb = inject(FormBuilder);

  readonly currency = input<CurrencyModel | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.currency());
  protected readonly statusOptions = [{ label: 'Active', value: 'Active' }, { label: 'Inactive', value: 'Inactive' }];

  readonly form = this.fb.group({
    currencyCode: this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]),
    currencyName: this.fb.nonNullable.control('', Validators.required),
    symbol: this.fb.nonNullable.control(''),
    decimalPlaces: this.fb.nonNullable.control(2, [Validators.min(0), Validators.max(4)]),
    status: this.fb.nonNullable.control<'Active' | 'Inactive'>('Active'),
  });

  constructor() {
    super();
    effect(() => {
      const c = this.currency();
      const adding = this.adding();
      if (!adding && !c) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(c
          ? { currencyCode: c.currencyCode, currencyName: c.currencyName, symbol: c.symbol ?? '', decimalPlaces: c.decimalPlaces, status: c.status === 'Active' ? 'Active' : 'Inactive' }
          : { currencyCode: '', currencyName: '', symbol: '', decimalPlaces: 2, status: 'Active' });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const body = { currencyCode: f.currencyCode.trim().toUpperCase(), currencyName: f.currencyName.trim(), symbol: f.symbol.trim() || null, decimalPlaces: f.decimalPlaces, status: f.status };
    const c = this.currency();
    const request = c ? this.api.update(c.currencyCode, body) : this.api.create(body);
    this.run(request, this.form, c ? 'Currency updated' : 'Currency added', () => this.saved.emit());
  }
}

/** Add an exchange rate (FSD §13A): always against the tenant's own current base currency, which the server
 * sets — this dialog only ever offers the "from" side. */
@Component({
  selector: 'app-exchange-rate-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, SelectModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '420px' }" header="Add an exchange rate" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="From currency" [control]="form.controls.fromCurrencyCode" for="xr-from" [hint]="'To ' + baseCurrencyCode() + ' (the tenant\\'s own base currency).'">
          <p-select inputId="xr-from" formControlName="fromCurrencyCode" [options]="fromOptions()" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Rate date" [control]="form.controls.rateDate" for="xr-date"><vms-date-picker inputId="xr-date" formControlName="rateDate" /></vms-field>
        <vms-field label="Rate" [control]="form.controls.rate" for="xr-rate"><p-inputnumber inputId="xr-rate" formControlName="rate" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="6" [fluid]="true" /></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Add rate" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }`],
})
export class ExchangeRateDialogComponent extends ActionDialog {
  private readonly api = inject(CurrenciesApi);
  private readonly fb = inject(FormBuilder);

  readonly adding = input(false);
  readonly currencies = input<CurrencyModel[]>([]);
  readonly baseCurrencyCode = input('');
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding());
  protected readonly fromOptions = computed(() => this.currencies().filter((c) => c.status === 'Active' && c.currencyCode !== this.baseCurrencyCode()).map((c) => ({ label: `${c.currencyCode} — ${c.currencyName}`, value: c.currencyCode })));

  readonly form = this.fb.group({
    fromCurrencyCode: this.fb.control<string | null>(null, Validators.required),
    rateDate: this.fb.control<string | null>(null, Validators.required),
    rate: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.000001)]),
  });

  constructor() {
    super();
    effect(() => {
      if (!this.adding()) return;
      untracked(() => { this.problems.set([]); this.form.reset({ fromCurrencyCode: null, rateDate: null, rate: null }); });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.createExchangeRate({ fromCurrencyCode: f.fromCurrencyCode!, rateDate: f.rateDate, rate: f.rate }), this.form, 'Exchange rate added', () => this.saved.emit());
  }
}
