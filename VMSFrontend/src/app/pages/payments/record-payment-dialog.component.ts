import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { PaymentsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { InvoiceModel } from '../../core/invoice.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { BankAccountPickerComponent } from '../../shared/bank-account-picker.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { PAYMENT_METHODS, SETTLEMENT_TYPES, optionsOf, remainingAfterPayment } from './payment-logic';

/**
 * Record Payment (FSD §37, §48.7). Overpayment and duplicate-instrument are both "soft block, confirm to
 * proceed" business rules (422 with a machine code, not a field error — the same `TAX_RULE_REPLACEMENT_CONFIRMATION`
 * shape from the Customer cluster) rather than ordinary validation, so they are caught here by `err.error.code`
 * and turned into a confirm-and-resubmit, not routed through the usual field-error path.
 */
@Component({
  selector: 'app-record-payment-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, SelectModule, CheckboxModule, TextareaModule, FieldComponent, DatePickerComponent, BankAccountPickerComponent],
  template: `
    <p-dialog [visible]="!!invoice()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '560px' }" [header]="'Record Payment · ' + (invoice()?.invoiceNumber ?? '')" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (invoice(); as inv) {
        <p class="totals">Net {{ inv.netAmount }} · Paid {{ inv.paidAmount }} · Balance {{ inv.balanceAmount }} {{ inv.currencyCode }}</p>
        <form [formGroup]="form" class="form-grid" novalidate>
          <vms-field label="Receipt date" [control]="form.controls.receiptDate" for="rp-date"><vms-date-picker inputId="rp-date" formControlName="receiptDate" /></vms-field>
          <vms-field label="Amount" [control]="form.controls.amount" for="rp-amount"><p-inputnumber inputId="rp-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
          <vms-field label="Method" [control]="form.controls.paymentMethod" for="rp-method"><p-select inputId="rp-method" formControlName="paymentMethod" [options]="methodOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
          <vms-field label="Received in" [control]="form.controls.bankCashAccountId" for="rp-account"><vms-bank-account-picker formControlName="bankCashAccountId" /></vms-field>
          <vms-field label="Instrument no." [control]="form.controls.instrumentNo" for="rp-instrument"><input pInputText id="rp-instrument" formControlName="instrumentNo" /></vms-field>
          @if (form.controls.paymentMethod.value === 'BankCheque') {
            <vms-field label="Instrument date" [control]="form.controls.instrumentDate" for="rp-instdate"><vms-date-picker inputId="rp-instdate" formControlName="instrumentDate" /></vms-field>
            <vms-field label="Drawn on bank" [control]="form.controls.drawnOnBank" for="rp-drawn"><input pInputText id="rp-drawn" formControlName="drawnOnBank" /></vms-field>
          }
          <vms-field label="Reference" [control]="form.controls.paymentReference" for="rp-ref"><input pInputText id="rp-ref" formControlName="paymentReference" /></vms-field>
          <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="rp-remarks"><textarea pTextarea id="rp-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>

          @if (remaining() > 0) {
            <div class="settle full">
              <label class="check"><p-checkbox formControlName="settle" [binary]="true" inputId="rp-settle-toggle" (onChange)="onSettleToggle()" /> <label for="rp-settle-toggle">Settle remaining balance</label></label>
              @if (form.controls.settle.value) {
                <div class="form-grid">
                  <vms-field label="Settlement type" [control]="form.controls.settlementType" for="rp-settletype"><p-select inputId="rp-settletype" formControlName="settlementType" [options]="settlementOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
                  <vms-field label="Settlement amount" [control]="form.controls.settlementAmount" for="rp-settleamt"><p-inputnumber inputId="rp-settleamt" formControlName="settlementAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
                  <vms-field class="full" label="Settlement reason" [control]="form.controls.settlementReason" for="rp-settlereason"><input pInputText id="rp-settlereason" formControlName="settlementReason" /></vms-field>
                </div>
              }
            </div>
          }
        </form>
        <p class="after">After save: Balance {{ afterBalance() }} {{ inv.currencyCode }}. A ledger credit will be posted against this invoice.</p>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Save" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }
      .totals { font-weight: 600; margin-top: 0; } .after { color: var(--vms-muted); font-size: .85rem; }
      .settle { margin: 1rem 0; padding: .75rem; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); }
      .check { display: inline-flex; align-items: center; gap: .4rem; margin-bottom: .5rem; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class RecordPaymentDialogComponent {
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
  protected readonly settlementOptions = optionsOf(SETTLEMENT_TYPES);

  readonly form = this.fb.group({
    receiptDate: this.fb.control<string | null>(null, Validators.required),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    paymentMethod: this.fb.nonNullable.control<'DirectToAccount' | 'BankCheque'>('DirectToAccount'),
    bankCashAccountId: this.fb.control<number | null>(null, Validators.required),
    instrumentNo: this.fb.nonNullable.control('', Validators.required),
    instrumentDate: this.fb.control<string | null>(null),
    drawnOnBank: this.fb.nonNullable.control(''),
    paymentReference: this.fb.nonNullable.control(''),
    remarks: this.fb.nonNullable.control(''),
    settle: this.fb.nonNullable.control(false),
    settlementType: this.fb.nonNullable.control<'WriteOff' | 'Discount'>('WriteOff'),
    settlementAmount: this.fb.control<number | null>(null),
    settlementReason: this.fb.nonNullable.control(''),
  });

  protected readonly remaining = computed(() => {
    const inv = this.invoice();
    const amount = this.form.controls.amount.value ?? 0;
    return inv ? remainingAfterPayment(inv.balanceAmount, amount) : 0;
  });
  protected readonly afterBalance = computed(() => {
    const inv = this.invoice();
    if (!inv) return 0;
    const amount = this.form.controls.amount.value ?? 0;
    const settled = this.form.controls.settle.value ? (this.form.controls.settlementAmount.value ?? this.remaining()) : 0;
    return inv.balanceAmount - amount - settled;
  });

  private confirmOverpayment = false;
  private confirmDuplicate = false;

  constructor() {
    effect(() => {
      if (!this.invoice()) return;
      untracked(() => {
        this.problems.set([]);
        this.confirmOverpayment = false;
        this.confirmDuplicate = false;
        this.form.reset({
          receiptDate: null, amount: null, paymentMethod: 'DirectToAccount', bankCashAccountId: null, instrumentNo: '', instrumentDate: null,
          drawnOnBank: '', paymentReference: '', remarks: '', settle: false, settlementType: 'WriteOff', settlementAmount: null, settlementReason: '',
        });
      });
    });
  }

  protected onSettleToggle(): void {
    if (this.form.controls.settle.value) this.form.controls.settlementAmount.setValue(this.remaining());
  }

  save(): void {
    const inv = this.invoice();
    if (!inv || this.busy()) return;
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    if (this.form.controls.settle.value && !this.form.controls.settlementReason.value.trim()) {
      this.form.controls.settlementReason.setErrors({ required: true });
      this.form.controls.settlementReason.markAsTouched();
      return;
    }

    this.busy.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    this.api.recordAgainstInvoice(inv.invoiceId, {
      receiptDate: f.receiptDate!, amount: f.amount!, paymentMethod: f.paymentMethod, bankCashAccountId: f.bankCashAccountId!, instrumentNo: f.instrumentNo.trim(),
      instrumentDate: f.instrumentDate, drawnOnBank: f.drawnOnBank.trim() || null, paymentReference: f.paymentReference.trim() || null, remarks: f.remarks.trim() || null,
      confirmOverpayment: this.confirmOverpayment, confirmDuplicate: this.confirmDuplicate,
      settleRemaining: f.settle ? { settlementType: f.settlementType, amount: f.settlementAmount, reason: f.settlementReason.trim() } : null,
    }).subscribe({
      next: () => { this.busy.set(false); this.notify.success('Payment recorded'); this.changed.emit(); },
      error: (err: unknown) => this.refused(err),
    });
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    if (err instanceof HttpErrorResponse && err.status === 422) {
      const code = (err.error as { code?: string } | null)?.code;
      const message = (err.error as { message?: string } | null)?.message ?? 'Continue?';
      if (code === 'OVERPAYMENT_CONFIRMATION_REQUIRED') {
        this.confirmOverpayment = true;
        this.notify.warn(message, 'Confirm and try again');
        return;
      }
      if (code === 'DUPLICATE_INSTRUMENT') {
        this.confirmDuplicate = true;
        this.notify.warn(message, 'Confirm and try again');
        return;
      }
    }
    const found = apiErrors(err);
    if (found.length === 0) { this.notify.error(err); return; }
    this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
  }
}
