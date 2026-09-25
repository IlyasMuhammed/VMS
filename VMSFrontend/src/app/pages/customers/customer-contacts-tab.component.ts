import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { CustomersApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { ConfirmService } from '../../shared/confirm.service';
import { CustomerContactModel } from '../../core/customer.models';
import { CustomerContactDialogComponent } from './customer-contact-dialog.component';

/** Customer Contacts (FSD §11, screen 3): people to call about this customer, one may be the main contact. */
@Component({
  selector: 'app-customer-contacts-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, CustomerContactDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Add contact" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Name</th><th>Designation</th><th>Mobile</th><th>Email</th><th>Purpose</th><th>Main</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (c of contacts(); track c.customerContactId) {
          <tr>
            <td>{{ c.name }}</td><td>{{ c.designation || '—' }}</td><td>{{ c.mobile1 }}</td><td>{{ c.email || '—' }}</td>
            <td>{{ c.purpose.join(', ') || '—' }}</td><td>@if (c.isPrimary) { <p-tag value="Main" severity="info" /> }</td>
            <td><p-tag [value]="c.status" [severity]="c.status === 'Active' ? 'success' : 'danger'" /></td>
            <td class="row-actions">
              @if (canEdit()) {
                <p-button label="Edit" [text]="true" size="small" (onClick)="editing.set(c)" />
                @if (c.status === 'Active') { <p-button label="Deactivate" [text]="true" size="small" severity="danger" (onClick)="deactivate(c)" /> }
                @else { <p-button label="Activate" [text]="true" size="small" (onClick)="activate(c)" /> }
              }
            </td>
          </tr>
        }
        @if (contacts().length === 0) { <tr><td colspan="8" class="muted">{{ loading() ? 'Loading…' : 'No contacts yet.' }}</td></tr> }
      </tbody>
    </table>

    <app-customer-contact-dialog [customerId]="customerId()" [adding]="addOpen()" [contact]="editing()" (closed)="addOpen.set(false); editing.set(null)" (saved)="addOpen.set(false); editing.set(null); load()" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class CustomerContactsTabComponent {
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);

  readonly customerId = input.required<number>();
  protected readonly contacts = signal<CustomerContactModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.CUSTOMER.EDIT');

  protected readonly addOpen = signal(false);
  protected readonly editing = signal<CustomerContactModel | null>(null);

  constructor() {
    effect(() => { this.customerId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.contacts(this.customerId(), true).subscribe({
      next: (r) => { this.loading.set(false); this.contacts.set(r); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Contacts could not be loaded.')); },
    });
  }

  protected deactivate(c: CustomerContactModel): void {
    void this.confirm.ask({ title: 'Deactivate contact', message: `Deactivate contact ${c.name}?`, confirmLabel: 'Deactivate', cancelLabel: 'Keep as it is', icon: 'pi pi-user-minus' }).then((go) => {
      if (!go) return;
      this.api.deactivateContact(c.customerContactId).subscribe({ next: () => this.load(), error: () => undefined });
    });
  }

  protected activate(c: CustomerContactModel): void {
    this.api.activateContact(c.customerContactId).subscribe({ next: () => this.load(), error: () => undefined });
  }
}
