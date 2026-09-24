import { Component, inject, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectButtonModule } from 'primeng/selectbutton';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { BranchPickerComponent } from '../../shared/branch-picker.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { Permissions, PartnerForm, newAddress, newBankAccount, newContact, setPrimary } from './partner-form';
import { RoleChipsComponent } from './partner-bits.component';
import { ADDRESS_TYPES, FILER_STATUSES, PARTY_TYPES, ROLES, maskCnic, optionsOf, panelsFor, panellessRoles, roleLabel } from './partner-logic';
import { CustomerPanelComponent, DriverPanelComponent, VendorPanelComponent } from './role-panels.component';

/**
 * The content of each tab of the partner form (FSD §9.2). Each is given the whole form and draws its part; none of them
 * knows how the tabs are laid out, so the tab strip can change without touching them.
 */

// ── General ────────────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-general-tab',
  standalone: true,
  imports: [
    ReactiveFormsModule, InputTextModule, InputNumberModule, TextareaModule, SelectModule, SelectButtonModule, MultiSelectModule,
    FieldComponent, DatePickerComponent, LookupPickerComponent, BranchPickerComponent, RoleChipsComponent,
  ],
  template: `
    <div class="form-grid" [formGroup]="form()">
      <vms-field class="full" label="Party type" [control]="form().controls.partyType" hint="A person is identified by a CNIC, a company by an NTN.">
        <p-selectbutton formControlName="partyType" [options]="partyTypes" optionLabel="label" optionValue="value" [allowEmpty]="false" ariaLabel="Party type" />
      </vms-field>

      <vms-field label="Legal name" [control]="form().controls.legalName" for="gn-legal" hint="As on the CNIC or NTN certificate.">
        <input pInputText id="gn-legal" formControlName="legalName" autocomplete="off" />
      </vms-field>
      <vms-field label="Display name" [control]="form().controls.displayName" for="gn-display" hint="Shown in lists. Leave blank to use the legal name.">
        <input pInputText id="gn-display" formControlName="displayName" />
      </vms-field>

      @if (mode() === 'create') {
        <vms-field class="full" label="Roles" [control]="form().controls.roles" for="gn-roles" hint="Choose everything this partner does for you. More can be added later.">
          <p-multiselect inputId="gn-roles" formControlName="roles" [options]="roleOptions" optionLabel="label" optionValue="value" display="chip" placeholder="Choose at least one role" [fluid]="true" appendTo="body" />
        </vms-field>
      } @else {
        <div class="field full">
          <label>Roles</label>
          <vms-role-chips [roles]="form().controls.roles.value" />
          <span class="hint">Add or remove roles with the buttons above, so each change is recorded with its reason.</span>
        </div>
      }

      @if (form().controls.partyType.value !== 'Company') {
        <vms-field label="CNIC" [control]="form().controls.cnic" for="gn-cnic" hint="00000-0000000-0">
          <input pInputText id="gn-cnic" formControlName="cnic" inputmode="numeric" autocomplete="off" (input)="mask($event)" maxlength="15" />
        </vms-field>
      }
      <vms-field label="NTN" [control]="form().controls.ntn" for="gn-ntn" hint="For example 1234567-8.">
        <input pInputText id="gn-ntn" formControlName="ntn" autocomplete="off" />
      </vms-field>
      <vms-field label="STRN" [control]="form().controls.strn" for="gn-strn"><input pInputText id="gn-strn" formControlName="strn" /></vms-field>
      <vms-field label="Filer status" [control]="form().controls.filerStatus" for="gn-filer">
        <p-select inputId="gn-filer" formControlName="filerStatus" [options]="filerStatuses" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
      </vms-field>

      <vms-field label="Primary mobile" [control]="form().controls.primaryMobile" for="gn-mobile" hint="0300-1234567 or +92…">
        <input pInputText id="gn-mobile" formControlName="primaryMobile" inputmode="tel" autocomplete="off" />
      </vms-field>
      <vms-field label="Alternate phone" [control]="form().controls.alternatePhone" for="gn-alt"><input pInputText id="gn-alt" formControlName="alternatePhone" inputmode="tel" /></vms-field>
      <vms-field class="full" label="Email" [control]="form().controls.email" for="gn-email" hint="Statements and notices are sent here.">
        <input pInputText id="gn-email" type="email" formControlName="email" autocomplete="off" />
      </vms-field>

      <vms-field label="City" [control]="form().controls.cityId" for="gn-city">
        <vms-lookup-picker type="CITY" formControlName="cityId" inputId="gn-city" placeholder="Choose a city" />
      </vms-field>
      <vms-field label="Address" [control]="form().controls.addressLine" for="gn-address" hint="Also saved as the registered address.">
        <input pInputText id="gn-address" formControlName="addressLine" />
      </vms-field>
      <vms-field label="Branch" [control]="form().controls.branchId" for="gn-branch" hint="Which of the company's own locations this partner belongs to.">
        <vms-branch-picker formControlName="branchId" inputId="gn-branch" placeholder="Choose a branch" />
      </vms-field>

      @if (perms().opening) {
        @if (mode() === 'create') {
          <vms-field label="Opening balance (PKR)" [control]="form().controls.openingBalance" for="gn-opening" hint="Positive: we owe the partner. Negative: the partner owes us. Entered once.">
            <p-inputnumber inputId="gn-opening" formControlName="openingBalance" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" />
          </vms-field>
          <vms-field label="As of" [control]="form().controls.openingBalanceDate" for="gn-opening-date" hint="Needed when there is a balance. Not in the future.">
            <vms-date-picker inputId="gn-opening-date" formControlName="openingBalanceDate" [notFuture]="true" />
          </vms-field>
        } @else {
          <div class="field">
            <label>Opening balance (PKR)</label>
            <span>{{ form().controls.openingBalance.value ?? 0 }}</span>
            <span class="hint">Fixed once saved. A correction is a journal adjustment.</span>
          </div>
        }
      }

      <vms-field class="full" label="Notes" [control]="form().controls.notes" for="gn-notes">
        <textarea pTextarea id="gn-notes" formControlName="notes" rows="3" [fluid]="true"></textarea>
      </vms-field>
    </div>
  `,
})
export class GeneralTabComponent {
  readonly form = input.required<PartnerForm>();
  readonly mode = input<'create' | 'edit'>('create');
  readonly perms = input.required<Permissions>();

