import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { PaymentsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { InvoiceModel } from '../../core/invoice.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { BankAccountPickerComponent } from '../../shared/bank-account-picker.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { PAYMENT_METHODS, optionsOf } from './payment-logic';

/** Refund (FSD §40, screen "Carry Forward / Refund" from a credit invoice): pay the credit back out. */
@Component({
  selector: 'app-refund-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, SelectModule, FieldComponent, DatePickerComponent, BankAccountPickerComponent],
  template: `
    <p-dialog [visible]="!!invoice()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '460px' }" header="Refund credit" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (invoice(); as inv) {
        <p class="totals">Available credit: {{ -inv.balanceAmount }} {{ inv.currencyCode }}</p>
        <form [formGroup]="form" class="stack" novalidate>
          <vms-field label="Refund date" [control]="form.controls.refundDate" for="rf-date" hint="Defaults to today."><vms-date-picker inputId="rf-date" formControlName="refundDate" /></vms-field>
          <vms-field label="Amount" [control]="form.controls.amount" for="rf-amount" hint="Cannot exceed the available credit."><p-inputnumber inputId="rf-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
          <vms-field label="Method" [control]="form.controls.paymentMethod" for="rf-method"><p-select inputId="rf-method" formControlName="paymentMethod" [options]="methodOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
          <vms-field label="Account" [control]="form.controls.bankCashAccountId" for="rf-account"><vms-bank-account-picker formControlName="bankCashAccountId" /></vms-field>
          <vms-field label="Reference" [control]="form.controls.reference" for="rf-ref"><input pInputText id="rf-ref" formControlName="reference" /></vms-field>
          <vms-field label="Reason" [control]="form.controls.reason" for="rf-reason"><input pInputText id="rf-reason" formControlName="reason" /></vms-field>
        </form>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Refund" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .totals { font-weight: 600; margin-top: 0; } .alert { margin-bottom: 1rem; }`],
})
export class RefundDialogComponent {
  private readonly api = inject(PaymentsApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly invoice = input<InvoiceModel | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();

  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  protected readonly methodOptions = optionsOf(PAYMENT_METHODS);

  readonly form = this.fb.group({
    refundDate: this.fb.control<string | null>(null),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    paymentMethod: this.fb.nonNullable.control<'DirectToAccount' | 'BankCheque'>('DirectToAccount'),
    bankCashAccountId: this.fb.control<number | null>(null, Validators.required),
    reference: this.fb.nonNullable.control(''),
    reason: this.fb.nonNullable.control('', Validators.required),
  });

  constructor() {
    effect(() => {
      const inv = this.invoice();
      if (!inv) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ refundDate: null, amount: Math.abs(inv.balanceAmount), paymentMethod: 'DirectToAccount', bankCashAccountId: null, reference: '', reason: '' });
      });
    });
  }

  save(): void {
    const inv = this.invoice();
    if (!inv || this.busy() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.busy.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    this.api.refund(inv.invoiceId, {
      refundDate: f.refundDate, amount: f.amount!, paymentMethod: f.paymentMethod, bankCashAccountId: f.bankCashAccountId!, reference: f.reference.trim() || null, reason: f.reason.trim(),
    }).subscribe({
      next: () => { this.busy.set(false); this.notify.success('Refund recorded'); this.changed.emit(); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
