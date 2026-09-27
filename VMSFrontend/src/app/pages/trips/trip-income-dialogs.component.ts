import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { Observable } from 'rxjs';
import { TripOperationsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { applyServerErrors } from '../../shared/server-errors';

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

/** Record additional income (FSD §30). Customer defaults to the trip's own — a different, Active customer may
 * be billed instead. */
@Component({
  selector: 'app-trip-income-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, InputTextModule, CheckboxModule, FieldComponent, DatePickerComponent, LookupPickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" header="Record income" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Income type" [control]="form.controls.incomeTypeId" for="in-type"><vms-lookup-picker type="TRIP_INCOME_TYPE" formControlName="incomeTypeId" inputId="in-type" /></vms-field>
        <vms-field label="Amount" [control]="form.controls.amount" for="in-amount"><p-inputnumber inputId="in-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Date" [control]="form.controls.incomeDate" for="in-date" hint="Defaults to today."><vms-date-picker inputId="in-date" formControlName="incomeDate" /></vms-field>
        <vms-field label="Reference" [control]="form.controls.reference" for="in-ref"><input pInputText id="in-ref" formControlName="reference" /></vms-field>
        <div><p-checkbox formControlName="isBillable" [binary]="true" inputId="in-billable" /> <label for="in-billable">Billable to the customer</label></div>
        <vms-field label="Remarks" [control]="form.controls.remarks" for="in-remarks"><input pInputText id="in-remarks" formControlName="remarks" /></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Record income" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }`],
})
export class TripIncomeDialogComponent extends ActionDialog {
  private readonly api = inject(TripOperationsApi);
  private readonly fb = inject(FormBuilder);

  readonly tripId = input<number | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.tripId() !== null);

  readonly form = this.fb.group({
    incomeTypeId: this.fb.control<number | null>(null, Validators.required),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    incomeDate: this.fb.control<string | null>(null),
    reference: this.fb.nonNullable.control(''),
    isBillable: this.fb.nonNullable.control(true),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      if (this.tripId() === null) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ incomeTypeId: null, amount: null, incomeDate: null, reference: '', isBillable: true, remarks: '' });
      });
    });
  }

  save(): void {
    const tripId = this.tripId();
    if (tripId === null || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.addIncome(tripId, {
      incomeTypeId: f.incomeTypeId!, amount: f.amount!, incomeDate: f.incomeDate, isBillable: f.isBillable, reference: f.reference.trim() || null, remarks: f.remarks.trim() || null,
    }), this.form, 'Income recorded', () => this.saved.emit());
  }
}