  protected readonly partyTypes = optionsOf(PARTY_TYPES);
  protected readonly filerStatuses = optionsOf(FILER_STATUSES);
  protected readonly roleOptions = ROLES.map((r) => ({ label: r.label, value: r.code }));

  /** The dashes go in as the CNIC is typed. */
  protected mask(event: Event): void {
    const control = this.form().controls.cnic;
    const masked = maskCnic((event.target as HTMLInputElement).value);
    if (masked !== control.value) control.setValue(masked);
  }
}

// ── Role details ───────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-role-details-tab',
  standalone: true,
  imports: [DriverPanelComponent, VendorPanelComponent, CustomerPanelComponent],
  template: `
    @if (form().controls.roles.value.length === 0) {
      <p class="muted">Choose at least one role on the General tab. Roles with their own details appear here.</p>
    }

    @if (has('Driver')) {
      <section class="panel"><h2>Driver</h2><app-driver-panel [group]="form().controls.driver" [salary]="perms().salary" [creating]="creating()" /></section>
    }
    @if (has('Vendor')) {
      <section class="panel"><h2>Vendor</h2><app-vendor-panel [group]="form().controls.vendor" [credit]="perms().credit" /></section>
    }
    @if (has('Customer')) {
      <section class="panel"><h2>Customer</h2><app-customer-panel [group]="form().controls.customer" [credit]="perms().credit" /></section>
    }

    @if (others().length > 0) {
      <section class="panel">
        <h2>Other roles</h2>
        <p class="muted">
          {{ others() }} {{ othersCount() === 1 ? 'has' : 'have' }} no details of {{ othersCount() === 1 ? 'its' : 'their' }} own to record yet. Nothing to fill in here.
        </p>
      </section>
    }
  `,
  styles: [`.panel { margin-bottom: 1.5rem; } .panel h2 { font-size: 1.05rem; margin-bottom: .75rem; }`],
})
export class RoleDetailsTabComponent {
  readonly form = input.required<PartnerForm>();
  readonly perms = input.required<Permissions>();
  readonly creating = input(true);

  protected has(role: string): boolean {
    return panelsFor(this.form().controls.roles.value).includes(role);
  }

  protected othersCount(): number {
    return panellessRoles(this.form().controls.roles.value).length;
  }

