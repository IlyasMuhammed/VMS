import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TabsModule } from 'primeng/tabs';
import { map } from 'rxjs';
import { CustomersApi } from '../../core/api.services';
import { apiErrors, errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { CustomerBalanceModel, CustomerModel } from '../../core/customer.models';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';
import { ConfirmService } from '../../shared/confirm.service';
import { applyServerErrors } from '../../shared/server-errors';
import { CustomerStatusComponent } from './customer-bits.component';
import { CustomerStatusDialogComponent } from './customer-dialogs.component';
import { buildCustomerForm, patchCustomerForm, toSaveRequest } from './customer-form';
import { CustomerGeneralTabComponent } from './customer-general-tab.component';
import { CustomerContactsTabComponent } from './customer-contacts-tab.component';
import { CustomerBillingAddressesTabComponent } from './customer-billing-addresses-tab.component';
import { CustomerBillingConfigurationTabComponent } from './customer-billing-configuration-tab.component';
import { CustomerTaxRulesTabComponent } from './customer-tax-rules-tab.component';
import { CustomerInvoiceTemplatesTabComponent } from './customer-invoice-templates-tab.component';
import { CustomerHistoryTabComponent } from './customer-history-tab.component';
import { statusMoves } from './customer-logic';

type CustomerTab = 'general' | 'contacts' | 'addresses' | 'billing' | 'tax' | 'templates' | 'history';

/**
 * The Customer screen, for a new customer and for a saved one (FSD §10, §48.2 screens 1-7): a header with the
 * code, name, status and per-currency balance; tabs for the General details and every child record; one Save
 * for the General tab, since every child record is saved through its own dedicated endpoint the moment it
 * exists (mirrors `PartnerFormComponent`, minus the duplicate watcher — §10's own note says a duplicate name is
 * a warning-free, non-blocking concern here, so there is nothing to watch for).
 *
 * Two tabs the FSD also lists for this screen are deliberately not here yet: Trip Configurations (belongs to
 * the Setup cluster, screens 8-11+17, not built in this pass) and the customer's Ledger/Finance statement
 * (§48.5, its own future screen). There is also no Documents tab: `DocumentOwnerType` has no `Customer` member
 * — this FSD's own evidence concept (§13, POD & evidence) is a Trip/Invoice affair, not the shared BP/Vehicle
 * document register, so there is nothing to attach here.
 */
@Component({
  selector: 'app-customer-form',
  standalone: true,
  imports: [
    RouterLink, ButtonModule, TabsModule, CustomerStatusComponent, CustomerStatusDialogComponent, CustomerGeneralTabComponent,
    CustomerContactsTabComponent, CustomerBillingAddressesTabComponent, CustomerBillingConfigurationTabComponent, CustomerTaxRulesTabComponent,
    CustomerInvoiceTemplatesTabComponent, CustomerHistoryTabComponent,
  ],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/customers">Customers</a> <span aria-hidden="true">/</span> <span>{{ customer()?.customerCode ?? 'New customer' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (loading()) {
        <p class="muted">Loading…</p>
      } @else {
        <header class="head">
          <div class="title">
            <h1>{{ customer()?.customerName || (isNew() ? 'New customer' : '') }}</h1>
            @if (customer(); as c) {
              <div class="meta">
                <span class="mono">{{ c.customerCode }}</span>
                <vms-customer-status [status]="c.status" />
                @if (c.inactiveReason) { <span class="muted">— {{ c.inactiveReason }}</span> }
                @for (b of balances(); track b.currencyCode) { <span class="chip chip--info">{{ b.currencyCode }} {{ b.balanceAmount }}</span> }
              </div>
            }
          </div>
          @if (customer(); as c) {
            <div class="actions-bar">
              @if (canStatus()) { @for (m of moves(); track m) { <p-button [label]="m === 'Active' ? (c.status === 'Draft' ? 'Activate' : 'Reactivate') : 'Deactivate'" icon="pi pi-flag" severity="secondary" [outlined]="true" size="small" [disabled]="dirty()" [title]="dirty() ? 'Save your changes first' : ''" (onClick)="statusMove.set(m); statusOpen.set(true)" /> } }
              @if (canDelete()) { <p-button label="Delete" icon="pi pi-trash" severity="danger" [outlined]="true" size="small" (onClick)="deleteDraft()" /> }
            </div>
          }
        </header>

        @if (conflict(); as message) {
          <div class="alert warning conflict" role="alert">
            <span>{{ message }}</span>
            <p-button label="Reload their version" size="small" (onClick)="reload()" />
          </div>
        }
        @if (problems().length > 0) {
          <div class="alert error" role="alert"><strong>This could not be saved.</strong><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div>
        }

        <div class="card body">
          <p-tabs [value]="activeTab()" (valueChange)="activeTab.set($any($event))" [scrollable]="true">
            <p-tablist>
              <p-tab value="general">General</p-tab>
              @if (!isNew()) { <p-tab value="contacts">Contacts</p-tab> }
              @if (!isNew()) { <p-tab value="addresses">Billing addresses</p-tab> }
              @if (!isNew()) { <p-tab value="billing">Billing configuration</p-tab> }
              @if (!isNew()) { <p-tab value="tax">Tax / deductions</p-tab> }
              @if (!isNew()) { <p-tab value="templates">Invoice templates</p-tab> }
              @if (!isNew()) { <p-tab value="history">History</p-tab> }
            </p-tablist>
            <p-tabpanels>
              <p-tabpanel value="general"><app-customer-general-tab [form]="form" [mode]="isNew() ? 'create' : 'edit'" /></p-tabpanel>
              @if (!isNew()) { <p-tabpanel value="contacts">@if (activeTab() === 'contacts') { <app-customer-contacts-tab [customerId]="customer()!.customerId" /> }</p-tabpanel> }
              @if (!isNew()) { <p-tabpanel value="addresses">@if (activeTab() === 'addresses') { <app-customer-billing-addresses-tab [customerId]="customer()!.customerId" /> }</p-tabpanel> }
              @if (!isNew()) { <p-tabpanel value="billing">@if (activeTab() === 'billing') { <app-customer-billing-configuration-tab [customerId]="customer()!.customerId" /> }</p-tabpanel> }
              @if (!isNew()) { <p-tabpanel value="tax">@if (activeTab() === 'tax') { <app-customer-tax-rules-tab [customerId]="customer()!.customerId" /> }</p-tabpanel> }
              @if (!isNew()) { <p-tabpanel value="templates">@if (activeTab() === 'templates') { <app-customer-invoice-templates-tab [customerId]="customer()!.customerId" /> }</p-tabpanel> }
              @if (!isNew()) { <p-tabpanel value="history">@if (activeTab() === 'history') { <app-customer-history-tab [customerId]="customer()!.customerId" [version]="customer()!.rowVersion" /> }</p-tabpanel> }
            </p-tabpanels>
          </p-tabs>
        </div>

        @if (editable()) {
          <div class="savebar">
            <span class="muted">{{ dirty() ? 'You have unsaved changes.' : (isNew() ? 'Fields marked * are required.' : 'All changes are saved.') }}</span>
            <span class="buttons">
              <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="cancel()" />
              <p-button [label]="isNew() ? 'Save' : 'Save changes'" icon="pi pi-check" [loading]="busy()" [disabled]="!isNew() && !dirty()" (onClick)="save()" />
            </span>
          </div>
        }
      }
    </div>

    <app-customer-status-dialog [customer]="statusOpen() ? customer() : null" [move]="statusMove()" (closed)="statusOpen.set(false)" (changed)="onServerChanged($event)" />
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
      .title h1 { font-size: 1.5rem; font-weight: 600; }
      .meta { display: flex; flex-wrap: wrap; align-items: center; gap: .6rem; margin-top: .35rem; }
      .actions-bar { display: flex; flex-wrap: wrap; gap: .5rem; }
      .conflict { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: 1rem; }
      .alert { margin-bottom: 1rem; } .alert ul { margin: .35rem 0 0; padding-left: 1.25rem; }
      .body { padding-bottom: 1.5rem; }
      .savebar {
        position: sticky; bottom: 0; z-index: 5; display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap;
        margin: 1rem -2rem -1.5rem; padding: .85rem 2rem; background: var(--vms-surface); border-top: 1px solid var(--vms-border);
      }
      .buttons { display: inline-flex; gap: .5rem; }
    `,
  ],
})
export class CustomerFormComponent implements HasUnsavedChanges {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = buildCustomerForm(this.fb);
  readonly customer = signal<CustomerModel | null>(null);
  readonly balances = signal<CustomerBalanceModel[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly conflict = signal<string | null>(null);
  readonly activeTab = signal<CustomerTab>('general');
  readonly statusOpen = signal(false);
  readonly statusMove = signal<'Active' | 'Inactive'>('Active');

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => (p.get('id') ? Number(p.get('id')) : null))), { initialValue: null as number | null });
  readonly isNew = computed(() => this.id() === null);
  // `statusMoves` never actually offers 'Draft' as a destination — narrowed here so the template can pass it
  // straight to `statusMove`, which only ever holds the two moves this screen's dialog can perform.
  readonly moves = computed(() => statusMoves(this.customer()?.status ?? '') as ('Active' | 'Inactive')[]);

  readonly editable = computed(() => this.auth.hasPermission('TRP.CUSTOMER.EDIT'));
  readonly canStatus = computed(() => this.editable() && this.moves().length > 0);
  readonly canDelete = computed(() => this.editable() && !this.isNew() && this.customer()?.status === 'Draft');

  private readonly tick = signal(0);
  readonly dirty = computed(() => (this.tick(), this.form.dirty));

  constructor() {
    this.form.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.tick.update((n) => n + 1));
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty && !this.busy();
  }

  @HostListener('window:beforeunload', ['$event'])
  protected warnOnUnload(event: BeforeUnloadEvent): void {
    if (this.form.dirty) event.preventDefault();
  }

  load(): void {
    this.problems.set([]);
    this.conflict.set(null);
    const id = this.id();

    if (id === null) {
      this.customer.set(null);
      this.balances.set([]);
      this.loadError.set(null);
      this.startNew();
      return;
    }

    this.loading.set(true);
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (c) => { this.loading.set(false); this.show(c); },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This customer was not found. It may belong to another company, or the link is wrong.' : errorMessage(err, 'The customer could not be loaded.'));
      },
    });
    this.api.balance(id).subscribe({ next: (b) => this.balances.set(b), error: () => undefined });
  }

  private show(c: CustomerModel): void {
    this.customer.set(c);
    patchCustomerForm(this.form, c);
    if (this.editable()) this.form.enable({ emitEvent: false });
    else this.form.disable({ emitEvent: false });
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.tick.update((n) => n + 1);
  }

  private startNew(): void {
    this.form.enable({ emitEvent: false });
    this.form.reset({ customerCode: '', customerName: '', shortName: '', addressLine1: '', addressLine2: '', countryId: null, provinceState: '', cityId: null, postalCode: '', ntn: '', strn: '', otherRegistrationNo: '', currencyCode: 'PKR', paymentTermsDays: 30, creditLimit: null, remarks: '' }, { emitEvent: false });
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.activeTab.set('general');
    this.tick.update((n) => n + 1);
  }

  reload(): void {
    this.form.markAsPristine();
    this.load();
  }

  onServerChanged(c: CustomerModel): void {
    this.statusOpen.set(false);
    this.show(c);
  }

  cancel(): void {
    void this.router.navigate(['/customers']);
  }

  save(): void {
    if (this.busy()) return;
    this.problems.set([]);
    this.conflict.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.tick.update((n) => n + 1);
      this.notify.warn('Please correct the highlighted fields.', 'Not saved');
      return;
    }

    this.busy.set(true);
    const current = this.customer();
    const request$ = current ? this.api.update(current.customerId, toSaveRequest(this.form, current.rowVersion)) : this.api.create(toSaveRequest(this.form, null));

    request$.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.form.markAsPristine();
        if (!current) return this.afterCreate(saved);
        this.notify.success('Changes saved');
        this.show(saved);
      },
      error: (err: unknown) => this.refused(err),
    });
  }

  private afterCreate(saved: CustomerModel): void {
    this.notify.success(`${saved.customerName} saved as ${saved.customerCode}`);
    void this.router.navigate(['/customers', saved.customerId], { replaceUrl: true });
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    if (err instanceof HttpErrorResponse && err.status === 409) {
      this.conflict.set(errorMessage(err, 'This customer was changed by someone else while you were editing.'));
      return;
    }
    const found = apiErrors(err);
    if (found.length === 0) { this.notify.error(err); return; }
    this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
    this.tick.update((n) => n + 1);
  }

  protected deleteDraft(): void {
    const c = this.customer();
    if (!c) return;
    void this.confirm.ask({ title: 'Delete customer', message: `Delete draft customer ${c.customerName}? This cannot be undone.`, confirmLabel: 'Delete', cancelLabel: 'Keep it', icon: 'pi pi-trash' }).then((go) => {
      if (!go) return;
      this.api.delete(c.customerId, { reason: null, rowVersion: c.rowVersion }).subscribe({
        next: () => { this.notify.success('Draft customer deleted'); void this.router.navigate(['/customers']); },
        error: (err) => this.notify.error(err),
      });
    });
  }
}
