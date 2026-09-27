import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { TripOperationsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { FieldComponent } from '../../shared/field.component';
import { InstantPickerComponent } from '../../shared/instant-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import { TripCityPickerComponent } from '../../shared/trip-city-picker.component';
import { FUEL_PAYMENT_METHODS, FUEL_TYPES, fuelAmountMismatch, optionsOf } from './trip-logic';

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

/** Log fuel (FSD §27, screen 16). No fuel-card picker here beyond an id — the assigned card for this
 * vehicle/date is the server's own job to validate ("required when payment method is Fuel Card," §27); this
 * dialog just offers a plain number field, since building a full assignment-aware fuel-card picker for one field
 * on one tab is more machinery than the screen's own acceptance needs. */
@Component({
  selector: 'app-trip-fuel-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, SelectModule, TextareaModule, FieldComponent, InstantPickerComponent, TripCityPickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '560px' }" header="Log fuel" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      @if (mismatch()) { <div class="alert warning">The amount does not match quantity × rate. Continuing will log it as entered.</div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="When" [control]="form.controls.fuelDateTime" for="fu-when" hint="Defaults to now."><vms-instant-picker inputId="fu-when" formControlName="fuelDateTime" [notFuture]="true" /></vms-field>
        <vms-field label="Fuel type" [control]="form.controls.fuelType" for="fu-type"><p-select inputId="fu-type" formControlName="fuelType" [options]="fuelTypes" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        <vms-field label="Quantity (litres)" [control]="form.controls.quantity" for="fu-qty"><p-inputnumber inputId="fu-qty" formControlName="quantity" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Rate" [control]="form.controls.rate" for="fu-rate"><p-inputnumber inputId="fu-rate" formControlName="rate" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Amount" [control]="form.controls.amount" for="fu-amount" hint="Defaults to quantity × rate."><p-inputnumber inputId="fu-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Odometer" [control]="form.controls.odometer" for="fu-odo"><p-inputnumber inputId="fu-odo" formControlName="odometer" [useGrouping]="false" [fluid]="true" /></vms-field>
        <vms-field label="Station" [control]="form.controls.stationName" for="fu-station"><input pInputText id="fu-station" formControlName="stationName" /></vms-field>
        <vms-field label="City" [control]="form.controls.cityId" for="fu-city"><vms-trip-city-picker formControlName="cityId" /></vms-field>
        <vms-field label="Payment method" [control]="form.controls.paymentMethod" for="fu-method"><p-select inputId="fu-method" formControlName="paymentMethod" [options]="paymentMethods" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        @if (form.controls.paymentMethod.value === 'FuelCard') {
          <vms-field label="Fuel card id" [control]="form.controls.fuelCardId" for="fu-card"><p-inputnumber inputId="fu-card" formControlName="fuelCardId" [useGrouping]="false" [fluid]="true" /></vms-field>
        }
        @if (form.controls.paymentMethod.value === 'Other') {
          <vms-field label="Payment note" [control]="form.controls.otherPaymentText" for="fu-other"><input pInputText id="fu-other" formControlName="otherPaymentText" /></vms-field>
        }
        <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="fu-remarks"><textarea pTextarea id="fu-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Log fuel" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; } .alert { margin-bottom: 1rem; }`],
})
export class TripFuelDialogComponent extends ActionDialog {
  private readonly api = inject(TripOperationsApi);
  private readonly fb = inject(FormBuilder);

  readonly tripId = input<number | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.tripId() !== null);
  protected readonly fuelTypes = optionsOf(FUEL_TYPES);
  protected readonly paymentMethods = optionsOf(FUEL_PAYMENT_METHODS);

  readonly form = this.fb.group({
    fuelDateTime: this.fb.control<string | null>(null),
    fuelType: this.fb.nonNullable.control<'Diesel' | 'Petrol' | 'CNG' | 'Other'>('Diesel'),
    quantity: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    rate: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    amount: this.fb.control<number | null>(null),
    odometer: this.fb.control<number | null>(null),
    stationName: this.fb.nonNullable.control(''),
    cityId: this.fb.control<number | null>(null),
    paymentMethod: this.fb.nonNullable.control<'Cash' | 'FuelCard' | 'Other'>('Cash'),
    fuelCardId: this.fb.control<number | null>(null),
    otherPaymentText: this.fb.nonNullable.control(''),
    remarks: this.fb.nonNullable.control(''),
  });

  protected readonly mismatch = computed(() => {
    const f = this.form.getRawValue();
    return f.quantity && f.rate && f.amount ? fuelAmountMismatch(f.quantity, f.rate, f.amount) : false;
  });

  constructor() {
    super();
    effect(() => {
      if (this.tripId() === null) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ fuelDateTime: null, fuelType: 'Diesel', quantity: null, rate: null, amount: null, odometer: null, stationName: '', cityId: null, paymentMethod: 'Cash', fuelCardId: null, otherPaymentText: '', remarks: '' });
      });
    });
  }

  save(): void {
    const tripId = this.tripId();
    if (tripId === null || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.addFuel(tripId, {
      fuelDateTime: f.fuelDateTime, fuelType: f.fuelType, quantity: f.quantity!, rate: f.rate!, amount: f.amount, odometer: f.odometer,
      stationName: f.stationName.trim() || null, cityId: f.cityId, paymentMethod: f.paymentMethod, fuelCardId: f.fuelCardId, otherPaymentText: f.otherPaymentText.trim() || null, remarks: f.remarks.trim() || null,
    }), this.form, 'Fuel logged', () => this.saved.emit());
  }
}
