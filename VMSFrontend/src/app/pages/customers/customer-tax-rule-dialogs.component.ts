import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, output, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { CustomersApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { CalculationBasis, CustomerTaxRuleModel, TaxType } from '../../core/customer.models';
import { ConfirmService } from '../../shared/confirm.service';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { ActionDialog } from './customer-dialogs.component';
import { CALCULATION_BASES, TAX_TYPES, optionsOf } from './customer-logic';

/**
 * Add a rule, or give an open rule a new rate (FSD §14, screen 6). Both go through the same request shape
 * (`SaveCustomerTaxRuleRequest`); the server treats a new rule whose name+code collide with one already open
 * as an implicit replace and refuses it once with `TAX_RULE_REPLACEMENT_CONFIRMATION` — a 422 with a
 * machine code, not a field error — so it is caught here and turned into a confirm dialog rather than routed
 * through {@link ActionDialog.run}, which only ever sees one shape of failure (field problems).
 */
@Component({
  selector: 'app-customer-tax-rule-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, InputNumberModule, CheckboxModule, SelectModule, TextareaModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '620px' }" [header]="replacing() ? 'New rate for ' + replacing()!.taxName : 'Add a tax/deduction rule'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="Name" [control]="form.controls.taxName" for="tr-name"><input pInputText id="tr-name" formControlName="taxName" [readonly]="!!replacing()" /></vms-field>
        <vms-field label="Code" [control]="form.controls.taxCode" for="tr-code"><input pInputText id="tr-code" formControlName="taxCode" [readonly]="!!replacing()" /></vms-field>
        <vms-field label="Type" [control]="form.controls.taxType" for="tr-type">
          <p-select inputId="tr-type" formControlName="taxType" [options]="taxTypes" optionLabel="label" optionValue="value" [disabled]="!!replacing()" [fluid]="true" appendTo="body" />
        </vms-field>
        @if (form.controls.taxType.value === 'Percentage') {
          <vms-field label="Percentage" [control]="form.controls.taxPercentage" for="tr-pct"><p-inputnumber inputId="tr-pct" formControlName="taxPercentage" suffix="%" [minFractionDigits]="0" [maxFractionDigits]="3" [fluid]="true" /></vms-field>
        } @else {
          <vms-field label="Fixed amount" [control]="form.controls.fixedAmount" for="tr-fixed"><p-inputnumber inputId="tr-fixed" formControlName="fixedAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        }
        <vms-field label="Calculation basis" [control]="form.controls.calculationBasis" for="tr-basis">
          <p-select inputId="tr-basis" formControlName="calculationBasis" [options]="bases" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Sequence" [control]="form.controls.sequence" for="tr-seq" hint="Lower runs first when several rules apply."><p-inputnumber inputId="tr-seq" formControlName="sequence" [min]="1" [useGrouping]="false" [fluid]="true" /></vms-field>
        <vms-field label="Effective from" [control]="form.controls.effectiveFrom" for="tr-from" [hint]="replacing() ? 'The existing rule closes the day before.' : ''"><vms-date-picker inputId="tr-from" formControlName="effectiveFrom" /></vms-field>
        <div><p-checkbox formControlName="applicable" [binary]="true" inputId="tr-applicable" /> <label for="tr-applicable">Applicable (used on new invoices)</label></div>
        <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="tr-remarks"><textarea pTextarea id="tr-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="replacing() ? 'Save new rate' : 'Add rule'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; align-items: end; } .full { grid-column: 1 / -1; }`],
})
export class CustomerTaxRuleDialogComponent extends ActionDialog {
  private readonly api = inject(CustomersApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);

  readonly customerId = input.required<number>();
  readonly adding = input(false);
  readonly replacing = input<CustomerTaxRuleModel | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.replacing());
  protected readonly taxTypes = optionsOf(TAX_TYPES);
  protected readonly bases = optionsOf(CALCULATION_BASES);

  readonly form = this.fb.group({
    taxName: this.fb.nonNullable.control('', Validators.required),
    taxCode: this.fb.nonNullable.control('', Validators.required),
    taxType: this.fb.nonNullable.control<TaxType>('Percentage'),
    taxPercentage: this.fb.control<number | null>(null),
    fixedAmount: this.fb.control<number | null>(null),
    calculationBasis: this.fb.nonNullable.control<CalculationBasis>('GrossTripAmount'),
    sequence: this.fb.nonNullable.control(1, [Validators.required, Validators.min(1)]),
    effectiveFrom: this.fb.control<string | null>(null),
    applicable: this.fb.nonNullable.control(true),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      const r = this.replacing();
      const adding = this.adding();
      if (!adding && !r) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(r
          ? { taxName: r.taxName, taxCode: r.taxCode, taxType: r.taxType, taxPercentage: r.taxPercentage ?? null, fixedAmount: r.fixedAmount ?? null, calculationBasis: r.calculationBasis, sequence: r.sequence, effectiveFrom: null, applicable: r.applicable, remarks: '' }
          : { taxName: '', taxCode: '', taxType: 'Percentage', taxPercentage: null, fixedAmount: null, calculationBasis: 'GrossTripAmount', sequence: 1, effectiveFrom: null, applicable: true, remarks: '' });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    this.submit(false);
  }

  private submit(confirmReplace: boolean): void {
    this.busy.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    const body = {
      taxName: f.taxName.trim(), taxCode: f.taxCode.trim(), taxType: f.taxType,
      taxPercentage: f.taxType === 'Percentage' ? f.taxPercentage : null,
      fixedAmount: f.taxType === 'Fixed' ? f.fixedAmount : null,
      applicable: f.applicable, calculationBasis: f.calculationBasis, sequence: f.sequence,
      effectiveFrom: f.effectiveFrom, remarks: f.remarks.trim() || null, confirmReplace,
    };
    const target = this.replacing();
    const request = target ? this.api.replaceTaxRule(target.customerTaxRuleId, body) : this.api.createTaxRule(this.customerId(), body);
    request.subscribe({
      next: () => { this.busy.set(false); this.notify.success(target ? 'New rate saved' : 'Tax rule added'); this.saved.emit(); },
      error: (err: unknown) => {
        this.busy.set(false);
        if (err instanceof HttpErrorResponse && err.status === 422 && (err.error as { code?: string } | null)?.code === 'TAX_RULE_REPLACEMENT_CONFIRMATION') {
          const message = (err.error as { message?: string } | null)?.message ?? 'This will replace the existing open rule. Continue?';
          void this.confirm.ask({ title: 'Replace existing rule', message, confirmLabel: 'Replace', cancelLabel: 'Cancel', icon: 'pi pi-question-circle' }).then((go) => {
            if (go) this.submit(true);
          });
          return;
        }
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}

/** In-place edit (FSD §14): rate, applicability, basis, sequence and remarks only — identity and effective
 * range are fixed once a rule exists (a rate change goes through {@link CustomerTaxRuleDialogComponent}'s
 * "New rate" instead, so it leaves its own history row). */
@Component({
  selector: 'app-customer-tax-rule-edit-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, CheckboxModule, SelectModule, TextareaModule, FieldComponent],
  template: `
    <p-dialog [visible]="!!rule()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '520px' }" header="Edit tax rule" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        @if (rule()?.taxType === 'Percentage') {
          <vms-field label="Percentage" [control]="form.controls.taxPercentage" for="tre-pct"><p-inputnumber inputId="tre-pct" formControlName="taxPercentage" suffix="%" [minFractionDigits]="0" [maxFractionDigits]="3" [fluid]="true" /></vms-field>
        } @else {
          <vms-field label="Fixed amount" [control]="form.controls.fixedAmount" for="tre-fixed"><p-inputnumber inputId="tre-fixed" formControlName="fixedAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        }
        <vms-field label="Calculation basis" [control]="form.controls.calculationBasis" for="tre-basis">
          <p-select inputId="tre-basis" formControlName="calculationBasis" [options]="bases" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Sequence" [control]="form.controls.sequence" for="tre-seq"><p-inputnumber inputId="tre-seq" formControlName="sequence" [min]="1" [useGrouping]="false" [fluid]="true" /></vms-field>
        <div><p-checkbox formControlName="applicable" [binary]="true" inputId="tre-applicable" /> <label for="tre-applicable">Applicable</label></div>
        <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="tre-remarks"><textarea pTextarea id="tre-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Save changes" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; align-items: end; } .full { grid-column: 1 / -1; }`],
})
export class CustomerTaxRuleEditDialogComponent extends ActionDialog {
  private readonly api = inject(CustomersApi);
  private readonly fb = inject(FormBuilder);

  readonly rule = input<CustomerTaxRuleModel | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  protected readonly bases = optionsOf(CALCULATION_BASES);

  readonly form = this.fb.group({
    taxPercentage: this.fb.control<number | null>(null),
    fixedAmount: this.fb.control<number | null>(null),
    calculationBasis: this.fb.nonNullable.control<CalculationBasis>('GrossTripAmount'),
    sequence: this.fb.nonNullable.control(1, [Validators.required, Validators.min(1)]),
    applicable: this.fb.nonNullable.control(true),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      const r = this.rule();
      if (!r) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ taxPercentage: r.taxPercentage ?? null, fixedAmount: r.fixedAmount ?? null, calculationBasis: r.calculationBasis, sequence: r.sequence, applicable: r.applicable, remarks: r.remarks ?? '' });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const r = this.rule();
    if (!r) return;
    const f = this.form.getRawValue();
    const body = { taxPercentage: f.taxPercentage, fixedAmount: f.fixedAmount, applicable: f.applicable, calculationBasis: f.calculationBasis, sequence: f.sequence, remarks: f.remarks.trim() || null };
    this.run(this.api.updateTaxRule(r.customerTaxRuleId, body), this.form, 'Tax rule updated', () => this.saved.emit());
  }
}
