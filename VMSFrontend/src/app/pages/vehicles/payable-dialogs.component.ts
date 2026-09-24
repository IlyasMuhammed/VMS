import { DecimalPipe } from '@angular/common';
import { Component, effect, inject, input, output, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { VehiclesApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { Payable } from '../../core/vehicle.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { ActionDialog } from './vehicle-dialogs.component';
import { PAYMENT_MODES, optionsOf, words } from './vehicle-logic';

/** Confirming a generated recurring charge entry (FSD §19A.4, BR-VH-031): the actual amount and date, which may differ from what was expected. */
@Component({
  selector: 'app-confirm-entry-dialog',
  standalone: true,
  imports: [DecimalPipe, ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, InputNumberModule, InputTextModule, FieldComponent, DatePickerComponent, BusinessDatePipe],
  template: `
    <p-dialog [visible]="!!entry()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" header="Confirm payment" [closable]="!busy()">
      @if (entry(); as e) {
        <p class="muted">{{ e.chargeType || words(e.kind) }}@if (e.payee) { for {{ e.payee.name }} }, due {{ e.dueDate | vmsDate }}.
          @if (e.expectedAmount !== undefined && e.expectedAmount !== null) { Expected PKR {{ e.expectedAmount | number: '1.2-2' }}. }</p>
      }
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Actual amount (PKR)" [control]="form.controls.amount" for="ce-amount"><p-inputnumber inputId="ce-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Paid on" [control]="form.controls.paidOn" for="ce-date"><vms-date-picker inputId="ce-date" formControlName="paidOn" [notFuture]="true" /></vms-field>
        <vms-field label="Payment mode" [control]="form.controls.paymentMode" for="ce-mode" hint="Optional.">
          <p-select inputId="ce-mode" formControlName="paymentMode" [options]="modes" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Reference" [control]="form.controls.reference" for="ce-ref" hint="Optional."><input pInputText id="ce-ref" formControlName="reference" autocomplete="off" /></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="Confirm payment" icon="pi pi-check" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class ConfirmEntryDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly vehicleId = input.required<number>();
  readonly entry = input<Payable | null>(null);
  readonly closed = output<void>();
  readonly confirmed = output<void>();
  protected readonly modes = optionsOf(PAYMENT_MODES);
  protected readonly words = words;

  readonly form = this.fb.group({
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    paidOn: this.fb.control<string | null>(null),
    paymentMode: this.fb.control<string | null>(null),
    reference: ['', Validators.maxLength(120)],
  });

  constructor() {
    super();
    effect(() => {
      const e = this.entry();
      if (e) untracked(() => { this.form.reset({ amount: e.expectedAmount ?? null, paidOn: null, paymentMode: null, reference: '' }); this.problems.set([]); });
    });
  }

  save(): void {
    const e = this.entry();
    if (!e || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(
      this.api.confirmChargeEntry(this.vehicleId(), e.id, { amount: f.amount, paidOn: f.paidOn, paymentMode: f.paymentMode, reference: (f.reference ?? '').trim() || null }),
      this.form, 'Payment recorded', () => this.confirmed.emit(),
    );
  }
}

/** Waiving or cancelling a Due or Overdue entry (§19A.4): it is set aside with a reason, not posted, not deleted. */
@Component({
  selector: 'app-waive-entry-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, TextareaModule, FieldComponent],
  template: `
    <p-dialog [visible]="!!entry()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '460px' }" [header]="kind() === 'cancel' ? 'Cancel this entry' : 'Waive this entry'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" novalidate>
        <vms-field label="Reason" [control]="form.controls.reason" for="we-reason"><textarea pTextarea id="we-reason" formControlName="reason" rows="3" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Back" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="kind() === 'cancel' ? 'Cancel entry' : 'Waive entry'" icon="pi pi-ban" severity="danger" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; }`],
})
export class WaiveEntryDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly vehicleId = input.required<number>();
  readonly entry = input<Payable | null>(null);
  readonly kind = input<'waive' | 'cancel'>('waive');
  readonly closed = output<void>();
  readonly done = output<void>();

  readonly form = this.fb.group({ reason: ['', [Validators.required, Validators.maxLength(500)]] });

  constructor() {
    super();
    effect(() => { if (this.entry()) untracked(() => { this.form.reset({ reason: '' }); this.problems.set([]); }); });
  }

  save(): void {
    const e = this.entry();
    if (!e || this.busy()) return;
    this.form.controls.reason.setValue((this.form.controls.reason.value ?? '').trim());
    if (this.invalid(this.form)) return;
    const reason = this.form.getRawValue().reason ?? '';
    const request = this.kind() === 'cancel' ? this.api.cancelChargeEntry(this.vehicleId(), e.id, { reason }) : this.api.waiveChargeEntry(this.vehicleId(), e.id, { reason });
    this.run(request, this.form, this.kind() === 'cancel' ? 'Entry cancelled' : 'Entry waived', () => this.done.emit());
  }
}
