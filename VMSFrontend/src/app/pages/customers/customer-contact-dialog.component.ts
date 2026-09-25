import { Component, computed, effect, inject, input, output, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { CustomersApi } from '../../core/api.services';
import { ContactPurpose, CustomerContactModel } from '../../core/customer.models';
import { FieldComponent } from '../../shared/field.component';
import { ActionDialog } from './customer-dialogs.component';
import { CONTACT_PURPOSES, isEmail, isMobile, isPhone, optionsOf } from './customer-logic';

const format = (test: (value: string) => boolean): ValidatorFn => (control) => {
  const value = (control.value ?? '') as string;
  return value.trim() === '' || test(value.trim()) ? null : { pattern: true };
};
const mobileFormat = format(isMobile);
const phoneFormat = format(isPhone);
const emailFormat = format(isEmail);

/** Add or edit a customer contact (FSD §11, screen 3: "Tab grid + side panel form"). */
@Component({
  selector: 'app-customer-contact-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, CheckboxModule, MultiSelectModule, FieldComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '560px' }" [header]="contact() ? 'Edit contact' : 'Add a contact'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        <vms-field label="Name" [control]="form.controls.name" for="ct-name"><input pInputText id="ct-name" formControlName="name" /></vms-field>
        <vms-field label="Designation" [control]="form.controls.designation" for="ct-designation"><input pInputText id="ct-designation" formControlName="designation" /></vms-field>
        <vms-field label="Mobile" [control]="form.controls.mobile1" for="ct-mobile1" hint="0300-1234567 or +92…"><input pInputText id="ct-mobile1" formControlName="mobile1" inputmode="tel" /></vms-field>
        <vms-field label="Alternate mobile" [control]="form.controls.mobile2" for="ct-mobile2"><input pInputText id="ct-mobile2" formControlName="mobile2" inputmode="tel" /></vms-field>
        <vms-field label="Telephone" [control]="form.controls.telephone" for="ct-tel"><input pInputText id="ct-tel" formControlName="telephone" inputmode="tel" /></vms-field>
        <vms-field label="Email" [control]="form.controls.email" for="ct-email"><input pInputText id="ct-email" type="email" formControlName="email" /></vms-field>
        <vms-field class="full" label="Purpose" [control]="form.controls.purpose" for="ct-purpose">
          <p-multiselect inputId="ct-purpose" formControlName="purpose" [options]="purposes" optionLabel="label" optionValue="value" display="chip" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field class="full" label="Availability" [control]="form.controls.availabilityTime" for="ct-avail" hint="Free text, e.g. 'Weekdays 9am-5pm'.">
          <input pInputText id="ct-avail" formControlName="availabilityTime" />
        </vms-field>
        <div class="full"><p-checkbox formControlName="isPrimary" [binary]="true" inputId="ct-primary" /> <label for="ct-primary">Main contact</label></div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="contact() ? 'Save changes' : 'Add contact'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }`],
})
export class CustomerContactDialogComponent extends ActionDialog {
  private readonly api = inject(CustomersApi);
  private readonly fb = inject(FormBuilder);

  readonly customerId = input.required<number>();
  readonly contact = input<CustomerContactModel | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.contact());
  protected readonly purposes = optionsOf(CONTACT_PURPOSES);

  readonly form = this.fb.group({
    name: this.fb.nonNullable.control('', Validators.required),
    designation: this.fb.nonNullable.control(''),
    mobile1: this.fb.nonNullable.control('', [Validators.required, mobileFormat]),
    mobile2: this.fb.nonNullable.control('', mobileFormat),
    telephone: this.fb.nonNullable.control('', phoneFormat),
    email: this.fb.nonNullable.control('', emailFormat),
    availabilityTime: this.fb.nonNullable.control(''),
    purpose: this.fb.nonNullable.control<ContactPurpose[]>([]),
    isPrimary: this.fb.nonNullable.control(false),
  });

  constructor() {
    super();
    effect(() => {
      const c = this.contact();
      const adding = this.adding();
      if (!adding && !c) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(c
          ? { name: c.name, designation: c.designation ?? '', mobile1: c.mobile1, mobile2: c.mobile2 ?? '', telephone: c.telephone ?? '', email: c.email ?? '', availabilityTime: c.availabilityTime ?? '', purpose: c.purpose, isPrimary: c.isPrimary }
          : { name: '', designation: '', mobile1: '', mobile2: '', telephone: '', email: '', availabilityTime: '', purpose: [], isPrimary: false });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const body = {
      name: f.name.trim(), designation: f.designation.trim() || null, mobile1: f.mobile1.trim(), mobile2: f.mobile2.trim() || null, telephone: f.telephone.trim() || null,
      email: f.email.trim() || null, availabilityTime: f.availabilityTime.trim() || null, purpose: f.purpose, isPrimary: f.isPrimary,
    };
    const c = this.contact();
    const request = c ? this.api.updateContact(c.customerContactId, body) : this.api.createContact(this.customerId(), body);
    this.run(request, this.form, c ? 'Contact updated' : 'Contact added', () => this.saved.emit());
  }
}
