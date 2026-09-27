import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { InvoicesApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { InvoiceListItem, InvoiceSearchFilter } from '../../core/invoice.models';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { ListQuery } from '../../shared/list-query';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { paymentStatusSeverity, statusSeverity, words } from './invoice-logic';

/** Invoice List (FSD §48.5 — not one of the FSD's own numbered screens; see `InvoiceListItem`'s own backend doc
 * comment for why it exists). Client-side text search (invoice no./customer) plus customer/status/payment-status
 * filters, matching the same "bespoke filter row + `vms-data-grid` for paging" shape as the Trip Desk. */
@Component({
  selector: 'app-invoices',
  standalone: true,
  imports: [FormsModule, ButtonModule, InputTextModule, SelectModule, TagModule, BusinessDatePipe, DataGridComponent, GridCellDirective, TripCustomerPickerComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Invoices</h1><div class="sub">Every invoice generated, and where it stands.</div></div>
        <div class="header-actions">
          @if (canGenerate()) { <p-button label="Generate invoice" icon="pi pi-plus" (onClick)="generate()" /> }
        </div>
      </div>

      <div class="card">
        <div class="toolbar">
          <input pInputText [ngModel]="searchText()" (ngModelChange)="searchText.set($event)" placeholder="Search invoice no. or customer" style="max-width: 20rem" />
          <div class="field"><label>Customer</label><vms-trip-customer-picker [ngModel]="customerId()" (ngModelChange)="customerId.set($event)" /></div>
          <div class="field"><label>Status</label><p-select [ngModel]="status()" (ngModelChange)="status.set($event)" [options]="statusOptions" optionLabel="label" optionValue="value" [showClear]="true" placeholder="Any" [fluid]="true" appendTo="body" /></div>
          <div class="field"><label>Payment</label><p-select [ngModel]="paymentStatus()" (ngModelChange)="paymentStatus.set($event)" [options]="paymentStatusOptions" optionLabel="label" optionValue="value" [showClear]="true" placeholder="Any" [fluid]="true" appendTo="body" /></div>
          <p-button label="Clear" [text]="true" size="small" (onClick)="clear()" />
        </div>

        <vms-data-grid label="Invoices" [columns]="columns" [load]="load" [filters]="filters()" [filtering]="isFiltering()"
                       emptyMessage="No invoices found." noMatchMessage="No invoices match these filters.">
          <ng-template vmsCell="invoiceNumber" let-i><a class="mono" [href]="'/invoices/' + i.invoiceId" (click)="open($event, i)">{{ i.invoiceNumber }}</a></ng-template>
          <ng-template vmsCell="customerName" let-i>{{ i.customerName }}</ng-template>
          <ng-template vmsCell="period" let-i>{{ i.periodFrom | vmsDate }} – {{ i.periodTo | vmsDate }}</ng-template>
          <ng-template vmsCell="invoiceDate" let-i>{{ i.invoiceDate | vmsDate }}</ng-template>
          <ng-template vmsCell="netAmount" let-i>{{ i.netAmount }} {{ i.currencyCode }}</ng-template>
          <ng-template vmsCell="balanceAmount" let-i>{{ i.balanceAmount }} {{ i.currencyCode }}</ng-template>
          <ng-template vmsCell="status" let-i><p-tag [value]="words(i.status)" [severity]="severity(i.status)" /></ng-template>
          <ng-template vmsCell="paymentStatus" let-i><p-tag [value]="words(i.paymentStatus)" [severity]="paySeverity(i.paymentStatus)" /></ng-template>
        </vms-data-grid>
      </div>
    </div>
  `,
  styles: [
    `
      .header-actions { display: flex; gap: .5rem; }
      .toolbar { display: flex; align-items: flex-end; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .field { display: flex; flex-direction: column; gap: .3rem; min-width: 11rem; } .field label { font-size: .8rem; color: var(--vms-muted); }
      a.mono { text-decoration: none; } a.mono:hover { text-decoration: underline; }
    `,
  ],
})
export class InvoicesComponent {
  private readonly api = inject(InvoicesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly canGenerate = computed(() => this.auth.hasPermission('TRP.INVOICE.GENERATE'));

  readonly searchText = signal('');
  readonly customerId = signal<number | null>(null);
  readonly status = signal<string | null>(null);
  readonly paymentStatus = signal<string | null>(null);
  protected readonly words = words;
  protected readonly severity = statusSeverity;
  protected readonly paySeverity = paymentStatusSeverity;

  protected readonly statusOptions = [
    { label: 'Draft', value: 'Draft' }, { label: 'Generated', value: 'Generated' }, { label: 'Submitted', value: 'Submitted' },
    { label: 'Inactive', value: 'Inactive' }, { label: 'Cancelled', value: 'Cancelled' },
  ];
  protected readonly paymentStatusOptions = [{ label: 'Unpaid', value: 'Unpaid' }, { label: 'Partially paid', value: 'PartiallyPaid' }, { label: 'Paid', value: 'Paid' }];

  readonly columns: GridColumn[] = [
    { key: 'invoiceNumber', header: 'Invoice no.' },
    { key: 'customerName', header: 'Customer' },
    { key: 'period', header: 'Period' },
    { key: 'invoiceDate', header: 'Date', width: '7rem' },
    { key: 'netAmount', header: 'Net' },
    { key: 'balanceAmount', header: 'Balance' },
    { key: 'status', header: 'Status', width: '8rem' },
    { key: 'paymentStatus', header: 'Payment', width: '9rem' },
  ];

  readonly filters = computed<Record<string, unknown>>(() => ({
    search: this.searchText(), customerId: this.customerId(), status: this.status(), paymentStatus: this.paymentStatus(),
  }));
  readonly isFiltering = computed(() => Object.values(this.filters()).some((v) => v !== null && v !== undefined && v !== ''));

  readonly load = (query: ListQuery) => {
    const f = query.filters as ReturnType<typeof this.filters>;
    const filter: InvoiceSearchFilter = {
      search: (f['search'] as string) || null, customerId: f['customerId'] as number | null, status: f['status'] as string | null, paymentStatus: f['paymentStatus'] as string | null,
    };
    return this.api.search(filter, query.page, query.pageSize);
  };

  clear(): void {
    this.searchText.set(''); this.customerId.set(null); this.status.set(null); this.paymentStatus.set(null);
  }

  generate(): void {
    void this.router.navigate(['/invoices/generate']);
  }

  open(event: MouseEvent | null, invoice: InvoiceListItem): void {
    if (event && (event.ctrlKey || event.metaKey || event.shiftKey || event.button === 1)) return;
    event?.preventDefault();
    void this.router.navigate(['/invoices', invoice.invoiceId]);
  }
}
