import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom, forkJoin } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { LookupsApi, VehiclesApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { LookupItem } from '../../core/models';
import { Acquisition, Agreement } from '../../core/vehicle.models';
import { ConfirmService } from '../../shared/confirm.service';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import {
  buildAcquisitionForm, buildFinanceForm, patchAcquisition, patchFinance, problemsIn, syncAcquisitionRules, syncFinanceRules, toAcquisitionRequest, toFinanceRequest,
} from './vehicle-finance-form';
import {
  ACQUISITION_TYPES, AGREEMENT_GRACE_DAYS, FINANCE_FREQUENCIES, FINANCE_NOT_RECONCILED, FULLY_PAID_CODE, PAYMENT_MODES, agreementDateLimit, optionsOf, reconciliation,
  showsFinanceBlock, suggestedFinanceAmount, totalPayable,
} from './vehicle-logic';

/**
 * Step 3 of the vehicle wizard: how the vehicle was acquired and what was paid (FSD §18.1), and, when a bank financed it, the
 * agreement (§18.2). Nothing is posted here: the block is kept on the Draft and becomes ledger rows when the vehicle is activated.
 * It saves through its own calls and tells the wizard when the vehicle row changed, so step 1 is never saved over it.
 */
@Component({
  selector: 'app-vehicle-finance-step',
  standalone: true,
  imports: [
    DecimalPipe, FormsModule, ReactiveFormsModule, ButtonModule, CheckboxModule, InputNumberModule, InputTextModule, SelectModule, FieldComponent, DatePickerComponent,
    PartnerPickerComponent,
  ],
  template: `
    @if (loadError(); as message) {
      <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
    } @else if (loading()) {
      <p class="muted">Loading…</p>
    } @else {
      @if (conflict(); as message) { <div class="alert warning conflict" role="alert"><span>{{ message }}</span><p-button label="Reload their version" size="small" (onClick)="load()" /></div> }
      @if (problems().length > 0) { <div class="alert error" role="alert"><strong>This could not be saved.</strong><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div> }

      <div [formGroup]="acquisition">
        <h2>Acquisition</h2>
        <p class="muted lead">Nothing is posted yet. These figures become the vehicle's opening entries when you activate it.</p>
        <div class="form-grid">
          <vms-field label="Acquisition date" [control]="acquisition.controls.acquisitionDate" for="vf-date" hint="The day the vehicle entered the fleet. Not in the future.">
            <vms-date-picker inputId="vf-date" formControlName="acquisitionDate" [notFuture]="true" />
          </vms-field>
          <vms-field label="Acquisition type" [control]="acquisition.controls.acquisitionType" for="vf-type">
            <p-select inputId="vf-type" formControlName="acquisitionType" [options]="acquisitionTypes" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
          </vms-field>
          <vms-field label="Seller or source" [control]="acquisition.controls.sellerId" for="vf-seller" hint="A vendor, a dealer or a person. Optional.">
            <vms-partner-picker formControlName="sellerId" inputId="vf-seller" [allowCreate]="false" />
          </vms-field>
          <div></div>
          <vms-field label="Purchase price" [control]="acquisition.controls.purchasePrice" for="vf-price" hint="Required for a self-owned or bank-leased vehicle.">
            <p-inputnumber inputId="vf-price" formControlName="purchasePrice" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
          </vms-field>
          <vms-field label="Amount paid at creation" [control]="acquisition.controls.amountPaid" for="vf-paid" hint="From nothing up to the purchase price.">
            <p-inputnumber inputId="vf-paid" formControlName="amountPaid" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
          </vms-field>
          @if (paidSomething()) {
            <vms-field label="Payment mode" [control]="acquisition.controls.paymentMode" for="vf-mode">
              <p-select inputId="vf-mode" formControlName="paymentMode" [options]="paymentModes" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
            </vms-field>
            <vms-field label="Payment reference" [control]="acquisition.controls.paymentReference" for="vf-ref" hint="A cheque or transaction number.">
              <input pInputText id="vf-ref" formControlName="paymentReference" autocomplete="off" />
            </vms-field>
          }
          <vms-field label="Registration cost" [control]="acquisition.controls.registrationCost" for="vf-reg" hint="Posted separately as a major expense.">
            <p-inputnumber inputId="vf-reg" formControlName="registrationCost" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
          </vms-field>
        </div>
      </div>

      <div class="financed">
        <p-checkbox [ngModel]="financed()" (ngModelChange)="setFinanced($event)" [binary]="true" inputId="vf-financed" />
        <label for="vf-financed">A bank finances this vehicle</label>
      </div>

      @if (financed()) {
        <div [formGroup]="finance">
          <h2>Bank finance</h2>
          <div class="form-grid">
            <vms-field label="Finance type" [control]="finance.controls.financeTypeId" for="vf-ftype">
              <p-select inputId="vf-ftype" formControlName="financeTypeId" [options]="financeTypes()" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
            </vms-field>
            <vms-field label="Bank" [control]="finance.controls.bankId" for="vf-bank" hint="Only partners with the Bank role.">
              <vms-partner-picker role="Bank" formControlName="bankId" inputId="vf-bank" [showClear]="false" />
            </vms-field>
            <vms-field label="Agreement number" [control]="finance.controls.agreementNo" for="vf-agr" hint="Unique for the bank.">
              <input pInputText id="vf-agr" formControlName="agreementNo" autocomplete="off" />
            </vms-field>
            <vms-field label="Agreement date" [control]="finance.controls.agreementDate" for="vf-agrdate" [hint]="'Not after ' + (agreementLimit() ?? 'the acquisition date plus ' + graceDays + ' days') + '.'">
              <vms-date-picker inputId="vf-agrdate" formControlName="agreementDate" [max]="agreementLimit()" />
            </vms-field>
            <vms-field label="Finance amount" [control]="finance.controls.financeAmount" for="vf-famt" hint="Usually the price less the down payment.">
              <p-inputnumber inputId="vf-famt" formControlName="financeAmount" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
            </vms-field>
            <vms-field label="Down payment" [control]="finance.controls.downPayment" for="vf-down" hint="The same as the amount paid at creation.">
              <p-inputnumber inputId="vf-down" formControlName="downPayment" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
            </vms-field>
            <vms-field label="Installment amount" [control]="finance.controls.installmentAmount" for="vf-inst">
              <p-inputnumber inputId="vf-inst" formControlName="installmentAmount" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
            </vms-field>
            <vms-field label="Frequency" [control]="finance.controls.frequency" for="vf-freq">
              <p-select inputId="vf-freq" formControlName="frequency" [options]="frequencies" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
            </vms-field>
            <vms-field label="Tenure (installments)" [control]="finance.controls.tenure" for="vf-tenure" hint="1 to 120.">
              <p-inputnumber inputId="vf-tenure" formControlName="tenure" [useGrouping]="false" [fluid]="true" />
            </vms-field>
            <vms-field label="First installment due" [control]="finance.controls.firstDueDate" for="vf-first" hint="On or after the agreement date.">
              <vms-date-picker inputId="vf-first" formControlName="firstDueDate" [min]="finance.controls.agreementDate.value" />
            </vms-field>
            <vms-field label="Markup or profit rate (%)" [control]="finance.controls.markupRate" for="vf-rate" hint="For reference: installments are not split into principal and markup.">
              <p-inputnumber inputId="vf-rate" formControlName="markupRate" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
            </vms-field>
            <vms-field label="Residual or balloon amount" [control]="finance.controls.residualAmount" for="vf-resid" hint="Due at the end of the tenure, as a final row of the schedule.">
              <p-inputnumber inputId="vf-resid" formControlName="residualAmount" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
            </vms-field>
            <vms-field label="Security deposit" [control]="finance.controls.securityDeposit" for="vf-dep" hint="Posted as a transaction.">
              <p-inputnumber inputId="vf-dep" formControlName="securityDeposit" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" />
            </vms-field>
          </div>

          <div class="summary" aria-live="polite">
            <div><span class="muted">Total payable</span> <strong class="total">{{ total() | number: '1.2-2' }}</strong>
              <span class="muted small">installment × tenure@if (residual() > 0) {, plus {{ residual() | number: '1.2-2' }} due at the end}</span></div>
            @if (mismatch(); as m) {
              <div class="alert warning" role="status">Finance amount plus down payment is {{ m.sum | number: '1.2-2' }}, but the purchase price is {{ m.price | number: '1.2-2' }}. You can still save; you will be asked to confirm.</div>
            }
          </div>
        </div>
      }

      <div class="actions">
        <span class="muted">{{ dirty() ? 'You have unsaved changes on this step.' : 'All changes on this step are saved.' }}</span>
        <p-button label="Save acquisition and finance" icon="pi pi-check" [loading]="busy()" [disabled]="!dirty()" (onClick)="save()" />
      </div>
    }
  `,
  styles: [
    `
      h2 { font-size: 1.05rem; margin: 0 0 .25rem; } .lead { margin: 0 0 .75rem; }
      .alert { margin-bottom: 1rem; } .alert ul { margin: .35rem 0 0; padding-left: 1.25rem; }
      .conflict { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
      .financed { display: flex; align-items: center; gap: .6rem; margin: 1.5rem 0 1rem; padding-top: 1rem; border-top: 1px solid var(--vms-border); }
      .financed label { font-weight: 600; cursor: pointer; }
      .summary { margin-top: 1rem; display: grid; gap: .75rem; } .total { font-size: 1.15rem; margin: 0 .5rem; } .small { font-size: .8rem; }
      .actions { display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; margin-top: 1.5rem; padding-top: 1rem; border-top: 1px solid var(--vms-border); }
    `,
  ],
})
export class VehicleFinanceStepComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(VehiclesApi);
  private readonly lookups = inject(LookupsApi);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly confirm = inject(ConfirmService);

  readonly vehicleId = input.required<number>();
  /** The vehicle row changed (its acquisition date, type and row version), so the wizard's own copy must follow. */
  readonly saved = output<Acquisition>();

  readonly acquisition = buildAcquisitionForm(this.fb);
  readonly finance = buildFinanceForm(this.fb);

  protected readonly acquisitionTypes = optionsOf(ACQUISITION_TYPES);
  protected readonly paymentModes = optionsOf(PAYMENT_MODES);
  protected readonly frequencies = optionsOf(FINANCE_FREQUENCIES);
  protected readonly graceDays = AGREEMENT_GRACE_DAYS;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly conflict = signal<string | null>(null);
  protected readonly financeTypes = signal<{ label: string; value: number }[]>([]);

  private vehicleRowVersion = '';
  private readonly agreement = signal<Agreement | null>(null);
  /** What the person chose with the tick box; null until they choose, when the type and any saved agreement decide. */
  private readonly asked = signal<boolean | null>(null);
  private readonly tick = signal(0);

  readonly financed = computed(() => (this.tick(), this.asked() ?? showsFinanceBlock(this.acquisition.controls.acquisitionType.value, this.agreement() !== null, false)));
  readonly paidSomething = computed(() => (this.tick(), (this.acquisition.controls.amountPaid.value ?? 0) > 0));
  readonly total = computed(() => (this.tick(), totalPayable(this.finance.controls.installmentAmount.value, this.finance.controls.tenure.value)));
  readonly residual = computed(() => (this.tick(), this.finance.controls.residualAmount.value ?? 0));
  readonly agreementLimit = computed(() => (this.tick(), agreementDateLimit(this.acquisition.controls.acquisitionDate.value)));
  readonly mismatch = computed(() => (this.tick(), reconciliation(this.acquisition.controls.purchasePrice.value, this.finance.controls.financeAmount.value, this.finance.controls.downPayment.value)));
  readonly dirty = computed(() => (this.tick(), this.acquisition.dirty || (this.financed() && this.finance.dirty) || (this.asked() !== null && this.asked() !== (this.agreement() !== null))));
  /** How many fields show a problem, to badge the step. */
  readonly problemCount = computed(() => (this.tick(), problemsIn(this.acquisition, ...(this.financed() ? [this.finance] : []))));

  constructor() {
    this.acquisition.events.subscribe(() => this.changed());
    this.finance.events.subscribe(() => this.changed());
    queueMicrotask(() => this.load());
  }

  // ── Loading ─────────────────────────────────────────────────────────────────────

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.conflict.set(null);
    this.problems.set([]);
    forkJoin({ acquisition: this.api.acquisition(this.vehicleId()), agreement: this.api.finance(this.vehicleId()), types: this.lookups.active('FINANCE_TYPE') }).subscribe({
      next: ({ acquisition, agreement, types }) => {
        this.vehicleRowVersion = acquisition.rowVersion;
        this.agreement.set(agreement);
        this.asked.set(null);
        this.financeTypes.set(this.typeOptions(types, agreement));
        patchAcquisition(this.acquisition, acquisition);
        patchFinance(this.finance, agreement);
        this.acquisition.markAsPristine();
        this.finance.markAsPristine();
        this.loading.set(false);
        this.changed();
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This vehicle was not found.' : errorMessage(err, 'The acquisition and finance could not be loaded.'));
      },
    });
  }

  /** Every finance type but Fully Paid, which means there is no agreement. A type retired since the agreement was saved stays, marked. */
  private typeOptions(types: LookupItem[], agreement: Agreement | null): { label: string; value: number }[] {
    const options = types.filter((t) => t.code !== FULLY_PAID_CODE).map((t) => ({ label: t.description, value: t.id }));
    if (agreement && !options.some((o) => o.value === agreement.financeTypeId)) options.push({ label: `${agreement.financeType ?? 'Finance type'} (retired)`, value: agreement.financeTypeId });
    return options;
  }

  // ── Keeping the rules and the totals in step ────────────────────────────────────

  private changed(): void {
    syncAcquisitionRules(this.acquisition);
    if (this.financed()) {
      this.suggest();
      syncFinanceRules(this.finance, this.acquisition);
    }
    this.tick.update((n) => n + 1);
  }

  /** Until the person types their own, the down payment follows the amount paid and the finance amount the balance of the price. */
  private suggest(): void {
    const a = this.acquisition.controls;
    const f = this.finance.controls;
    const paid = a.amountPaid.value ?? 0;
    if (!f.downPayment.dirty && f.downPayment.value !== paid) f.downPayment.setValue(paid, { emitEvent: false });
    const balance = suggestedFinanceAmount(a.purchasePrice.value, a.amountPaid.value);
    if (!f.financeAmount.dirty && balance !== null && f.financeAmount.value !== balance) f.financeAmount.setValue(balance, { emitEvent: false });
  }

  setFinanced(on: boolean): void {
    this.asked.set(on);
    this.changed();
  }

  // ── Saving ──────────────────────────────────────────────────────────────────────

  /** Saves the acquisition, then the finance. Returns whether everything the person asked for was saved. */
  async save(): Promise<boolean> {
    if (this.busy()) return false;
    this.problems.set([]);
    this.conflict.set(null);
    this.acquisition.markAllAsTouched();
    if (this.financed()) this.finance.markAllAsTouched();
    this.changed();
    if (this.acquisition.invalid || (this.financed() && this.finance.invalid)) {
      this.notify.warn('Please correct the highlighted fields.', 'Not saved');
      return false;
    }

    this.busy.set(true);
    try {
      const acquisition = await firstValueFrom(this.api.saveAcquisition(this.vehicleId(), toAcquisitionRequest(this.acquisition, this.vehicleRowVersion)));
      this.vehicleRowVersion = acquisition.rowVersion;
      this.acquisition.markAsPristine();
      this.saved.emit(acquisition);

      if (this.financed()) {
        if (!(await this.saveFinance(false))) return false;
      } else if (this.agreement() !== null) {
        if (!(await this.confirm.ask({ title: 'Remove bank finance?', message: 'The saved bank agreement for this vehicle will be removed.', confirmLabel: 'Remove it', cancelLabel: 'Keep it', icon: 'pi pi-trash' }))) return false;
        await firstValueFrom(this.api.removeFinance(this.vehicleId()));
        this.agreement.set(null);
        this.asked.set(null);
        patchFinance(this.finance, null);
      }
      this.notify.success('Acquisition and finance saved');
      this.changed();
      return true;
    } catch (err) {
      this.refused(err, true);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  private async saveFinance(confirmMismatch: boolean): Promise<boolean> {
    const current = this.agreement();
    try {
      const saved = await firstValueFrom(this.api.saveFinance(this.vehicleId(), toFinanceRequest(this.finance, current?.rowVersion ?? null, confirmMismatch)));
      this.agreement.set(saved);
      this.asked.set(null);
      patchFinance(this.finance, saved);
      this.finance.markAsPristine();
      return true;
    } catch (err) {
      // Finance and down payment that do not add up to the price is a question, not an error (BR-VH-010): ask, and send again with the yes.
      const found = apiErrors(err);
      const question = found.find((e) => e.code === FINANCE_NOT_RECONCILED);
      if (question && found.length === 1) {
        const go = await this.confirm.ask({ title: 'Finance does not add up to the price', message: this.messages.describe(question), confirmLabel: 'Continue anyway', cancelLabel: 'Go back', icon: 'pi pi-question-circle' });
        return go ? this.saveFinance(true) : false;
      }
      this.refused(err, false);
      return false;
    }
  }

  /** Puts what the API said against the field it names, in whichever block has it (the agreement date is judged against the acquisition date), and lists the rest. */
  private refused(err: unknown, acquisitionFirst: boolean): void {
    if (err instanceof HttpErrorResponse && err.status === 409) {
      this.conflict.set(errorMessage(err, 'This vehicle was changed by someone else while you were editing.'));
      return;
    }
    const found = apiErrors(err);
    if (found.length === 0) return this.notify.error(err);
    const [first, second] = acquisitionFirst ? [this.acquisition, this.finance] : [this.finance, this.acquisition];
    const unplaced = applyServerErrors(second, applyServerErrors(first, found, this.messages), this.messages);
    this.problems.set(unplaced.map((e) => this.messages.describe(e)));
    this.changed();
  }
}