import { Component, computed, effect, inject, input, output, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { CustomersApi } from '../../core/api.services';
import { CustomerBillingAddressModel } from '../../core/customer.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { ActionDialog } from './customer-dialogs.component';

/** Add or edit a customer billing address (FSD §12, screen 4). */
@Component({
  selector: 'app-customer-billing-address-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, CheckboxModule, FieldComponent, LookupPickerComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '620px' }" [header]="address() ? 'Edit billing address' : 'Add a billing address'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="Address name" [control]="form.controls.addressName" for="ba-name" hint="Unique for this customer, e.g. Head Office."><input pInputText id="ba-name" formControlName="addressName" /></vms-field>
        <vms-field label="City" [control]="form.controls.cityId" for="ba-city"><vms-lookup-picker type="CITY" formControlName="cityId" inputId="ba-city" placeholder="Choose a city" /></vms-field>
        <vms-field label="Address line 1" [control]="form.controls.addressLine1" for="ba-l1"><input pInputText id="ba-l1" formControlName="addressLine1" /></vms-field>
        <vms-field label="Address line 2" [control]="form.controls.addressLine2" for="ba-l2"><input pInputText id="ba-l2" formControlName="addressLine2" /></vms-field>
        <vms-field label="Province/State" [control]="form.controls.provinceState" for="ba-province"><input pInputText id="ba-province" formControlName="provinceState" /></vms-field>
        <vms-field label="Postal code" [control]="form.controls.postalCode" for="ba-postal"><input pInputText id="ba-postal" formControlName="postalCode" /></vms-field>
        <vms-field label="NTN" [control]="form.controls.ntn" for="ba-ntn" hint="Overrides the customer's own NTN on an invoice, if set."><input pInputText id="ba-ntn" formControlName="ntn" /></vms-field>
        <vms-field label="STRN" [control]="form.controls.strn" for="ba-strn"><input pInputText id="ba-strn" formControlName="strn" /></vms-field>
        <vms-field label="Effective from" [control]="form.controls.effectiveFrom" for="ba-from"><vms-date-picker inputId="ba-from" formControlName="effectiveFrom" /></vms-field>
        <vms-field label="Effective to" [control]="form.controls.effectiveTo" for="ba-to" hint="Optional."><vms-date-picker inputId="ba-to" formControlName="effectiveTo" /></vms-field>
        <div class="full"><p-checkbox formControlName="isDefault" [binary]="true" inputId="ba-default" /> <label for="ba-default">Default billing address</label></div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="address() ? 'Save changes' : 'Add address'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }`],
})
export class CustomerBillingAddressDialogComponent extends ActionDialog {
  private readonly api = inject(CustomersApi);
  private readonly fb = inject(FormBuilder);

  readonly customerId = input.required<number>();
  readonly address = input<CustomerBillingAddressModel | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.address());

  readonly form = this.fb.group({
    addressName: this.fb.nonNullable.control('', Validators.required),
    cityId: this.fb.control<number | null>(null, Validators.required),
    addressLine1: this.fb.nonNullable.control('', Validators.required),
    addressLine2: this.fb.nonNullable.control(''),
    provinceState: this.fb.nonNullable.control(''),
    postalCode: this.fb.nonNullable.control(''),
    ntn: this.fb.nonNullable.control(''),
    strn: this.fb.nonNullable.control(''),
    effectiveFrom: this.fb.control<string | null>(null),
    effectiveTo: this.fb.control<string | null>(null),
    isDefault: this.fb.nonNullable.control(false),
  });

  constructor() {
    super();
    effect(() => {
      const a = this.address();
      const adding = this.adding();
      if (!adding && !a) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(a
          ? { addressName: a.addressName, cityId: a.cityId, addressLine1: a.addressLine1, addressLine2: a.addressLine2 ?? '', provinceState: a.provinceState ?? '', postalCode: a.postalCode ?? '', ntn: a.ntn ?? '', strn: a.strn ?? '', effectiveFrom: a.effectiveFrom, effectiveTo: a.effectiveTo ?? null, isDefault: a.isDefault }
          : { addressName: '', cityId: null, addressLine1: '', addressLine2: '', provinceState: '', postalCode: '', ntn: '', strn: '', effectiveFrom: null, effectiveTo: null, isDefault: false });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const body = {
      addressName: f.addressName.trim(), cityId: f.cityId!, addressLine1: f.addressLine1.trim(), addressLine2: f.addressLine2.trim() || null, provinceState: f.provinceState.trim() || null,
      postalCode: f.postalCode.trim() || null, ntn: f.ntn.trim() || null, strn: f.strn.trim() || null, isDefault: f.isDefault, effectiveFrom: f.effectiveFrom, effectiveTo: f.effectiveTo,
    };
    const a = this.address();
    const request = a ? this.api.updateBillingAddress(a.customerBillingAddressId, body) : this.api.createBillingAddress(this.customerId(), body);
    this.run(request, this.form, a ? 'Address updated' : 'Address added', () => this.saved.emit());
  }
}