  protected others(): string {
    return panellessRoles(this.form().controls.roles.value).map(roleLabel).join(', ');
  }
}

// ── Contacts ───────────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-contacts-tab',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, FieldComponent],
  template: `
    <p class="muted intro">People to call about this partner. One can be the main contact.</p>

    @for (row of rows.controls; track row; let i = $index) {
      <div class="row card-row" [formGroup]="row">
        <div class="form-grid cols">
          <vms-field label="Name" [control]="row.controls['contactName']" [for]="'ct-name-' + i"><input pInputText [id]="'ct-name-' + i" formControlName="contactName" /></vms-field>
          <vms-field label="Designation" [control]="row.controls['designation']" [for]="'ct-role-' + i"><input pInputText [id]="'ct-role-' + i" formControlName="designation" /></vms-field>
          <vms-field label="Mobile" [control]="row.controls['mobile']" [for]="'ct-mobile-' + i"><input pInputText [id]="'ct-mobile-' + i" formControlName="mobile" inputmode="tel" /></vms-field>
          <vms-field label="Email" [control]="row.controls['email']" [for]="'ct-email-' + i"><input pInputText [id]="'ct-email-' + i" formControlName="email" /></vms-field>
        </div>
        <div class="row-actions">
          <label class="primary"><input type="radio" [name]="'contact-primary'" [checked]="row.controls['isPrimary'].value" (change)="primary(i)" [disabled]="row.disabled" /> Main contact</label>
          @if (editable()) { <p-button icon="pi pi-trash" [text]="true" severity="danger" [ariaLabel]="'Remove contact ' + (i + 1)" title="Remove" (onClick)="remove(i)" /> }
        </div>
      </div>
    }
    @if (rows.length === 0) { <p class="empty muted">No contacts yet.</p> }
    @if (editable()) { <p-button label="Add contact" icon="pi pi-plus" severity="secondary" [outlined]="true" (onClick)="add()" /> }
  `,
  styleUrl: './partner-rows.scss',
})
export class ContactsTabComponent {
  private readonly fb = inject(FormBuilder);
  readonly form = input.required<PartnerForm>();
  readonly editable = input(true);
  protected get rows() { return this.form().controls.contacts; }

  protected add(): void {
    const first = this.rows.length === 0;
    const row = newContact(this.fb, { isPrimary: first });
    this.rows.push(row);
    this.rows.markAsDirty();
  }

  protected remove(index: number): void {
    const wasPrimary = this.rows.at(index).controls['isPrimary'].value;
    this.rows.removeAt(index);
    if (wasPrimary && this.rows.length > 0) setPrimary(this.rows, 0);
    this.rows.markAsDirty();
  }

  protected primary(index: number): void {
    setPrimary(this.rows, index);
  }
}

// ── Addresses ──────────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-addresses-tab',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, SelectModule, FieldComponent, LookupPickerComponent],
  template: `
    <div class="card-row registered">
      <div><strong>Registered address</strong> <span class="muted">(from the General tab)</span></div>
      <div>{{ form().controls.addressLine.value || '—' }}</div>
    </div>

    <p class="muted intro">Other places: a billing office, a workshop, a yard.</p>

    @for (row of rows.controls; track row; let i = $index) {
      <div class="row card-row" [formGroup]="row">
        <div class="form-grid cols">
          <vms-field label="Type" [control]="row.controls['addressType']" [for]="'ad-type-' + i">
            <p-select [inputId]="'ad-type-' + i" formControlName="addressType" [options]="types" optionLabel="label" optionValue="value" placeholder="Choose…" [fluid]="true" appendTo="body" />
          </vms-field>
          <vms-field label="Address line 1" [control]="row.controls['line1']" [for]="'ad-l1-' + i"><input pInputText [id]="'ad-l1-' + i" formControlName="line1" /></vms-field>
          <vms-field label="Address line 2" [control]="row.controls['line2']" [for]="'ad-l2-' + i"><input pInputText [id]="'ad-l2-' + i" formControlName="line2" /></vms-field>
          <vms-field label="City" [control]="row.controls['cityId']" [for]="'ad-city-' + i"><vms-lookup-picker type="CITY" formControlName="cityId" [inputId]="'ad-city-' + i" placeholder="Choose a city" /></vms-field>
          <vms-field label="Landmark" [control]="row.controls['landmark']" [for]="'ad-land-' + i"><input pInputText [id]="'ad-land-' + i" formControlName="landmark" /></vms-field>
        </div>
        @if (editable()) {
          <div class="row-actions"><span></span><p-button icon="pi pi-trash" [text]="true" severity="danger" [ariaLabel]="'Remove address ' + (i + 1)" title="Remove" (onClick)="remove(i)" /></div>
        }
      </div>
    }
    @if (rows.length === 0) { <p class="empty muted">No other addresses.</p> }
    @if (editable()) { <p-button label="Add address" icon="pi pi-plus" severity="secondary" [outlined]="true" (onClick)="add()" /> }
  `,
  styleUrl: './partner-rows.scss',
})
export class AddressesTabComponent {
  private readonly fb = inject(FormBuilder);
  readonly form = input.required<PartnerForm>();
  readonly editable = input(true);
  protected readonly types = optionsOf(ADDRESS_TYPES.filter((t) => t !== 'Registered'));
  protected get rows() { return this.form().controls.addresses; }

