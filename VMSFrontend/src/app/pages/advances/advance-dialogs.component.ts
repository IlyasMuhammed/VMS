import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { AdvancesApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { CustomerAdvanceModel } from '../../core/advance.models';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { BankAccountPickerComponent } from '../../shared/bank-account-picker.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { OpenTripPickerComponent } from '../../shared/open-trip-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { PAYMENT_METHODS, optionsOf } from '../payments/payment-logic';

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

/**
 * New Advance (FSD §37.4: "From an Open trip's Payments tab or Receipts → New Advance"). Trip Detail has no
 * Payments tab of its own yet (deliberately deferred, see `TASKS.md`), so this is reached from the Advances
 * list's own "New Advance" button — the customer and Open trip are both picked here rather than assumed.
 */
@Component({
  selector: 'app-new-advance-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, SelectModule, TextareaModule, FieldComponent, DatePickerComponent, BankAccountPickerComponent, TripCustomerPickerComponent, OpenTripPickerComponent],
  template: `
    <p-dialog [visible]="adding()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '520px' }" header="New advance" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="Customer" [control]="form.controls.customerId" for="na-customer"><vms-trip-customer-picker formControlName="customerId" /></vms-field>
        <vms-field label="Trip" [control]="form.controls.tripId" for="na-trip" hint="Only this customer's own Open trips are offered."><vms-open-trip-picker [customerId]="form.controls.customerId.value" formControlName="tripId" /></vms-field>
        <vms-field label="Advance date" [control]="form.controls.advanceDate" for="na-date"><vms-date-picker inputId="na-date" formControlName="advanceDate" /></vms-field>
        <vms-field label="Amount" [control]="form.controls.amount" for="na-amount"><p-inputnumber inputId="na-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Method" [control]="form.controls.paymentMethod" for="na-method"><p-select inputId="na-method" formControlName="paymentMethod" [options]="methodOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        <vms-field label="Received in" [control]="form.controls.bankCashAccountId" for="na-account"><vms-bank-account-picker formControlName="bankCashAccountId" /></vms-field>
        <vms-field label="Instrument no." [control]="form.controls.instrumentNo" for="na-instrument"><input pInputText id="na-instrument" formControlName="instrumentNo" /></vms-field>
        @if (form.controls.paymentMethod.value === 'BankCheque') {
          <vms-field label="Instrument date" [control]="form.controls.instrumentDate" for="na-instdate"><vms-date-picker inputId="na-instdate" formControlName="instrumentDate" /></vms-field>
          <vms-field label="Drawn on bank" [control]="form.controls.drawnOnBank" for="na-drawn"><input pInputText id="na-drawn" formControlName="drawnOnBank" /></vms-field>
        }
        <vms-field label="Reference" [control]="form.controls.paymentReference" for="na-ref"><input pInputText id="na-ref" formControlName="paymentReference" /></vms-field>
        <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="na-remarks"><textarea pTextarea id="na-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Record advance" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; } .alert { margin-bottom: 1rem; }`],
})
export class NewAdvanceDialogComponent extends ActionDialog {
  private readonly api = inject(AdvancesApi);
  private readonly fb = inject(FormBuilder);

  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  protected readonly methodOptions = optionsOf(PAYMENT_METHODS);

  readonly form = this.fb.group({
    customerId: this.fb.control<number | null>(null, Validators.required),
    tripId: this.fb.control<number | null>(null, Validators.required),
    advanceDate: this.fb.control<string | null>(null, Validators.required),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    paymentMethod: this.fb.nonNullable.control<'DirectToAccount' | 'BankCheque'>('DirectToAccount'),
    bankCashAccountId: this.fb.control<number | null>(null, Validators.required),
    instrumentNo: this.fb.nonNullable.control('', Validators.required),
    instrumentDate: this.fb.control<string | null>(null),
    drawnOnBank: this.fb.nonNullable.control(''),
    paymentReference: this.fb.nonNullable.control(''),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      if (!this.adding()) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({
          customerId: null, tripId: null, advanceDate: null, amount: null, paymentMethod: 'DirectToAccount', bankCashAccountId: null,
          instrumentNo: '', instrumentDate: null, drawnOnBank: '', paymentReference: '', remarks: '',
        });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(
      this.api.create(f.tripId!, {
        advanceDate: f.advanceDate!, amount: f.amount!, paymentMethod: f.paymentMethod, bankCashAccountId: f.bankCashAccountId!, instrumentNo: f.instrumentNo.trim(),
        instrumentDate: f.instrumentDate, drawnOnBank: f.drawnOnBank.trim() || null, paymentReference: f.paymentReference.trim() || null, remarks: f.remarks.trim() || null,
      }),
      this.form,
      'Advance recorded',
      () => this.saved.emit(),
    );
  }
}

/** Move (FSD §37.4: "Finance can move the advance to another Open trip, reason, audited") — same customer only,
 * the trip it is already on excluded. */
@Component({
  selector: 'app-move-advance-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, TextareaModule, FieldComponent, OpenTripPickerComponent],
  template: `
    <p-dialog [visible]="!!advance()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '440px' }" [header]="'Move advance ' + (advance()?.advanceNumber ?? '')" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (advance(); as a) {
        <form [formGroup]="form" class="stack" novalidate>
          <vms-field label="Move to trip" [control]="form.controls.toTripId" for="ma-trip" hint="This customer's own other Open trips.">
            <vms-open-trip-picker [customerId]="a.customerId" [excludeTripId]="a.tripId" formControlName="toTripId" />
          </vms-field>
          <vms-field label="Reason" [control]="form.controls.reason" for="ma-reason"><textarea pTextarea id="ma-reason" formControlName="reason" rows="3" [fluid]="true"></textarea></vms-field>
        </form>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Move" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }`],
})
export class MoveAdvanceDialogComponent extends ActionDialog {
  private readonly api = inject(AdvancesApi);
  private readonly fb = inject(FormBuilder);

  readonly advance = input<CustomerAdvanceModel | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly form = this.fb.group({
    toTripId: this.fb.control<number | null>(null, Validators.required),
    reason: this.fb.nonNullable.control('', Validators.required),
  });

  constructor() {
    super();
    effect(() => {
      const a = this.advance();
      if (!a) return;
      untracked(() => { this.problems.set([]); this.form.reset({ toTripId: null, reason: '' }); });
    });
  }

  save(): void {
    const a = this.advance();
    if (!a || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.move(a.customerAdvanceId, { toTripId: f.toTripId!, reason: f.reason.trim() }), this.form, 'Advance moved', () => this.saved.emit());
  }
}
