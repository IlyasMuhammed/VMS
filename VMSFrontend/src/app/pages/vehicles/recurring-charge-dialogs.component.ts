import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { VehiclesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { RecurringCharge } from '../../core/vehicle.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { ActionDialog } from './vehicle-dialogs.component';
import { CHARGE_AMOUNT_BASES, CHARGE_FREQUENCIES, CHARGE_FREQUENCIES_WITH_DUE_DAY, CHARGE_POSTING_MODES, optionsOf } from './vehicle-logic';

/**
 * Adding a recurring charge, or amending one (FSD §19A.1). An amendment is never an edit in place: the server ends the current
 * row and starts a new one from the effective date (BR-VH-033), so an entry already generated keeps the terms that applied
 * when it was made. Auto-post is offered only to someone with the Auto-post permission, and only for a Fixed amount (BR-VH-027/028).
 */
@Component({
  selector: 'app-recurring-charge-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, InputTextModule, InputNumberModule, FieldComponent, DatePickerComponent, LookupPickerComponent, PartnerPickerComponent],
  template: `
    <p-dialog [visible]="!!open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '640px' }" [header]="charge() ? 'Amend recurring charge' : 'Add a recurring charge'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="Charge type" [control]="form.controls.chargeTypeId" for="rc-type"><vms-lookup-picker type="RECURRING_CHARGE_TYPE" formControlName="chargeTypeId" inputId="rc-type" placeholder="Choose a type" [showClear]="false" /></vms-field>
        <vms-field label="Payee" [control]="form.controls.payeeId" for="rc-payee" hint="Filtered by role where the charge type implies one — a Bank, a Tracker Company."><vms-partner-picker formControlName="payeeId" inputId="rc-payee" /></vms-field>
        <vms-field label="Expense type" [control]="form.controls.expenseTypeId" for="rc-expense"><vms-lookup-picker type="EXPENSE_TYPE" formControlName="expenseTypeId" inputId="rc-expense" placeholder="Choose…" [showClear]="false" /></vms-field>
        <vms-field label="Amount basis" [control]="form.controls.amountBasis" for="rc-basis">
          <p-select inputId="rc-basis" formControlName="amountBasis" [options]="bases" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Amount (PKR)" [control]="form.controls.amount" for="rc-amount" [hint]="form.controls.amountBasis.value === 'Variable' ? 'Optional: the generated entry pre-fills the last paid amount.' : ''">
          <p-inputnumber inputId="rc-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" />
        </vms-field>
        <vms-field label="Frequency" [control]="form.controls.frequency" for="rc-freq">
          <p-select inputId="rc-freq" formControlName="frequency" [options]="frequencies" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        @if (needsDueDay()) {
          <vms-field label="Due day" [control]="form.controls.dueDay" for="rc-day" hint="Day of month, 1 to 31. 29 to 31 falls back to month end.">
            <p-inputnumber inputId="rc-day" formControlName="dueDay" [min]="1" [max]="31" [useGrouping]="false" [fluid]="true" />
          </vms-field>
        }
        @if (form.controls.frequency.value === 'Yearly') {
          <vms-field label="Due month" [control]="form.controls.dueMonth" for="rc-month" hint="1 to 12.">
            <p-inputnumber inputId="rc-month" formControlName="dueMonth" [min]="1" [max]="12" [useGrouping]="false" [fluid]="true" />
          </vms-field>
        }
        @if (form.controls.frequency.value === 'CustomDays') {
          <vms-field label="Every (days)" [control]="form.controls.customIntervalDays" for="rc-interval">
            <p-inputnumber inputId="rc-interval" formControlName="customIntervalDays" [min]="1" [max]="365" [useGrouping]="false" [fluid]="true" />
          </vms-field>
        }
        <vms-field label="Start date" [control]="form.controls.startDate" for="rc-start" hint="Not before the vehicle's acquisition date."><vms-date-picker inputId="rc-start" formControlName="startDate" /></vms-field>
        <vms-field label="End date" [control]="form.controls.endDate" for="rc-end" hint="Optional. Open-ended until the vehicle is disposed."><vms-date-picker inputId="rc-end" formControlName="endDate" /></vms-field>
        <vms-field label="Number of occurrences" [control]="form.controls.occurrenceCount" for="rc-count" hint="Optional, instead of an end date.">
          <p-inputnumber inputId="rc-count" formControlName="occurrenceCount" [min]="1" [useGrouping]="false" [fluid]="true" />
        </vms-field>
        <vms-field label="Posting mode" [control]="form.controls.postingMode" for="rc-mode">
          <p-select inputId="rc-mode" formControlName="postingMode" [options]="postingModes" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        @if (form.controls.postingMode.value === 'AutoPost' && !canAutoPost()) {
          <div class="full hint-danger">Only an Admin may set a charge to Auto-post.</div>
        }
        <vms-field label="Generate lead days" [control]="form.controls.generateLeadDays" for="rc-lead" hint="How many days before the due date the entry is created. Default 7.">
          <p-inputnumber inputId="rc-lead" formControlName="generateLeadDays" [min]="0" [max]="90" [useGrouping]="false" [fluid]="true" />
        </vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="charge() ? 'Save changes' : 'Add charge'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; } .hint-danger { color: var(--vms-danger-text); font-size: .8rem; }`],
})
export class RecurringChargeDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly vehicleId = input.required<number>();
  /** The charge being amended, or null (with `adding` true) to add a new one. */
  readonly charge = input<RecurringCharge | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<RecurringCharge>();

  protected readonly open = computed(() => this.adding() || !!this.charge());
  protected readonly bases = optionsOf(CHARGE_AMOUNT_BASES);
  protected readonly frequencies = optionsOf(CHARGE_FREQUENCIES);
  protected readonly postingModes = optionsOf(CHARGE_POSTING_MODES);
  protected readonly canAutoPost = computed(() => this.auth.hasPermission('FIN.RECURRING.AUTOPOST'));
  protected readonly needsDueDay = computed(() => (CHARGE_FREQUENCIES_WITH_DUE_DAY as readonly string[]).includes(this.form.controls.frequency.value));

  readonly form = this.fb.group({
    chargeTypeId: this.fb.control<number | null>(null, Validators.required),
    payeeId: this.fb.control<number | null>(null, Validators.required),
    expenseTypeId: this.fb.control<number | null>(null, Validators.required),
    amountBasis: this.fb.nonNullable.control('Fixed', Validators.required),
    amount: this.fb.control<number | null>(null),
    frequency: this.fb.nonNullable.control('Monthly', Validators.required),
    dueDay: this.fb.control<number | null>(null),
    dueMonth: this.fb.control<number | null>(null),
    customIntervalDays: this.fb.control<number | null>(null),
    startDate: this.fb.control<string | null>(null, Validators.required),
    endDate: this.fb.control<string | null>(null),
    occurrenceCount: this.fb.control<number | null>(null),
    postingMode: this.fb.nonNullable.control('GenerateAsDue', Validators.required),
    generateLeadDays: this.fb.control<number | null>(7),
  });

  constructor() {
    super();
    effect(() => {
      const c = this.charge();
      const adding = this.adding();
      if (!adding && !c) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(c
          ? {
              chargeTypeId: c.chargeTypeId, payeeId: c.payee?.id ?? null, expenseTypeId: c.expenseTypeId, amountBasis: c.amountBasis, amount: c.amount ?? null, frequency: c.frequency,
              dueDay: c.dueDay ?? null, dueMonth: c.dueMonth ?? null, customIntervalDays: c.customIntervalDays ?? null, startDate: null, endDate: c.endDate ?? null,
              occurrenceCount: c.occurrenceCount ?? null, postingMode: c.postingMode, generateLeadDays: c.generateLeadDays,
            }
          : { chargeTypeId: null, payeeId: null, expenseTypeId: null, amountBasis: 'Fixed', amount: null, frequency: 'Monthly', dueDay: null, dueMonth: null, customIntervalDays: null, startDate: null, endDate: null, occurrenceCount: null, postingMode: 'GenerateAsDue', generateLeadDays: 7 });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const body = {
      chargeTypeId: f.chargeTypeId, payeeId: f.payeeId, expenseTypeId: f.expenseTypeId, amount: f.amount, amountBasis: f.amountBasis, frequency: f.frequency,
      dueDay: f.dueDay, dueMonth: f.dueMonth, customIntervalDays: f.customIntervalDays, startDate: f.startDate, endDate: f.endDate, occurrenceCount: f.occurrenceCount,
      postingMode: f.postingMode, generateLeadDays: f.generateLeadDays, taxWithholdingPercent: null,
    };
    const c = this.charge();
    const request = c ? this.api.amendRecurringCharge(this.vehicleId(), c.id, { ...body, rowVersion: c.rowVersion }) : this.api.createRecurringCharge(this.vehicleId(), body);
    this.run(request, this.form, c ? 'Charge updated' : 'Charge added', (saved) => this.saved.emit(saved));
  }
}

/** Ending a recurring charge by hand (§19A.5 "End-date"). It stops generating; entries already made are untouched. */
@Component({
  selector: 'app-end-charge-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, TextareaModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="!!charge()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" header="End this charge" [closable]="!busy()">
      <p class="muted">It will stop generating due entries. Entries already made are not affected.</p>
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="End date" [control]="form.controls.endDate" for="ec-date" hint="Today unless you choose another date."><vms-date-picker inputId="ec-date" formControlName="endDate" /></vms-field>
        <vms-field label="Reason" [control]="form.controls.reason" for="ec-reason"><textarea pTextarea id="ec-reason" formControlName="reason" rows="3" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" /><p-button label="End charge" icon="pi pi-stop-circle" severity="danger" [loading]="busy()" (onClick)="save()" /></ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class EndChargeDialogComponent extends ActionDialog {
  private readonly api = inject(VehiclesApi);
  private readonly fb = inject(FormBuilder);
  readonly vehicleId = input.required<number>();
  readonly charge = input<RecurringCharge | null>(null);
  readonly closed = output<void>();
  readonly ended = output<RecurringCharge>();

  readonly form = this.fb.group({ endDate: this.fb.control<string | null>(null), reason: ['', [Validators.required, Validators.maxLength(500)]] });

  constructor() {
    super();
    effect(() => { if (this.charge()) untracked(() => { this.form.reset({ endDate: null, reason: '' }); this.problems.set([]); }); });
  }

  save(): void {
    const c = this.charge();
    if (!c || this.busy()) return;
    this.form.controls.reason.setValue((this.form.controls.reason.value ?? '').trim());
    if (this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.endRecurringCharge(this.vehicleId(), c.id, { endDate: f.endDate, reason: f.reason ?? '' }), this.form, 'Charge ended', (r) => this.ended.emit(r));
  }
}