  protected add(): void {
    this.rows.push(newAddress(this.fb, { cityId: this.form().controls.cityId.value }));
    this.rows.markAsDirty();
  }

  protected remove(index: number): void {
    this.rows.removeAt(index);
    this.rows.markAsDirty();
  }
}

// ── Bank accounts ──────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-bank-accounts-tab',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, FieldComponent],
  template: `
    <p class="muted intro">Where this partner is paid. One can be the main account.</p>

    @for (row of rows.controls; track row; let i = $index) {
      <div class="row card-row" [formGroup]="row">
        <div class="form-grid cols">
          <vms-field label="Account title" [control]="row.controls['accountTitle']" [for]="'bk-title-' + i"><input pInputText [id]="'bk-title-' + i" formControlName="accountTitle" /></vms-field>
          <vms-field label="Bank" [control]="row.controls['bankName']" [for]="'bk-bank-' + i"><input pInputText [id]="'bk-bank-' + i" formControlName="bankName" /></vms-field>
          <vms-field label="Branch / code" [control]="row.controls['branchCode']" [for]="'bk-branch-' + i"><input pInputText [id]="'bk-branch-' + i" formControlName="branchCode" /></vms-field>
          <vms-field label="Account number" [control]="row.controls['accountNumber']" [for]="'bk-number-' + i" hint="Digits and dashes."><input pInputText [id]="'bk-number-' + i" formControlName="accountNumber" inputmode="numeric" /></vms-field>
          <vms-field label="IBAN" [control]="row.controls['iban']" [for]="'bk-iban-' + i" hint="PK and 22 characters, for example PK36SCBL0000001123456702.">
            <input pInputText [id]="'bk-iban-' + i" formControlName="iban" autocomplete="off" />
          </vms-field>
        </div>
        <div class="row-actions">
          <label class="primary"><input type="radio" name="bank-primary" [checked]="row.controls['isPrimary'].value" (change)="primary(i)" [disabled]="row.disabled" /> Main account</label>
          @if (editable()) { <p-button icon="pi pi-trash" [text]="true" severity="danger" [ariaLabel]="'Remove bank account ' + (i + 1)" title="Remove" (onClick)="remove(i)" /> }
        </div>
      </div>
    }
    @if (rows.length === 0) { <p class="empty muted">No bank accounts yet.</p> }
    @if (editable()) { <p-button label="Add bank account" icon="pi pi-plus" severity="secondary" [outlined]="true" (onClick)="add()" /> }
  `,
  styleUrl: './partner-rows.scss',
})
export class BankAccountsTabComponent {
  private readonly fb = inject(FormBuilder);
  readonly form = input.required<PartnerForm>();
  readonly editable = input(true);
  protected get rows() { return this.form().controls.bankAccounts; }

  protected add(): void {
    this.rows.push(newBankAccount(this.fb, { isPrimary: this.rows.length === 0 }));
    this.rows.markAsDirty();
  }

  protected remove(index: number): void {
    const wasPrimary = this.rows.at(index).controls['isPrimary'].value;
    this.rows.removeAt(index);
    if (wasPrimary && this.rows.length > 0) setPrimary(this.rows, 0);
    this.rows.markAsDirty();
  }

  protected primary(index: number): void {
    setPrimary(this.rows, index);
  }
}
