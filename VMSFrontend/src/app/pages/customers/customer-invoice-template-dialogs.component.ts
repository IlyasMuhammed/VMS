import { Component, computed, effect, inject, input, output, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { CustomersApi } from '../../core/api.services';
import { CustomerInvoiceTemplateModel, InvoiceTemplateType } from '../../core/customer.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { ActionDialog } from './customer-dialogs.component';
import { INVOICE_TEMPLATE_TYPES, optionsOf } from './customer-logic';

/** Add a template, or start a new Draft version off an existing one (FSD §15, screen 7). No file upload here —
 * the backend has no storage endpoint for a template's actual file yet, so `templateReference` is a plain
 * pointer (a path or a report name) the operator types in, not something this dialog uploads. */
@Component({
  selector: 'app-customer-invoice-template-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, SelectModule, FieldComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '560px' }" [header]="newVersionOf() ? 'New version of ' + newVersionOf()!.templateName : 'Add an invoice template'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="form-grid" novalidate>
        @if (!newVersionOf()) {
          <vms-field class="full" label="Template name" [control]="form.controls.templateName" for="it-name"><input pInputText id="it-name" formControlName="templateName" /></vms-field>
        }
        <vms-field label="Type" [control]="form.controls.templateType" for="it-type">
          <p-select inputId="it-type" formControlName="templateType" [options]="types" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
        </vms-field>
        <vms-field label="Reference" [control]="form.controls.templateReference" for="it-ref" hint="A file path or report name — there is no upload yet.">
          <input pInputText id="it-ref" formControlName="templateReference" />
        </vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="newVersionOf() ? 'Create new version' : 'Add template'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }`],
})
export class CustomerInvoiceTemplateDialogComponent extends ActionDialog {
  private readonly api = inject(CustomersApi);
  private readonly fb = inject(FormBuilder);

  readonly customerId = input.required<number>();
  readonly adding = input(false);
  readonly newVersionOf = input<CustomerInvoiceTemplateModel | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.newVersionOf());
  protected readonly types = optionsOf(INVOICE_TEMPLATE_TYPES);

  readonly form = this.fb.group({
    templateName: this.fb.nonNullable.control('', Validators.required),
    templateType: this.fb.nonNullable.control<InvoiceTemplateType>('SystemStandard'),
    templateReference: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      const t = this.newVersionOf();
      const adding = this.adding();
      if (!adding && !t) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(t
          ? { templateName: t.templateName, templateType: t.templateType, templateReference: t.templateReference }
          : { templateName: '', templateType: 'SystemStandard', templateReference: '' });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const t = this.newVersionOf();
    const request = t
      ? this.api.newInvoiceTemplateVersion(t.customerInvoiceTemplateId, { templateType: f.templateType, templateReference: f.templateReference.trim() || null })
      : this.api.createInvoiceTemplate(this.customerId(), { templateName: f.templateName.trim(), templateType: f.templateType, templateReference: f.templateReference.trim() || null });
    this.run(request, this.form, t ? 'New version created' : 'Template added', () => this.saved.emit());
  }
}

/** Activate a Draft version (FSD §15: "Activate sets it live from a date; may also become the default"). */
@Component({
  selector: 'app-customer-invoice-template-activate-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, CheckboxModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="!!template()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '460px' }" header="Activate template version" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Effective from" [control]="form.controls.effectiveFrom" for="ita-from" hint="Defaults to today."><vms-date-picker inputId="ita-from" formControlName="effectiveFrom" /></vms-field>
        <div><p-checkbox formControlName="isDefault" [binary]="true" inputId="ita-default" /> <label for="ita-default">Make this the default template</label></div>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Activate" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class CustomerInvoiceTemplateActivateDialogComponent extends ActionDialog {
  private readonly api = inject(CustomersApi);
  private readonly fb = inject(FormBuilder);

  readonly template = input<CustomerInvoiceTemplateModel | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly form = this.fb.group({
    effectiveFrom: this.fb.control<string | null>(null),
    isDefault: this.fb.nonNullable.control(false),
  });

  constructor() {
    super();
    effect(() => {
      const t = this.template();
      if (!t) return;
      untracked(() => { this.problems.set([]); this.form.reset({ effectiveFrom: null, isDefault: false }); });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const t = this.template();
    if (!t) return;
    const f = this.form.getRawValue();
    this.run(this.api.activateInvoiceTemplate(t.customerInvoiceTemplateId, { effectiveFrom: f.effectiveFrom, isDefault: f.isDefault }), this.form, 'Template activated', () => this.saved.emit());
  }
}
