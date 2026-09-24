import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { CategoryDetails } from '../../core/vehicle.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { ARRANGEMENT_TYPES, CATEGORY_RULES, Category, EXPENSE_RULES, RENT_FREQUENCIES, SHARING_BASES, optionsOf, visibleCategoryFields } from './vehicle-logic';

/** The fields of an ownership category (FSD §17) as one flat form, so a server error's `details.` name finds its control. */
export function buildCategoryDetails(fb: FormBuilder): FormGroup {
  return fb.group({
    counterpartyId: fb.control<number | null>(null), sharePercent: fb.control<number | null>(null), sharingBasis: fb.control<string | null>(null),
    fixedMonthlyAmount: fb.control<number | null>(null), expenseSharingRule: fb.control<string | null>(null), rentAmount: fb.control<number | null>(null),
    rentFrequency: fb.control<string | null>(null), rentDueDay: fb.control<number | null>(null), securityDeposit: fb.control<number | null>(null),
    arrangementType: fb.control<string | null>(null), agreedAmount: fb.control<number | null>(null), revenueSharePercent: fb.control<number | null>(null),
    agreementReference: [''], endDate: fb.control<string | null>(null),
  });
}

/** The form as the API takes it. */
export function toCategoryDetails(group: FormGroup): CategoryDetails {
  const d = group.getRawValue() as CategoryDetails & { agreementReference?: string | null };
  return { ...d, agreementReference: d.agreementReference?.trim() || null };
}

/**
 * The counterparty and the terms an ownership category asks for. Which fields show follows the category and the choices already made
 * (a fixed monthly amount only for a fixed-monthly share). Used to change the category of a vehicle in the fleet and to enter the
 * ownership of a Draft. The wording and the rules are the same in both, so they are written once.
 */
@Component({
  selector: 'app-category-fields',
  standalone: true,
  imports: [ReactiveFormsModule, InputNumberModule, InputTextModule, SelectModule, FieldComponent, DatePickerComponent, PartnerPickerComponent],
  template: `
    <div class="form-grid" [formGroup]="details()">
      @if (rule().counterpartyLabel) {
        <vms-field class="full" [label]="rule().counterpartyLabel" [control]="ctl('counterpartyId')" [for]="id('party')" [hint]="partyHint()">
          <vms-partner-picker [role]="pickerRole()" formControlName="counterpartyId" [inputId]="id('party')" [allowCreate]="allowCreate()" />
        </vms-field>
      }
      @for (f of fields(); track f) {
        @switch (f) {
          @case ('sharePercent') { <vms-field label="Our share %" [control]="ctl('sharePercent')" [for]="id('share')" hint="0.01 to 100."><p-inputnumber [inputId]="id('share')" formControlName="sharePercent" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" /></vms-field> }
          @case ('sharingBasis') { <vms-field label="Sharing basis" [control]="ctl('sharingBasis')" [for]="id('basis')"><p-select [inputId]="id('basis')" formControlName="sharingBasis" [options]="sharingBases" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" /></vms-field> }
          @case ('fixedMonthlyAmount') { @if (finance()) { <vms-field label="Fixed monthly amount (PKR)" [control]="ctl('fixedMonthlyAmount')" [for]="id('fixed')"><p-inputnumber [inputId]="id('fixed')" formControlName="fixedMonthlyAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field> } }
          @case ('expenseSharingRule') { <vms-field label="Expense sharing" [control]="ctl('expenseSharingRule')" [for]="id('rule')" hint="How the expenses are split is worked out at month-end profit and loss."><p-select [inputId]="id('rule')" formControlName="expenseSharingRule" [options]="expenseRules" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" /></vms-field> }
          @case ('rentAmount') { @if (finance()) { <vms-field label="Rent amount (PKR)" [control]="ctl('rentAmount')" [for]="id('rent')"><p-inputnumber [inputId]="id('rent')" formControlName="rentAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field> } }
          @case ('rentFrequency') { <vms-field label="Rent frequency" [control]="ctl('rentFrequency')" [for]="id('freq')"><p-select [inputId]="id('freq')" formControlName="rentFrequency" [options]="rentFrequencies" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" /></vms-field> }
          @case ('rentDueDay') { <vms-field label="Rent due day" [control]="ctl('rentDueDay')" [for]="id('day')" hint="1 to 31. The 29th to 31st fall back to month end."><p-inputnumber [inputId]="id('day')" formControlName="rentDueDay" [min]="1" [max]="31" [useGrouping]="false" [fluid]="true" /></vms-field> }
          @case ('securityDeposit') { @if (finance()) { <vms-field label="Security deposit (PKR)" [control]="ctl('securityDeposit')" [for]="id('deposit')"><p-inputnumber [inputId]="id('deposit')" formControlName="securityDeposit" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field> } }
          @case ('arrangementType') { <vms-field label="Arrangement type" [control]="ctl('arrangementType')" [for]="id('type')"><p-select [inputId]="id('type')" formControlName="arrangementType" [options]="arrangementTypes" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" /></vms-field> }
          @case ('agreedAmount') { @if (finance()) { <vms-field label="Agreed amount (PKR)" [control]="ctl('agreedAmount')" [for]="id('agreed')"><p-inputnumber [inputId]="id('agreed')" formControlName="agreedAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field> } }
          @case ('revenueSharePercent') { <vms-field label="Revenue share %" [control]="ctl('revenueSharePercent')" [for]="id('revshare')"><p-inputnumber [inputId]="id('revshare')" formControlName="revenueSharePercent" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [fluid]="true" /></vms-field> }
          @case ('agreementReference') { <vms-field label="Agreement reference" [control]="ctl('agreementReference')" [for]="id('ref')"><input pInputText [id]="id('ref')" formControlName="agreementReference" /></vms-field> }
          @case ('endDate') { <vms-field label="End date" [control]="ctl('endDate')" [for]="id('end')" hint="Optional. After the start."><vms-date-picker [inputId]="id('end')" formControlName="endDate" /></vms-field> }
        }
      }
    </div>
  `,
})
export class CategoryFieldsComponent {
  private readonly destroyRef = inject(DestroyRef);

