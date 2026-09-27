import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { PaymentsApi } from '../../core/api.services';
import { NotifyService } from '../../core/notify.service';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CustomerReceiptModel, ReceiptSearchFilter } from '../../core/payment.models';
import { BankAccountPickerComponent } from '../../shared/bank-account-picker.component';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { ListQuery } from '../../shared/list-query';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { PAYMENT_METHODS, optionsOf, statusSeverity, words } from './payment-logic';

/** Receipts List (FSD §48.5's own unnumbered row: "All receipts") — see `ReceiptListItem`'s own backend doc
 * comment for the gap this closes. Reverse acts per allocation (§37 BR-P5: "Reverse (per allocation, reason)"),
 * so opening a row shows its own allocations in a small dialog rather than reversing the whole receipt at once. */
@Component({
  selector: 'app-receipts',
  standalone: true,
  imports: [FormsModule, ButtonModule, SelectModule, TagModule, DialogModule, BusinessDatePipe, DataGridComponent, GridCellDirective, TripCustomerPickerComponent, BankAccountPickerComponent, ReasonDialogComponent],
  template: `
    <div class="page">
      <div class="page-header"><div><h1>Receipts</h1><div class="sub">Every payment received, and which invoices it was applied to.</div></div></div>

      <div class="card">
        <div class="toolbar">
          <div class="field"><label>Customer</label><vms-trip-customer-picker [ngModel]="customerId()" (ngModelChange)="customerId.set($event)" /></div>
          <div class="field"><label>Method</label><p-select [ngModel]="paymentMethod()" (ngModelChange)="paymentMethod.set($event)" [options]="methodOptions" optionLabel="label" optionValue="value" [showClear]="true" placeholder="Any" [fluid]="true" appendTo="body" /></div>
          <div class="field"><label>Account</label><vms-bank-account-picker [ngModel]="bankCashAccountId()" (ngModelChange)="bankCashAccountId.set($event)" /></div>
          <div class="field"><label>Status</label><p-select [ngModel]="status()" (ngModelChange)="status.set($event)" [options]="statusOptions" optionLabel="label" optionValue="value" [showClear]="true" placeholder="Any" [fluid]="true" appendTo="body" /></div>
          <p-button label="Clear" [text]="true" size="small" (onClick)="clear()" />
        </div>

        <vms-data-grid label="Receipts" [columns]="columns" [load]="load" [filters]="filters()" [filtering]="isFiltering()" emptyMessage="No receipts found." noMatchMessage="No receipts match these filters.">
          <ng-template vmsCell="receiptNumber" let-r><span class="mono">{{ r.receiptNumber }}</span></ng-template>
          <ng-template vmsCell="receiptDate" let-r>{{ r.receiptDate | vmsDate }}</ng-template>
          <ng-template vmsCell="customerName" let-r>{{ r.customerName }}</ng-template>
          <ng-template vmsCell="paymentMethod" let-r>{{ words(r.paymentMethod) }}</ng-template>
          <ng-template vmsCell="instrumentNo" let-r>{{ r.instrumentNo }}</ng-template>
          <ng-template vmsCell="receiptAmount" let-r>{{ r.receiptAmount }} {{ r.currencyCode }}</ng-template>
          <ng-template vmsCell="invoiceNumbers" let-r>{{ r.invoiceNumbers.join(', ') || '—' }}</ng-template>
          <ng-template vmsCell="status" let-r><p-tag [value]="r.status" [severity]="severity(r.status)" /></ng-template>
          <ng-template vmsCell="actions" let-r><p-button label="View" [text]="true" size="small" (onClick)="view(r.customerReceiptId)" /></ng-template>
        </vms-data-grid>
      </div>
    </div>

    <p-dialog [visible]="!!detail()" (visibleChange)="!$event && detail.set(null)" [modal]="true" [style]="{ width: '520px' }" [header]="'Receipt ' + (detail()?.receiptNumber ?? '')">
      @if (detail(); as d) {
        <table>
          <thead><tr><th>Invoice</th><th>Amount</th><th>Balance after</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (a of d.allocations; track a.invoicePaymentId) {
              <tr>
                <td class="mono">{{ a.invoiceNumber }}</td><td>{{ a.amount }}</td><td>{{ a.invoiceBalance }}</td><td>{{ a.invoicePaymentStatus }}</td>
                <td>@if (canReverse() && d.status === 'Posted') { <p-button label="Reverse" [text]="true" size="small" severity="danger" (onClick)="reversing.set(a.invoicePaymentId)" /> }</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </p-dialog>

    <vms-reason-dialog [open]="!!reversing()" title="Reverse payment" confirmLabel="Reverse" [minLength]="10" [busy]="reverseBusy()" (closed)="reversing.set(null)" (confirmed)="doReverse($event)" />
  `,
  styles: [
    `
      .toolbar { display: flex; align-items: flex-end; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .field { display: flex; flex-direction: column; gap: .3rem; min-width: 11rem; } .field label { font-size: .8rem; color: var(--vms-muted); }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
    `,
  ],
})
export class ReceiptsComponent {
  private readonly api = inject(PaymentsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  protected readonly canReverse = computed(() => this.auth.hasPermission('TRP.PAYMENT.REVERSE'));

  readonly customerId = signal<number | null>(null);
  readonly paymentMethod = signal<string | null>(null);
  readonly bankCashAccountId = signal<number | null>(null);
  readonly status = signal<string | null>(null);
  protected readonly words = words;
  protected readonly severity = statusSeverity;
  protected readonly methodOptions = optionsOf(PAYMENT_METHODS);
  protected readonly statusOptions = [{ label: 'Posted', value: 'Posted' }, { label: 'Reversed', value: 'Reversed' }];

  readonly detail = signal<CustomerReceiptModel | null>(null);
  readonly reversing = signal<number | null>(null);
  readonly reverseBusy = signal(false);

  readonly columns: GridColumn[] = [
    { key: 'receiptNumber', header: 'Receipt no.' },
    { key: 'receiptDate', header: 'Date', width: '7rem' },
    { key: 'customerName', header: 'Customer' },
    { key: 'paymentMethod', header: 'Method' },
    { key: 'instrumentNo', header: 'Instrument' },
    { key: 'receiptAmount', header: 'Amount' },
    { key: 'invoiceNumbers', header: 'Invoices' },
    { key: 'status', header: 'Status', width: '8rem' },
    { key: 'actions', header: '' },
  ];

  readonly filters = computed<Record<string, unknown>>(() => ({
    customerId: this.customerId(), paymentMethod: this.paymentMethod(), bankCashAccountId: this.bankCashAccountId(), status: this.status(),
  }));
  readonly isFiltering = computed(() => Object.values(this.filters()).some((v) => v !== null && v !== undefined));

  readonly load = (query: ListQuery) => {
    const f = query.filters as ReturnType<typeof this.filters>;
    const filter: ReceiptSearchFilter = {
      customerId: f['customerId'] as number | null, paymentMethod: f['paymentMethod'] as string | null, bankCashAccountId: f['bankCashAccountId'] as number | null, status: f['status'] as string | null,
    };
    return this.api.searchReceipts(filter, query.page, query.pageSize);
  };

  clear(): void {
    this.customerId.set(null); this.paymentMethod.set(null); this.bankCashAccountId.set(null); this.status.set(null);
  }

  protected view(receiptId: number): void {
    this.api.getReceipt(receiptId).subscribe({ next: (d) => this.detail.set(d), error: (err) => this.notify.error(err) });
  }

  protected doReverse(reason: string): void {
    const id = this.reversing();
    const receiptId = this.detail()?.customerReceiptId;
    if (!id || !receiptId) return;
    this.reverseBusy.set(true);
    this.api.reversePayment(id, { reason }).subscribe({
      next: () => { this.reverseBusy.set(false); this.reversing.set(null); this.notify.success('Payment reversed'); this.view(receiptId); },
      error: (err) => { this.reverseBusy.set(false); this.notify.error(err); },
    });
  }
}
