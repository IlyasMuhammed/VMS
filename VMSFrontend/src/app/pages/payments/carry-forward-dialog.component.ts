import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { InvoicesApi, PaymentsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { InvoiceListItem, InvoiceModel } from '../../core/invoice.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';

/** Carry Forward (FSD §40, screen "Carry Forward / Refund" from a credit invoice): move the credit to another of
 * this customer's own open (Submitted) invoices — the target list is this customer's own Submitted invoices,
 * fetched via the same `InvoicesApi.search` the Invoice List screen uses, not a bespoke endpoint. */
@Component({
  selector: 'app-carry-forward-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, SelectModule, FieldComponent],
  template: `
    <p-dialog [visible]="!!invoice()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '460px' }" header="Carry Forward" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (invoice(); as inv) {
        <p class="totals">Available credit: {{ -inv.balanceAmount }} {{ inv.currencyCode }}</p>
        <form [formGroup]="form" class="stack" novalidate>
          <vms-field label="Target invoice" [control]="form.controls.targetInvoiceId" for="cf-target">
            <p-select inputId="cf-target" formControlName="targetInvoiceId" [options]="targetOptions()" optionLabel="label" optionValue="value" [loading]="loadingTargets()" placeholder="Choose an open invoice" [fluid]="true" appendTo="body" />
          </vms-field>
          <vms-field label="Amount" [control]="form.controls.amount" for="cf-amount" hint="Cannot exceed the available credit."><p-inputnumber inputId="cf-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
          <vms-field label="Reason" [control]="form.controls.reason" for="cf-reason"><input pInputText id="cf-reason" formControlName="reason" /></vms-field>
        </form>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Carry forward" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .totals { font-weight: 600; margin-top: 0; } .alert { margin-bottom: 1rem; }`],
})
export class CarryForwardDialogComponent {
  private readonly api = inject(PaymentsApi);
  private readonly invoicesApi = inject(InvoicesApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly invoice = input<InvoiceModel | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();

  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly loadingTargets = signal(false);
  readonly targets = signal<InvoiceListItem[]>([]);
  protected readonly targetOptions = () => this.targets().map((i) => ({ label: `${i.invoiceNumber} — balance ${i.balanceAmount} ${i.currencyCode}`, value: i.invoiceId }));

  readonly form = this.fb.group({
    targetInvoiceId: this.fb.control<number | null>(null, Validators.required),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    reason: this.fb.nonNullable.control('', Validators.required),
  });

  constructor() {
    effect(() => {
      const inv = this.invoice();
      if (!inv) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ targetInvoiceId: null, amount: Math.abs(inv.balanceAmount), reason: '' });
        this.loadingTargets.set(true);
        this.invoicesApi.search({ customerId: inv.customerId, status: 'Submitted' }, 1, 100).subscribe({
          next: (page) => { this.loadingTargets.set(false); this.targets.set(page.items.filter((i) => i.invoiceId !== inv.invoiceId)); },
          error: () => this.loadingTargets.set(false),
        });
      });
    });
  }

  save(): void {
    const inv = this.invoice();
    if (!inv || this.busy() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.busy.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    this.api.carryForward(inv.invoiceId, { targetInvoiceId: f.targetInvoiceId!, amount: f.amount!, reason: f.reason.trim() }).subscribe({
      next: () => { this.busy.set(false); this.notify.success('Credit carried forward'); this.changed.emit(); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