  /** The form from <c>buildCategoryDetails</c>. */
  readonly details = input.required<FormGroup>();
  readonly category = input<string>('');
  /** Prefixes the field ids, so two of these on a page (or a page and a dialog) never share one. */
  readonly idPrefix = input('cc');
  /** Whether the person may enter amounts. Without the permission they are not asked for. */
  readonly finance = input(true);
  /** What to say under the counterparty of a bank lease when the bank is normally taken from the finance agreement. */
  readonly bankHint = input('');
  /** Offer "New" beside the counterparty for the roles that name one (a bank), for people who may create partners. */
  readonly allowCreate = input(false);

  protected readonly sharingBases = optionsOf(SHARING_BASES);
  protected readonly expenseRules = optionsOf(EXPENSE_RULES);
  protected readonly rentFrequencies = optionsOf(RENT_FREQUENCIES);
  protected readonly arrangementTypes = optionsOf(ARRANGEMENT_TYPES);

  private readonly tick = signal(0);
  protected readonly rule = computed(() => CATEGORY_RULES[this.category() as Category] ?? CATEGORY_RULES.SelfOwned);
  protected readonly pickerRole = computed(() => (this.category() === 'BankLeased' ? 'Bank' : null));
  protected readonly partyHint = computed(() => (this.category() === 'CustomerArrangement' ? 'A customer or a running customer.' : this.category() === 'BankLeased' ? this.bankHint() : ''));
  protected readonly fields = computed(() => {
    this.tick();
    const v = this.details().getRawValue() as CategoryDetails;
    return visibleCategoryFields(this.category(), { sharingBasis: v.sharingBasis, rentFrequency: v.rentFrequency, arrangementType: v.arrangementType });
  });

  constructor() {
    // The fields shown depend on choices made in the form itself, so follow it (whichever form it is given).
    effect(() => {
      const form = this.details();
      untracked(() => form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.tick.update((n) => n + 1)));
    });
  }

  protected id(name: string): string {
    return `${this.idPrefix()}-${name}`;
  }

  protected ctl(name: string) {
    return this.details().controls[name];
  }
}
