import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { CustomersApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { CustomerBillingAddressModel, CustomerBillingConfigurationModel, CustomerContactModel } from '../../core/customer.models';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { DUPLICATE_REFERENCE_BEHAVIOURS, optionsOf } from './customer-logic';

/** Billing Configuration (FSD §13, screen 5): "Save creates a new effective version" — there is no in-place
 * edit, so the form always writes forward from today, never onto the row already showing. */
@Component({
  selector: 'app-customer-billing-configuration-tab',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, CheckboxModule, InputNumberModule, InputTextModule, SelectModule, FieldComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
    @if (config()) {
      <form [formGroup]="form" class="form-grid" novalidate>
        <h2 class="full">Terms &amp; currency</h2>
        <vms-field label="Payment terms (days)" [control]="form.controls.paymentTermsDays" for="bc-terms">
          <p-inputnumber inputId="bc-terms" formControlName="paymentTermsDays" [min]="0" [max]="365" [useGrouping]="false" [fluid]="true" />
        </vms-field>
        <vms-field label="Currency" [control]="form.controls.currencyCode" for="bc-currency" hint="Only editable when multi-currency is on for this tenant.">
          <input pInputText id="bc-currency" formControlName="currencyCode" maxlength="3" style="text-transform: uppercase" />
        </vms-field>

        <h2 class="full">Defaults</h2>
        <vms-field label="Default billing address" [control]="form.controls.defaultBillingAddressId" for="bc-address">
          <p-select inputId="bc-address" formControlName="defaultBillingAddressId" [options]="addressOptions()" optionLabel="label" optionValue="value" [showClear]="true" placeholder="None" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Invoice number prefix" [control]="form.controls.invoiceNumberPrefix" for="bc-prefix"><input pInputText id="bc-prefix" formControlName="invoiceNumberPrefix" maxlength="10" /></vms-field>

        <h2 class="full">POD &amp; evidence</h2>
        <div><p-checkbox formControlName="podRequired" [binary]="true" inputId="bc-pod" /> <label for="bc-pod">POD required before an invoice's trip can complete</label></div>
        <div><p-checkbox formControlName="evidenceRequired" [binary]="true" inputId="bc-evidence" /> <label for="bc-evidence">Evidence required before submitting an invoice</label></div>
        <vms-field label="Evidence page size" [control]="form.controls.evidencePageSize" for="bc-pagesize">
          <p-inputnumber inputId="bc-pagesize" formControlName="evidencePageSize" [min]="10" [max]="200" [useGrouping]="false" [fluid]="true" />
        </vms-field>

        <h2 class="full">References</h2>
        <vms-field label="Duplicate trip reference" [control]="form.controls.duplicateReferenceBehaviour" for="bc-dup">
          <p-select inputId="bc-dup" formControlName="duplicateReferenceBehaviour" [options]="dupOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        <div><p-checkbox formControlName="customerReferenceRequired" [binary]="true" inputId="bc-refreq" /> <label for="bc-refreq">Customer reference required on every trip</label></div>

        <h2 class="full">Payments</h2>
        <vms-field label="Statement email contact" [control]="form.controls.statementEmailContactId" for="bc-contact">
          <p-select inputId="bc-contact" formControlName="statementEmailContactId" [options]="contactOptions()" optionLabel="label" optionValue="value" [showClear]="true" placeholder="None" [fluid]="true" appendTo="body" />
        </vms-field>
      </form>
      @if (canEdit()) {
        <div class="savebar"><p-button label="Save" icon="pi pi-check" [loading]="saving()" (onClick)="save()" /></div>
      }
    } @else if (!error()) { <p class="muted">Loading…</p> }
  `,
  styles: [
    `
      .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; align-items: end; }
      .full { grid-column: 1 / -1; } h2 { font-size: 1.05rem; margin: 1rem 0 0; } h2.full:first-child { margin-top: 0; }
      .savebar { margin-top: 1.5rem; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class CustomerBillingConfigurationTabComponent {
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly customerId = input.required<number>();
  protected readonly config = signal<CustomerBillingConfigurationModel | null>(null);
  protected readonly addresses = signal<CustomerBillingAddressModel[]>([]);
  protected readonly contacts = signal<CustomerContactModel[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly problems = signal<string[]>([]);
  protected readonly saving = signal(false);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.CUSTOMER.EDIT');

  protected readonly dupOptions = optionsOf(DUPLICATE_REFERENCE_BEHAVIOURS);
  protected readonly addressOptions = () => this.addresses().filter((a) => a.status === 'Active').map((a) => ({ label: a.addressName, value: a.customerBillingAddressId }));
  protected readonly contactOptions = () => this.contacts().filter((c) => c.status === 'Active').map((c) => ({ label: c.name, value: c.customerContactId }));

  readonly form = this.fb.group({
    paymentTermsDays: this.fb.nonNullable.control(30, [Validators.required, Validators.min(0), Validators.max(365)]),
    currencyCode: this.fb.nonNullable.control('PKR', Validators.required),
    defaultBillingAddressId: this.fb.control<number | null>(null),
    invoiceNumberPrefix: this.fb.nonNullable.control('INV', Validators.required),
    podRequired: this.fb.nonNullable.control(false),
    evidenceRequired: this.fb.nonNullable.control(true),
    evidencePageSize: this.fb.nonNullable.control(50, [Validators.min(10), Validators.max(200)]),
    duplicateReferenceBehaviour: this.fb.nonNullable.control<'Allow' | 'Warn' | 'Block'>('Warn'),
    customerReferenceRequired: this.fb.nonNullable.control(false),
    statementEmailContactId: this.fb.control<number | null>(null),
  });

  constructor() {
    effect(() => { this.customerId(); untracked(() => this.load()); });
  }

  load(): void {
    this.error.set(null);
    const id = this.customerId();
    this.api.billingConfiguration(id).subscribe({
      next: (c) => {
        this.config.set(c);
        this.form.reset({
          paymentTermsDays: c.paymentTermsDays, currencyCode: c.currencyCode, defaultBillingAddressId: c.defaultBillingAddressId ?? null, invoiceNumberPrefix: c.invoiceNumberPrefix,
          podRequired: c.podRequired, evidenceRequired: c.evidenceRequired, evidencePageSize: c.evidencePageSize, duplicateReferenceBehaviour: c.duplicateReferenceBehaviour,
          customerReferenceRequired: c.customerReferenceRequired, statementEmailContactId: c.statementEmailContactId ?? null,
        });
      },
      error: (err) => this.error.set(errorMessage(err, 'The billing configuration could not be loaded.')),
    });
    this.api.billingAddresses(id, false).subscribe({ next: (r) => this.addresses.set(r), error: () => undefined });
    this.api.contacts(id, false).subscribe({ next: (r) => this.contacts.set(r), error: () => undefined });
  }

  save(): void {
    if (this.saving() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.saving.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    this.api.saveBillingConfiguration(this.customerId(), {
      paymentTermsDays: f.paymentTermsDays, currencyCode: f.currencyCode.trim().toUpperCase(), defaultBillingAddressId: f.defaultBillingAddressId, defaultInvoiceTemplateId: this.config()?.defaultInvoiceTemplateId ?? null,
      invoiceNumberPrefix: f.invoiceNumberPrefix.trim(), podRequired: f.podRequired, evidenceRequired: f.evidenceRequired, evidencePageSize: f.evidencePageSize,
      duplicateReferenceBehaviour: f.duplicateReferenceBehaviour, customerReferenceRequired: f.customerReferenceRequired, statementEmailContactId: f.statementEmailContactId,
    }).subscribe({
      next: (c) => { this.saving.set(false); this.config.set(c); this.notify.success('Billing configuration saved'); },
      error: (err) => {
        this.saving.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
