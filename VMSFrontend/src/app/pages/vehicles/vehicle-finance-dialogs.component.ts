import { DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { VehiclesApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { SCANS } from '../../core/file-rules';
import { uploadFile } from '../../core/upload';
import { Installment, InstallmentPayment, LedgerEntry } from '../../core/vehicle.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { FileUploadComponent } from '../../shared/file-upload.component';
import { ActionDialog } from './vehicle-dialogs.component';
import { PAYMENT_MODES, optionsOf, words } from './vehicle-logic';

/**
 * Recording a payment against one installment of the schedule (source §7, §8). It may be less than what is expected: the rest stays due.
 * Once it is recorded the dialog offers a receipt (source §7); attaching one is optional and, once done, cannot be replaced from here —
 * a wrong receipt is corrected the same way a wrong amount is, by reversing the entry.
 */
@Component({
  selector: 'app-pay-installment-dialog',
  standalone: true,
  imports: [DecimalPipe, ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, InputNumberModule, InputTextModule, FieldComponent, DatePickerComponent, BusinessDatePipe, FileUploadComponent],
  template: `
    <p-dialog [visible]="!!installment()" (visibleChange)="!$event && close()" [modal]="true" [style]="{ width: '520px' }" [header]="paidResult() ? 'Attach a receipt' : 'Record payment'" [closable]="!busy()">
      @if (!paidResult()) {
        @if (installment(); as i) {
          <p class="muted">{{ i.isResidual ? 'Residual payment' : 'Installment ' + i.installmentNo }}, due {{ i.dueDate | vmsDate }}.
            @if (i.remainingAmount !== undefined) { PKR {{ i.remainingAmount | number: '1.2-2' }} is still to pay. }</p>
        }
        @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
        <form [formGroup]="form" class="stack" novalidate>
          <vms-field label="Amount paid (PKR)" [control]="form.controls.amount" for="pi-amount" hint="Part of the installment is fine; the rest stays due.">
            <p-inputnumber inputId="pi-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" />
          </vms-field>
          <vms-field label="Paid on" [control]="form.controls.paidOn" for="pi-date"><vms-date-picker inputId="pi-date" formControlName="paidOn" [notFuture]="true" /></vms-field>
          <vms-field label="Payment mode" [control]="form.controls.paymentMode" for="pi-mode" hint="Optional.">
            <p-select inputId="pi-mode" formControlName="paymentMode" [options]="modes" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" />
          </vms-field>
          <vms-field label="Reference" [control]="form.controls.reference" for="pi-ref" hint="A cheque or transaction number. Optional."><input pInputText id="pi-ref" formControlName="reference" autocomplete="off" /></vms-field>
        </form>
      } @else {
        <p class="muted">Payment recorded. A scanned cheque or receipt is optional, and can only be attached once — a wrong one is corrected by reversing the payment, not by replacing it here.</p>
        @if (attached()) {
          <p class="ok"><i class="pi pi-check-circle" aria-hidden="true"></i> Receipt attached.</p>
        } @else {
          <vms-file-upload [kinds]="scanKinds" label="Choose a receipt" [upload]="upload" (uploaded)="attached.set(true)" />
        }
      }
      <ng-template pTemplate="footer">
        @if (!paidResult()) {
          <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="close()" [disabled]="busy()" />
          <p-button label="Record payment" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
        } @else {
          <p-button [label]="attached() ? 'Done' : 'Skip'" [text]="!attached()" icon="pi pi-check" (onClick)="close()" />
        }
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .ok { color: var(--vms-success-text); font-weight: 600; display: flex; align-items: center; gap: .5rem; }`],
})
export class PayInstallmentDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  readonly vehicleId = input.required<number>();
  readonly installment = input<Installment | null>(null);
  /** The dialog is closing, whether or not a receipt was attached. */
  readonly closed = output<void>();
  /** The payment was recorded (before the receipt step): the caller can refresh its figures at once. */
  readonly paid = output<InstallmentPayment>();
  /** A receipt was attached to the payment just recorded. */
  readonly receiptAttached = output<void>();

  protected readonly modes = optionsOf(PAYMENT_MODES);
  protected readonly scanKinds = SCANS;
  protected readonly paidResult = signal<InstallmentPayment | null>(null);
  protected readonly attached = signal(false);
  readonly form = this.fb.group({
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    paidOn: this.fb.control<string | null>(null),
    paymentMode: this.fb.control<string | null>(null),
    reference: ['', Validators.maxLength(60)],
  });

  /** Given to `vms-file-upload`: where the receipt of the payment just recorded goes. */
  protected readonly upload = (file: File): ReturnType<typeof uploadFile> => {
    const i = this.installment();
    const paid = this.paidResult();
    if (!i || !paid) throw new Error('No payment to attach a receipt to.');
    return uploadFile(this.http, this.api.receiptUploadUrl(this.vehicleId(), i.id, paid.transactionId), file);
  };

  constructor() {
    super();
    effect(() => {
      const i = this.installment();
      if (i) untracked(() => { this.form.reset({ amount: i.remainingAmount ?? null, paidOn: null, paymentMode: null, reference: '' }); this.problems.set([]); this.paidResult.set(null); this.attached.set(false); });
    });
  }

  save(): void {
    const i = this.installment();
    if (!i || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(
      this.api.payInstallment(this.vehicleId(), i.id, { amount: f.amount, paidOn: f.paidOn, paymentMode: f.paymentMode, reference: (f.reference ?? '').trim() || null, rowVersion: i.rowVersion }),
      this.form, 'Payment recorded', (r) => { this.paidResult.set(r); this.paid.emit(r); },
    );
  }

  protected close(): void {
    if (this.busy()) return;
    if (this.attached()) this.receiptAttached.emit();
    this.closed.emit();
  }
}

/** The controlled adjustment route (BR-VH-011): the entry stays as it was, and a reversing entry is posted against it with the reason. */
@Component({
  selector: 'app-reverse-entry-dialog',
  standalone: true,
  imports: [DecimalPipe, ReactiveFormsModule, DialogModule, ButtonModule, TextareaModule, FieldComponent, DatePickerComponent, BusinessDatePipe],
  template: `
    <p-dialog [visible]="!!entry()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '520px' }" header="Reverse an entry" [closable]="!busy()">
      @if (entry(); as e) {
        <p class="muted">{{ words(e.type) }}@if (e.subType) { · {{ words(e.subType) }} } on {{ e.date | vmsDate }}@if (e.amount !== undefined) { , PKR {{ e.amount | number: '1.2-2' }} }.
          The entry stays in the ledger. A reversing entry is posted against it, and both are kept.</p>
      }
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Reason" [control]="form.controls.reason" for="re-reason"><textarea pTextarea id="re-reason" formControlName="reason" rows="3" [fluid]="true"></textarea></vms-field>
        <vms-field label="Reversed on" [control]="form.controls.date" for="re-date" hint="Today unless you enter a later date than the entry."><vms-date-picker inputId="re-date" formControlName="date" [notFuture]="true" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Reverse entry" icon="pi pi-undo" severity="danger" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class ReverseEntryDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly vehicleId = input.required<number>();
  readonly entry = input<LedgerEntry | null>(null);
  readonly closed = output<void>();
  readonly reversed = output<LedgerEntry>();
  protected readonly words = words;

  readonly form = this.fb.group({ reason: ['', [Validators.required, Validators.maxLength(500)]], date: this.fb.control<string | null>(null) });

  constructor() {
    super();
    effect(() => {
      if (this.entry()) untracked(() => { this.form.reset({ reason: '', date: null }); this.problems.set([]); });
    });
  }

  save(): void {
    const e = this.entry();
    if (!e || this.busy()) return;
    this.form.controls.reason.setValue((this.form.controls.reason.value ?? '').trim());
    if (this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.reverseTransaction(this.vehicleId(), e.id, { reason: f.reason ?? '', date: f.date }), this.form, 'Entry reversed', (r) => this.reversed.emit(r));
  }
}
