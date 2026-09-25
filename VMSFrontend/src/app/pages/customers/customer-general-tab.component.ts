import { Component, inject, input, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { CurrenciesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { CurrencyModel } from '../../core/customer.models';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { CustomerForm } from './customer-form';

/**
 * The General tab (FSD §10). The currency field is the one place this cluster has to degrade gracefully: the
 * currency master list is gated to `TRP.CURRENCY.MANAGE` (Admin, §48.8), narrower than the `TRP.CUSTOMER.EDIT`
 * this screen otherwise needs — see {@link CurrencyModel}'s own doc comment. Whoever cannot list currencies
 * (or whose list call fails for any other reason) gets a plain text box instead of an empty, broken dropdown.
 */
@Component({
  selector: 'app-customer-general-tab',
  standalone: true,
  imports: [ReactiveFormsModule, InputTextModule, InputNumberModule, SelectModule, TextareaModule, FieldComponent, LookupPickerComponent],
  template: `
    <div class="form-grid" [formGroup]="form()">
      <vms-field label="Customer code" [control]="form().controls.customerCode" for="cg-code" hint="Leave blank to auto-generate.">
        <input pInputText id="cg-code" formControlName="customerCode" [readonly]="mode() === 'edit'" />
      </vms-field>
      <vms-field label="Customer name" [control]="form().controls.customerName" for="cg-name"><input pInputText id="cg-name" formControlName="customerName" /></vms-field>
      <vms-field label="Short name" [control]="form().controls.shortName" for="cg-short"><input pInputText id="cg-short" formControlName="shortName" /></vms-field>

      <vms-field class="full" label="Address line 1" [control]="form().controls.addressLine1" for="cg-l1"><input pInputText id="cg-l1" formControlName="addressLine1" /></vms-field>
      <vms-field class="full" label="Address line 2" [control]="form().controls.addressLine2" for="cg-l2"><input pInputText id="cg-l2" formControlName="addressLine2" /></vms-field>
      <vms-field label="Country" [control]="form().controls.countryId" for="cg-country"><vms-lookup-picker type="COUNTRY" formControlName="countryId" inputId="cg-country" placeholder="Choose a country" /></vms-field>
      <vms-field label="Province/State" [control]="form().controls.provinceState" for="cg-province"><input pInputText id="cg-province" formControlName="provinceState" /></vms-field>
      <vms-field label="City" [control]="form().controls.cityId" for="cg-city"><vms-lookup-picker type="CITY" formControlName="cityId" inputId="cg-city" placeholder="Choose a city" /></vms-field>
      <vms-field label="Postal code" [control]="form().controls.postalCode" for="cg-postal"><input pInputText id="cg-postal" formControlName="postalCode" /></vms-field>

      <vms-field label="NTN" [control]="form().controls.ntn" for="cg-ntn"><input pInputText id="cg-ntn" formControlName="ntn" /></vms-field>
      <vms-field label="STRN" [control]="form().controls.strn" for="cg-strn"><input pInputText id="cg-strn" formControlName="strn" /></vms-field>
      <vms-field label="Other registration no." [control]="form().controls.otherRegistrationNo" for="cg-other"><input pInputText id="cg-other" formControlName="otherRegistrationNo" /></vms-field>

      <vms-field label="Currency" [control]="form().controls.currencyCode" for="cg-currency" [hint]="currencyHint()">
        @if (currencies(); as list) {
          <p-select inputId="cg-currency" formControlName="currencyCode" [options]="currencyOptions(list)" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        } @else {
          <input pInputText id="cg-currency" formControlName="currencyCode" maxlength="3" style="text-transform: uppercase" />
        }
      </vms-field>
      <vms-field label="Payment terms (days)" [control]="form().controls.paymentTermsDays" for="cg-terms">
        <p-inputnumber inputId="cg-terms" formControlName="paymentTermsDays" [min]="0" [max]="365" [useGrouping]="false" [fluid]="true" />
      </vms-field>
      <vms-field label="Credit limit" [control]="form().controls.creditLimit" for="cg-credit">
        <p-inputnumber inputId="cg-credit" formControlName="creditLimit" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" />
      </vms-field>

      <vms-field class="full" label="Remarks" [control]="form().controls.remarks" for="cg-remarks">
        <textarea pTextarea id="cg-remarks" formControlName="remarks" rows="3" [fluid]="true"></textarea>
      </vms-field>
    </div>
  `,
  styles: [`.form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }`],
})
export class CustomerGeneralTabComponent {
  private readonly currenciesApi = inject(CurrenciesApi);
  private readonly auth = inject(AuthService);

  readonly form = input.required<CustomerForm>();
  readonly mode = input<'create' | 'edit'>('create');

  protected readonly currencies = signal<CurrencyModel[] | null>(null);
  protected readonly currencyHint = () => (this.currencies() ? '' : 'No permission to browse the currency list — type a 3-letter ISO code (e.g. PKR).');
  protected readonly currencyOptions = (list: CurrencyModel[]) => list.map((c) => ({ label: `${c.currencyCode} — ${c.currencyName}`, value: c.currencyCode }));

  constructor() {
    if (this.auth.hasPermission('TRP.CURRENCY.MANAGE')) {
      this.currenciesApi.list().subscribe({ next: (list) => this.currencies.set(list), error: () => this.currencies.set(null) });
    }
  }
}
