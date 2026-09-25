import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { CustomersApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CustomerInvoiceTemplateModel } from '../../core/customer.models';
import { CustomerInvoiceTemplateActivateDialogComponent, CustomerInvoiceTemplateDialogComponent } from './customer-invoice-template-dialogs.component';
import { words } from './customer-logic';

/** Invoice Templates (FSD §15, screen 7): each named template is a chain of versions, at most one Active
 * version live at a time, at most one template Default overall. No Preview here — see the dialog's own note
 * on why there is nothing to render yet. */
@Component({
  selector: 'app-customer-invoice-templates-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, BusinessDatePipe, CustomerInvoiceTemplateDialogComponent, CustomerInvoiceTemplateActivateDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Add template" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Name</th><th>Ver</th><th>Type</th><th>Reference</th><th>Default</th><th>Effective</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (t of templates(); track t.customerInvoiceTemplateId) {
          <tr>
            <td>{{ t.templateName }}</td><td>{{ t.version }}</td><td>{{ words(t.templateType) }}</td><td>{{ t.templateReference || '—' }}</td>
            <td>@if (t.isDefault) { <p-tag value="Default" severity="info" /> }</td>
            <td class="nowrap">{{ t.effectiveFrom | vmsDate }}@if (t.effectiveTo) { – {{ t.effectiveTo | vmsDate }} }</td>
            <td><p-tag [value]="t.status" [severity]="t.status === 'Active' ? 'success' : t.status === 'Draft' ? 'warn' : 'danger'" /></td>
            <td class="row-actions">
              @if (canEdit()) {
                @if (t.status === 'Draft') { <p-button label="Activate" [text]="true" size="small" (onClick)="activating.set(t)" /> }
                @if (t.status === 'Active') {
                  <p-button label="New version" [text]="true" size="small" (onClick)="newVersionOf.set(t)" />
                  @if (!t.isDefault) { <p-button label="Set default" [text]="true" size="small" (onClick)="setDefault(t)" /> }
                  <p-button label="Deactivate" [text]="true" size="small" severity="danger" (onClick)="deactivate(t)" />
                }
              }
            </td>
          </tr>
        }
        @if (templates().length === 0) { <tr><td colspan="8" class="muted">{{ loading() ? 'Loading…' : 'No invoice templates yet.' }}</td></tr> }
      </tbody>
    </table>

    <app-customer-invoice-template-dialog [customerId]="customerId()" [adding]="addOpen()" [newVersionOf]="newVersionOf()" (closed)="addOpen.set(false); newVersionOf.set(null)" (saved)="addOpen.set(false); newVersionOf.set(null); load()" />
    <app-customer-invoice-template-activate-dialog [template]="activating()" (closed)="activating.set(null)" (saved)="activating.set(null); load()" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class CustomerInvoiceTemplatesTabComponent {
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);

  readonly customerId = input.required<number>();
  protected readonly templates = signal<CustomerInvoiceTemplateModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.TEMPLATE.EDIT');

  protected readonly addOpen = signal(false);
  protected readonly newVersionOf = signal<CustomerInvoiceTemplateModel | null>(null);
  protected readonly activating = signal<CustomerInvoiceTemplateModel | null>(null);
  protected readonly words = words;

  constructor() {
    effect(() => { this.customerId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.invoiceTemplates(this.customerId(), true).subscribe({
      next: (r) => { this.loading.set(false); this.templates.set(r); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Invoice templates could not be loaded.')); },
    });
  }

  protected setDefault(t: CustomerInvoiceTemplateModel): void {
    this.api.setDefaultInvoiceTemplate(t.customerInvoiceTemplateId).subscribe({ next: () => this.load(), error: () => undefined });
  }

  protected deactivate(t: CustomerInvoiceTemplateModel): void {
    this.api.deactivateInvoiceTemplate(t.customerInvoiceTemplateId).subscribe({ next: () => this.load(), error: () => undefined });
  }
}
