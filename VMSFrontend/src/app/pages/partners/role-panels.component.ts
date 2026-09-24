import { Component, computed, input } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { todayDateOnly } from '../../core/datetime/datetime';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import {
  BILLING_CYCLES, COMMISSION_BASES, CUSTOMER_TYPES, EMPLOYMENT_TYPES, LICENCE_TYPES, RATE_BASES, SUPPLY_CATEGORIES, optionsOf,
} from './partner-logic';

/**
 * The fields of a role that has fields of its own (FSD §7), one component per role. Each is given its part of the form
 * and knows nothing of tabs or dialogs, so the Role details tab and the Add role dialog show the same thing. Amounts
 * a person may not see (salary, credit) are not drawn at all (BR-SEC-001).
 */

@Component({
  selector: 'app-driver-panel',
  standalone: true,
  imports: [ReactiveFormsModule, InputTextModule, InputNumberModule, SelectModule, ToggleSwitchModule, FieldComponent, DatePickerComponent],
  template: `
    <div class="form-grid" [formGroup]="group()">
      <vms-field label="Licence number" [control]="group().controls['licenceNo']" for="dr-licence" hint="Unique among active drivers.">
        <input pInputText id="dr-licence" formControlName="licenceNo" />
      </vms-field>
      <vms-field label="Licence type" [control]="group().controls['licenceType']" for="dr-type">
        <p-select inputId="dr-type" formControlName="licenceType" [options]="licenceTypes" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
      </vms-field>
      <vms-field label="Licence issue date" [control]="group().controls['licenceIssueDate']" for="dr-issue">
        <vms-date-picker inputId="dr-issue" formControlName="licenceIssueDate" [notFuture]="true" />
      </vms-field>
      <vms-field label="Licence expiry date" [control]="group().controls['licenceExpiryDate']" for="dr-expiry" [hint]="creating() ? 'Must be in the future.' : ''">
        <vms-date-picker inputId="dr-expiry" formControlName="licenceExpiryDate" [min]="creating() ? tomorrow() : null" />
      </vms-field>
      <vms-field label="Employment type" [control]="group().controls['employmentType']" for="dr-employment">
        <p-select inputId="dr-employment" formControlName="employmentType" [options]="employmentTypes" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
      </vms-field>
      <vms-field label="Date of joining" [control]="group().controls['dateOfJoining']" for="dr-joining" hint="Required for an employee.">
        <vms-date-picker inputId="dr-joining" formControlName="dateOfJoining" [notFuture]="true" />
      </vms-field>

      @if (salary()) {
        <vms-field label="Monthly rate (PKR)" [control]="group().controls['monthlyRate']" for="dr-rate">
          <p-inputnumber inputId="dr-rate" formControlName="monthlyRate" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" />
        </vms-field>
        <vms-field label="Commission basis" [control]="group().controls['commissionBasis']" for="dr-basis">
          <p-select inputId="dr-basis" formControlName="commissionBasis" [options]="commissionBases" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        @if (group().controls['commissionBasis'].value !== 'None') {
          <vms-field [label]="group().controls['commissionBasis'].value === 'Percent' ? 'Commission (%)' : 'Commission (PKR)'" [control]="group().controls['commissionValue']" for="dr-commission">
            <p-inputnumber inputId="dr-commission" formControlName="commissionValue" mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="2" [min]="0" [fluid]="true" />
          </vms-field>
        }
      } @else {
        <div class="full muted">Salary and commission are not shown to your role.</div>
      }

      <vms-field label="Blood group" [control]="group().controls['bloodGroup']" for="dr-blood"><input pInputText id="dr-blood" formControlName="bloodGroup" placeholder="O+" /></vms-field>
      <vms-field label="Guarantor" [control]="group().controls['guarantor']" for="dr-guarantor"><input pInputText id="dr-guarantor" formControlName="guarantor" /></vms-field>
      <vms-field label="Emergency contact" [control]="group().controls['emergencyContactName']" for="dr-em-name"><input pInputText id="dr-em-name" formControlName="emergencyContactName" /></vms-field>
      <vms-field label="Emergency phone" [control]="group().controls['emergencyContactPhone']" for="dr-em-phone"><input pInputText id="dr-em-phone" formControlName="emergencyContactPhone" /></vms-field>
      <label class="full switch"><p-toggleswitch formControlName="driverAppAccess" /> Can sign in to the driver app</label>
    </div>
  `,
  styles: [`.switch { display: flex; align-items: center; gap: .6rem; }`],
})
export class DriverPanelComponent {
  readonly group = input.required<FormGroup>();
  readonly salary = input(false);
  /** A new partner: the licence must be valid now. An existing driver may hold an expired one. */
  readonly creating = input(true);

