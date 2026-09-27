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
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { SETTLEMENT_TYPES, optionsOf } from './payment-logic';

/** Write-off / Discount (FSD §37.5, §48.8) — standalone, from Invoice Detail (also embeddable inside Record
 * Payment's own "Settle remaining balance," a separate dialog for that case). */
@Component({
  selector: 'app-settlement-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, SelectModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="!!invoice()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '440px' }" header="Write-off / Discount" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (invoice(); as inv) {
        <p class="totals">Balance {{ inv.balanceAmount }} {{ inv.currencyCode }}</p>
        <form [formGroup]="form" class="stack" novalidate>
          <vms-field label="Type" [control]="form.controls.settlementType" for="st-type"><p-select inputId="st-type" formControlName="settlementType" [options]="typeOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
          <vms-field label="Amount" [control]="form.controls.amount" for="st-amount" hint="Cannot exceed the invoice balance."><p-inputnumber inputId="st-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
          <vms-field label="Date" [control]="form.controls.settlementDate" for="st-date" hint="Defaults to today."><vms-date-picker inputId="st-date" formControlName="settlementDate" /></vms-field>
          <vms-field label="Reason" [control]="form.controls.reason" for="st-reason"><input pInputText id="st-reason" formControlName="reason" /></vms-field>
        </form>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Save" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .totals { font-weight: 600; margin-top: 0; } .alert { margin-bottom: 1rem; }`],
})
export class SettlementDialogComponent {
  private readonly api = inject(PaymentsApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly invoice = input<InvoiceModel | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();

  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  protected readonly typeOptions = optionsOf(SETTLEMENT_TYPES);

  readonly form = this.fb.group({
    settlementType: this.fb.nonNullable.control<'WriteOff' | 'Discount'>('WriteOff'),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    settlementDate: this.fb.control<string | null>(null),
    reason: this.fb.nonNullable.control('', Validators.required),
  });

  constructor() {
    effect(() => {
      const inv = this.invoice();
      if (!inv) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ settlementType: 'WriteOff', amount: Math.abs(inv.balanceAmount), settlementDate: null, reason: '' });
      });
    });
  }

  save(): void {
    const inv = this.invoice();
    if (!inv || this.busy() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.busy.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    this.api.createSettlement(inv.invoiceId, { settlementType: f.settlementType, amount: f.amount!, settlementDate: f.settlementDate, reason: f.reason.trim() }).subscribe({
      next: () => { this.busy.set(false); this.notify.success('Settlement recorded'); this.changed.emit(); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
