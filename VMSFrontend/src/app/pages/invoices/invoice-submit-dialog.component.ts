import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { InvoicesApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { InvoiceModel } from '../../core/invoice.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';

/** Submit (FSD §36, §47.3): captures the ledger date and the channel it went out on — "no approval step," so
 * this is the one action, not a review-then-approve pair. Carries a fresh `Idempotency-Key` per dialog open
 * (§47.1: a retry with the same key safely replays the first response rather than submitting twice). */
@Component({
  selector: 'app-invoice-submit-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="!!invoice()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '440px' }" header="Submit invoice" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Submitted on" [control]="form.controls.submittedOn" for="si-date" hint="Defaults to today — the ledger date."><vms-date-picker inputId="si-date" formControlName="submittedOn" /></vms-field>
        <vms-field label="Submission channel" [control]="form.controls.submissionChannel" for="si-channel"><p-select inputId="si-channel" formControlName="submissionChannel" [options]="channelOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Submit" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }`],
})
export class InvoiceSubmitDialogComponent {
  private readonly api = inject(InvoicesApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly invoice = input<InvoiceModel | null>(null);
  readonly closed = output<void>();
  readonly changed = output<InvoiceModel>();

  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  protected readonly channelOptions = [{ label: 'Hand', value: 'Hand' }, { label: 'Email', value: 'Email' }, { label: 'Portal', value: 'Portal' }];

  private idempotencyKey = crypto.randomUUID();

  readonly form = this.fb.group({
    submittedOn: this.fb.control<string | null>(null),
    submissionChannel: this.fb.nonNullable.control<'Hand' | 'Email' | 'Portal'>('Hand'),
  });

  constructor() {
    effect(() => {
      if (!this.invoice()) return;
      untracked(() => {
        this.problems.set([]);
        this.idempotencyKey = crypto.randomUUID();
        this.form.reset({ submittedOn: null, submissionChannel: 'Hand' });
      });
    });
  }

  save(): void {
    const inv = this.invoice();
    if (!inv || this.busy()) return;
    this.busy.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    this.api.submit(inv.invoiceId, { rowVersion: inv.rowVersion, submittedOn: f.submittedOn, submissionChannel: f.submissionChannel }, this.idempotencyKey).subscribe({
      next: (r) => { this.busy.set(false); this.notify.success('Invoice submitted'); this.changed.emit(r); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(found.map((e) => this.messages.describe(e)));
      },
    });
  }
}
