import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { CustomersApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CustomerBillingAddressModel } from '../../core/customer.models';
import { CustomerBillingAddressDialogComponent } from './customer-billing-address-dialog.component';

/** Billing Addresses (FSD §12, screen 4): where invoices are addressed to; exactly one may be Default. */
@Component({
  selector: 'app-customer-billing-addresses-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, BusinessDatePipe, CustomerBillingAddressDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Add address" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Name</th><th>Address</th><th>NTN</th><th>Default</th><th>Effective</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (a of addresses(); track a.customerBillingAddressId) {
          <tr>
            <td>{{ a.addressName }}</td><td>{{ a.addressLine1 }}@if (a.addressLine2) { , {{ a.addressLine2 }} }</td><td>{{ a.ntn || '—' }}</td>
            <td>@if (a.isDefault) { <p-tag value="Default" severity="info" /> }</td>
            <td class="nowrap">{{ a.effectiveFrom | vmsDate }}@if (a.effectiveTo) { – {{ a.effectiveTo | vmsDate }} }</td>
            <td><p-tag [value]="a.status" [severity]="a.status === 'Active' ? 'success' : 'danger'" /></td>
            <td class="row-actions">
              @if (canEdit()) {
                <p-button label="Edit" [text]="true" size="small" (onClick)="editing.set(a)" />
                @if (!a.isDefault && a.status === 'Active') { <p-button label="Set default" [text]="true" size="small" (onClick)="setDefault(a)" /> }
                @if (a.status === 'Active') { <p-button label="Deactivate" [text]="true" size="small" severity="danger" (onClick)="deactivate(a)" /> }
                @else { <p-button label="Activate" [text]="true" size="small" (onClick)="activate(a)" /> }
              }
            </td>
          </tr>
        }
        @if (addresses().length === 0) { <tr><td colspan="7" class="muted">{{ loading() ? 'Loading…' : 'No billing addresses yet.' }}</td></tr> }
      </tbody>
    </table>

    <app-customer-billing-address-dialog [customerId]="customerId()" [adding]="addOpen()" [address]="editing()" (closed)="addOpen.set(false); editing.set(null)" (saved)="addOpen.set(false); editing.set(null); load()" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class CustomerBillingAddressesTabComponent {
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);

  readonly customerId = input.required<number>();
  protected readonly addresses = signal<CustomerBillingAddressModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.CUSTOMER.EDIT');

  protected readonly addOpen = signal(false);
  protected readonly editing = signal<CustomerBillingAddressModel | null>(null);

  constructor() {
    effect(() => { this.customerId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.billingAddresses(this.customerId(), true).subscribe({
      next: (r) => { this.loading.set(false); this.addresses.set(r); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Billing addresses could not be loaded.')); },
    });
  }

  protected setDefault(a: CustomerBillingAddressModel): void {
    this.api.setDefaultBillingAddress(a.customerBillingAddressId).subscribe({ next: () => this.load(), error: () => undefined });
  }

  protected deactivate(a: CustomerBillingAddressModel): void {
    this.api.deactivateBillingAddress(a.customerBillingAddressId).subscribe({ next: () => this.load(), error: () => undefined });
  }

  protected activate(a: CustomerBillingAddressModel): void {
    this.api.activateBillingAddress(a.customerBillingAddressId).subscribe({ next: () => this.load(), error: () => undefined });
  }
}