  protected readonly licenceTypes = optionsOf(LICENCE_TYPES);
  protected readonly employmentTypes = optionsOf(EMPLOYMENT_TYPES);
  protected readonly commissionBases = optionsOf(COMMISSION_BASES);
  protected readonly tomorrow = computed(() => {
    const [y, m, d] = todayDateOnly().split('-').map(Number);
    const next = new Date(Date.UTC(y, m - 1, d + 1));
    return next.toISOString().slice(0, 10);
  });
}

@Component({
  selector: 'app-vendor-panel',
  standalone: true,
  imports: [ReactiveFormsModule, InputNumberModule, MultiSelectModule, FieldComponent],
  template: `
    <div class="form-grid" [formGroup]="group()">
      <vms-field class="full" label="Supply categories" [control]="group().controls['supplyCategories']" for="vn-categories">
        <p-multiselect inputId="vn-categories" formControlName="supplyCategories" [options]="categories" optionLabel="label" optionValue="value" display="chip" placeholder="Choose…" [fluid]="true" appendTo="body" />
      </vms-field>
      @if (credit()) {
        <vms-field label="Payment terms (days)" [control]="group().controls['paymentTermDays']" for="vn-terms">
          <p-inputnumber inputId="vn-terms" formControlName="paymentTermDays" [min]="0" [useGrouping]="false" [fluid]="true" />
        </vms-field>
        <vms-field label="Credit limit (PKR)" [control]="group().controls['creditLimit']" for="vn-limit" hint="A warning only: it does not stop a purchase.">
          <p-inputnumber inputId="vn-limit" formControlName="creditLimit" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" />
        </vms-field>
      } @else {
        <div class="full muted">Payment terms and credit limit are not shown to your role.</div>
      }
    </div>
  `,
})
export class VendorPanelComponent {
  readonly group = input.required<FormGroup>();
  readonly credit = input(false);
  protected readonly categories = optionsOf(SUPPLY_CATEGORIES);
}

@Component({
  selector: 'app-customer-panel',
  standalone: true,
  imports: [ReactiveFormsModule, InputNumberModule, SelectModule, FieldComponent],
  template: `
    <div class="form-grid" [formGroup]="group()">
      <vms-field label="Customer type" [control]="group().controls['customerType']" for="cu-type">
        <p-select inputId="cu-type" formControlName="customerType" [options]="types" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
      </vms-field>
      <vms-field label="Billing cycle" [control]="group().controls['billingCycle']" for="cu-cycle">
        <p-select inputId="cu-cycle" formControlName="billingCycle" [options]="cycles" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
      </vms-field>
      <vms-field label="Rate basis" [control]="group().controls['rateBasis']" for="cu-basis">
        <p-select inputId="cu-basis" formControlName="rateBasis" [options]="bases" optionLabel="label" optionValue="value" placeholder="Choose…" [showClear]="true" [fluid]="true" appendTo="body" />
      </vms-field>
      <vms-field label="Default rate (PKR)" [control]="group().controls['defaultRate']" for="cu-rate">
        <p-inputnumber inputId="cu-rate" formControlName="defaultRate" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" />
      </vms-field>
      @if (credit()) {
        <vms-field label="Credit limit (PKR)" [control]="group().controls['creditLimit']" for="cu-limit">
          <p-inputnumber inputId="cu-limit" formControlName="creditLimit" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [min]="0" [fluid]="true" />
        </vms-field>
        <vms-field label="Credit days" [control]="group().controls['creditDays']" for="cu-days">
          <p-inputnumber inputId="cu-days" formControlName="creditDays" [min]="0" [useGrouping]="false" [fluid]="true" />
        </vms-field>
      } @else {
        <div class="full muted">Credit limit and credit days are not shown to your role.</div>
      }
    </div>
  `,
})
export class CustomerPanelComponent {
  readonly group = input.required<FormGroup>();
  readonly credit = input(false);
  protected readonly types = optionsOf(CUSTOMER_TYPES);
  protected readonly cycles = optionsOf(BILLING_CYCLES);
  protected readonly bases = optionsOf(RATE_BASES);
}
