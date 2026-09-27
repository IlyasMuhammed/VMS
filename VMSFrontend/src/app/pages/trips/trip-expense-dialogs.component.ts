import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { Observable } from 'rxjs';
import { TripOperationsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { FieldComponent } from '../../shared/field.component';
import { InstantPickerComponent } from '../../shared/instant-picker.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import { EXPENSE_PAYMENT_METHODS, optionsOf } from './trip-logic';

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

/** Log an expense (FSD §29, screen 18). Type comes from the shared `TRIP_EXPENSE_TYPE` lookup, seeded with a
 * "Fuel" entry the service itself refuses here ("use the Fuel screen") — not filtered out of the list, since the
 * lookup is shared platform-wide; the server's own rejection is the actual guard. */
@Component({
  selector: 'app-trip-expense-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, SelectModule, FieldComponent, InstantPickerComponent, LookupPickerComponent, PartnerPickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '560px' }" header="Log an expense" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="When" [control]="form.controls.expenseDate" for="ex-when" hint="Defaults to now."><vms-instant-picker inputId="ex-when" formControlName="expenseDate" [notFuture]="true" /></vms-field>
        <vms-field label="Expense type" [control]="form.controls.expenseTypeId" for="ex-type"><vms-lookup-picker type="TRIP_EXPENSE_TYPE" formControlName="expenseTypeId" inputId="ex-type" /></vms-field>
        <vms-field class="full" label="Description" [control]="form.controls.description" for="ex-desc"><input pInputText id="ex-desc" formControlName="description" /></vms-field>
        <vms-field label="Quantity" [control]="form.controls.quantity" for="ex-qty"><p-inputnumber inputId="ex-qty" formControlName="quantity" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Rate" [control]="form.controls.rate" for="ex-rate"><p-inputnumber inputId="ex-rate" formControlName="rate" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Amount" [control]="form.controls.amount" for="ex-amount" hint="Defaults to quantity × rate."><p-inputnumber inputId="ex-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Reference" [control]="form.controls.reference" for="ex-ref"><input pInputText id="ex-ref" formControlName="reference" /></vms-field>
        <vms-field label="Vendor" [control]="form.controls.businessPartnerId" for="ex-vendor"><vms-partner-picker [allowCreate]="false" formControlName="businessPartnerId" inputId="ex-vendor" /></vms-field>
        <vms-field label="Payment method" [control]="form.controls.paymentMethod" for="ex-method"><p-select inputId="ex-method" formControlName="paymentMethod" [options]="paymentMethods" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Log expense" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; } .alert { margin-bottom: 1rem; }`],
})
export class TripExpenseDialogComponent extends ActionDialog {
  private readonly api = inject(TripOperationsApi);
  private readonly fb = inject(FormBuilder);

  readonly tripId = input<number | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.tripId() !== null);
  protected readonly paymentMethods = optionsOf(EXPENSE_PAYMENT_METHODS);

  readonly form = this.fb.group({
    expenseDate: this.fb.control<string | null>(null),
    expenseTypeId: this.fb.control<number | null>(null, Validators.required),
    description: this.fb.nonNullable.control(''),
    quantity: this.fb.control<number | null>(null),
    rate: this.fb.control<number | null>(null),
    amount: this.fb.control<number | null>(null),
    reference: this.fb.nonNullable.control(''),
    businessPartnerId: this.fb.control<number | null>(null),
    paymentMethod: this.fb.nonNullable.control<'Cash' | 'Card' | 'Bank' | 'PaidByDriver' | 'Other'>('Cash'),
  });

  constructor() {
    super();
    effect(() => {
      if (this.tripId() === null) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ expenseDate: null, expenseTypeId: null, description: '', quantity: null, rate: null, amount: null, reference: '', businessPartnerId: null, paymentMethod: 'Cash' });
      });
    });
  }

  save(): void {
    const tripId = this.tripId();
    if (tripId === null || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.addExpense(tripId, {
      expenseDate: f.expenseDate, expenseTypeId: f.expenseTypeId!, description: f.description.trim() || null, quantity: f.quantity, rate: f.rate, amount: f.amount,
      reference: f.reference.trim() || null, businessPartnerId: f.businessPartnerId, paymentMethod: f.paymentMethod,
    }), this.form, 'Expense logged', () => this.saved.emit());
  }
}
