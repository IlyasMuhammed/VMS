import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { map } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { InvoicesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { InvoiceModel } from '../../core/invoice.models';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { CarryForwardDialogComponent } from '../payments/carry-forward-dialog.component';
import { RecordPaymentDialogComponent } from '../payments/record-payment-dialog.component';
import { RefundDialogComponent } from '../payments/refund-dialog.component';
import { SettlementDialogComponent } from '../payments/settlement-dialog.component';
import { isCredit } from '../payments/payment-logic';
import { InvoiceEvidenceTabComponent } from './invoice-evidence-tab.component';
import { InvoiceHistoryTabComponent } from './invoice-history-tab.component';
import { InvoiceLedgerTabComponent } from './invoice-ledger-tab.component';
import { InvoiceSubmitDialogComponent } from './invoice-submit-dialog.component';
import { InvoiceVersionsTabComponent } from './invoice-versions-tab.component';
import { paymentStatusSeverity, statusSeverity, words } from './invoice-logic';

type Tab = 'lines' | 'adjustments' | 'deductions' | 'evidence' | 'ledger' | 'versions' | 'history';

/**
 * Invoice Detail (FSD §32-§41, §48.5 screen 21): header card, Submit/Cancel/Record Payment/Write-off-Discount/
 * Carry Forward/Refund/Regenerate, and the Lines/Adjustments/Deductions/Evidence/Ledger/Versions/History tabs —
 * each of Lines/Adjustments/Deductions reads straight off the already-loaded `InvoiceModel` (no separate fetch,
 * since `GET /api/invoices/{id}` returns all three inline); Evidence/Ledger/Versions each fetch their own.
 * Carry Forward/Refund only show on a credit invoice (a negative balance, §40) — there is nothing to carry
 * forward or refund otherwise. Regenerate is its own page (`invoice-regeneration.component.ts`), not a dialog.
 *
 * One of the FSD's own ten tabs is **not built in this pass**: Transfers (needs its own transfer-preview work)
 * — left for its own follow-up cluster, the same "large task, one slice at a time" pacing this whole UI build
 * has used throughout. Documents (upload) is also not built.
 */
@Component({
  selector: 'app-invoice-detail',
  standalone: true,
  imports: [
    RouterLink, ButtonModule, TabsModule, TagModule, BusinessDatePipe, ReasonDialogComponent, InvoiceSubmitDialogComponent, InvoiceHistoryTabComponent,
    InvoiceEvidenceTabComponent, InvoiceLedgerTabComponent, InvoiceVersionsTabComponent, RecordPaymentDialogComponent, SettlementDialogComponent, CarryForwardDialogComponent, RefundDialogComponent,
  ],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/invoices">Invoices</a> <span aria-hidden="true">/</span> <span>{{ invoice()?.invoiceNumber ?? '…' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (invoice()) {
        @let inv = invoice()!;
        <header class="head">
          <div>
            <h1>{{ inv.invoiceNumber }} <span class="muted">v{{ inv.version }}</span></h1>
            <div class="meta">
              <p-tag [value]="words(inv.status)" [severity]="severity(inv.status)" /> <p-tag [value]="words(inv.paymentStatus)" [severity]="paySeverity(inv.paymentStatus)" />
              @if (!inv.isActive) { <span class="chip chip--danger">Inactive</span> }
              <span class="muted">{{ inv.customerName }} ({{ inv.customerCode }}) · {{ inv.periodFrom | vmsDate }} – {{ inv.periodTo | vmsDate }}</span>
            </div>
          </div>
          <div class="actions-bar">
            @if (canSubmit() && inv.status === 'Generated') { <p-button label="Submit" icon="pi pi-check" size="small" (onClick)="submitOpen.set(true)" /> }
            @if (canPay() && inv.status === 'Submitted') { <p-button label="Record Payment" severity="secondary" [outlined]="true" size="small" (onClick)="paymentOpen.set(true)" /> }
            @if (canSettle() && inv.status === 'Submitted') { <p-button label="Write-off / Discount" severity="secondary" [outlined]="true" size="small" (onClick)="settlementOpen.set(true)" /> }
            @if (isCreditInvoice() && canCarryForward()) { <p-button label="Carry Forward" severity="secondary" [outlined]="true" size="small" (onClick)="carryForwardOpen.set(true)" /> }
            @if (isCreditInvoice() && canRefund()) { <p-button label="Refund" severity="secondary" [outlined]="true" size="small" (onClick)="refundOpen.set(true)" /> }
            @if (canRegenerate() && canBeRegenerated()) { <p-button label="Regenerate" severity="secondary" [outlined]="true" size="small" [routerLink]="['/invoices', inv.invoiceId, 'regenerate']" /> }
            @if (canCancel() && inv.status !== 'Cancelled') { <p-button label="Cancel invoice" severity="danger" [outlined]="true" size="small" (onClick)="cancelOpen.set(true)" /> }
          </div>
        </header>

        @if (!inv.isActive) { <div class="alert info">Replaced by a later version; this copy is kept for its own history.</div> }
        @for (w of inv.warnings; track w) { <div class="alert warning">{{ w }}</div> }
        @if (inv.cancelReason) { <div class="alert warning">Cancelled: {{ inv.cancelReason }}</div> }

        <div class="totals">
          <div><dt>Trip amount</dt><dd>{{ inv.totalTripAmount }} {{ inv.currencyCode }}</dd></div>
          <div><dt>Adjustments</dt><dd>{{ inv.totalAdjustment }} {{ inv.currencyCode }}</dd></div>
          <div><dt>Gross</dt><dd>{{ inv.grossAmount }} {{ inv.currencyCode }}</dd></div>
          <div><dt>Deductions</dt><dd>{{ inv.totalDeduction }} {{ inv.currencyCode }}</dd></div>
          <div class="net"><dt>Net</dt><dd>{{ inv.netAmount }} {{ inv.currencyCode }}</dd></div>
          <div><dt>Paid</dt><dd>{{ inv.paidAmount }} {{ inv.currencyCode }}</dd></div>
          <div class="balance"><dt>Balance</dt><dd [class.negative]="inv.balanceAmount < 0">{{ inv.balanceAmount }} {{ inv.currencyCode }}</dd></div>
        </div>

        <div class="card body">
          <p-tabs [value]="tab()" (valueChange)="tab.set($any($event))" [scrollable]="true">
            <p-tablist>
              <p-tab value="lines">Lines</p-tab>
              <p-tab value="adjustments">Adjustments</p-tab>
              <p-tab value="deductions">Deductions</p-tab>
              <p-tab value="evidence">Evidence</p-tab>
              <p-tab value="ledger">Ledger</p-tab>
              <p-tab value="versions">Versions</p-tab>
              <p-tab value="history">History</p-tab>
            </p-tablist>
            <p-tabpanels>
              <p-tabpanel value="lines">
                <table>
                  <thead><tr><th>#</th><th>Trip</th><th>Date</th><th>Route</th><th>Vehicle</th><th>Driver</th><th>Description</th><th>Amount</th></tr></thead>
                  <tbody>
                    @for (l of inv.lines; track l.invoiceLineId) {
                      <tr><td>{{ l.lineNo }}</td><td class="mono">{{ l.tripNumber || '—' }}</td><td>{{ l.tripDate ? (l.tripDate | vmsDate) : '—' }}</td><td>{{ l.routeLabel || '—' }}</td>
                        <td>{{ l.vehicleRegNo || '—' }}</td><td>{{ l.driverName || '—' }}</td><td>{{ l.description }}</td><td>{{ l.amount }}</td></tr>
                    }
                    @if (inv.lines.length === 0) { <tr><td colspan="8" class="muted">No lines.</td></tr> }
                  </tbody>
                </table>
              </p-tabpanel>
              <p-tabpanel value="adjustments">
                <table>
                  <thead><tr><th>Month</th><th>Amount</th><th>Note</th><th>Reference invoice</th></tr></thead>
                  <tbody>
                    @for (a of inv.adjustments; track a.invoiceAdjustmentId) { <tr><td>{{ a.adjustmentMonth }}</td><td>{{ a.adjustmentAmount }}</td><td>{{ a.adjustmentNote }}</td><td>{{ a.referenceInvoiceNo || '—' }}</td></tr> }
                    @if (inv.adjustments.length === 0) { <tr><td colspan="4" class="muted">No adjustments.</td></tr> }
                  </tbody>
                </table>
              </p-tabpanel>
              <p-tabpanel value="deductions">
                <table>
                  <thead><tr><th>Tax</th><th>Code</th><th>Applicable</th><th>Amount</th></tr></thead>
                  <tbody>
                    @for (t of inv.taxLines; track t.invoiceTaxLineId) {
                      <tr><td>{{ t.taxName }}</td><td class="mono">{{ t.taxCode }}</td><td>{{ t.applicable ? 'Yes' : 'No' }}</td><td>{{ t.amount }}</td></tr>
                    }
                    @if (inv.taxLines.length === 0) { <tr><td colspan="4" class="muted">No deductions.</td></tr> }
                  </tbody>
                </table>
              </p-tabpanel>
              <p-tabpanel value="evidence">@if (tab() === 'evidence') { <app-invoice-evidence-tab [invoiceId]="inv.invoiceId" /> }</p-tabpanel>
              <p-tabpanel value="ledger">@if (tab() === 'ledger') { <app-invoice-ledger-tab [invoiceId]="inv.invoiceId" /> }</p-tabpanel>
              <p-tabpanel value="versions">@if (tab() === 'versions') { <app-invoice-versions-tab [invoiceId]="inv.invoiceId" /> }</p-tabpanel>
              <p-tabpanel value="history">@if (tab() === 'history') { <app-invoice-history-tab [invoiceId]="inv.invoiceId" [version]="inv.rowVersion" /> }</p-tabpanel>
            </p-tabpanels>
          </p-tabs>
        </div>
      } @else { <p class="muted">Loading…</p> }
    </div>

    <app-invoice-submit-dialog [invoice]="submitOpen() ? invoice() : null" (closed)="submitOpen.set(false)" (changed)="changed($event)" />
    <app-record-payment-dialog [invoice]="paymentOpen() ? invoice() : null" (closed)="paymentOpen.set(false)" (changed)="paymentOpen.set(false); load()" />
    <app-settlement-dialog [invoice]="settlementOpen() ? invoice() : null" (closed)="settlementOpen.set(false)" (changed)="settlementOpen.set(false); load()" />
    <app-carry-forward-dialog [invoice]="carryForwardOpen() ? invoice() : null" (closed)="carryForwardOpen.set(false)" (changed)="carryForwardOpen.set(false); load()" />
    <app-refund-dialog [invoice]="refundOpen() ? invoice() : null" (closed)="refundOpen.set(false)" (changed)="refundOpen.set(false); load()" />
    <vms-reason-dialog [open]="cancelOpen()" title="Cancel invoice" confirmLabel="Cancel invoice" [minLength]="10" [busy]="cancelBusy()" (closed)="cancelOpen.set(false)" (confirmed)="doCancel($event)" />
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
      .head h1 { font-size: 1.5rem; font-weight: 600; } .meta { display: flex; flex-wrap: wrap; gap: .6rem; align-items: center; margin-top: .35rem; }
      .actions-bar { display: flex; flex-wrap: wrap; gap: .5rem; } .alert { margin-bottom: 1rem; } .body { padding-bottom: 1.5rem; }
      .totals { display: flex; flex-wrap: wrap; gap: 1.5rem; margin-bottom: 1rem; padding: .75rem 1rem; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); }
      .totals dt { font-size: .8rem; color: var(--vms-muted); } .totals dd { margin: .1rem 0 0; font-weight: 600; }
      .totals .net dd, .totals .balance dd { font-size: 1.1rem; } .negative { color: var(--vms-danger-text); }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
    `,
  ],
})
export class InvoiceDetailComponent {
  private readonly api = inject(InvoicesApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly notify = inject(NotifyService);

  readonly invoice = signal<InvoiceModel | null>(null);
  readonly loadError = signal<string | null>(null);
  readonly tab = signal<Tab>('lines');
  protected readonly words = words;
  protected readonly severity = statusSeverity;
  protected readonly paySeverity = paymentStatusSeverity;

  readonly submitOpen = signal(false);
  readonly cancelOpen = signal(false);
  readonly cancelBusy = signal(false);
  readonly paymentOpen = signal(false);
  readonly settlementOpen = signal(false);
  readonly carryForwardOpen = signal(false);
  readonly refundOpen = signal(false);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => Number(p.get('id')))), { initialValue: 0 });
  protected readonly canSubmit = computed(() => this.auth.hasPermission('TRP.INVOICE.SUBMIT'));
  protected readonly canCancel = computed(() => this.auth.hasPermission('TRP.INVOICE.CANCEL'));
  protected readonly canPay = computed(() => this.auth.hasPermission('TRP.PAYMENT.CREATE'));
  protected readonly canSettle = computed(() => this.auth.hasPermission('TRP.PAYMENT.WRITEOFF') || this.auth.hasPermission('TRP.PAYMENT.DISCOUNT'));
  protected readonly canCarryForward = computed(() => this.auth.hasPermission('TRP.PAYMENT.CARRYFORWARD'));
  protected readonly canRefund = computed(() => this.auth.hasPermission('TRP.PAYMENT.REFUND'));
  protected readonly isCreditInvoice = computed(() => isCredit(this.invoice()?.balanceAmount ?? 0) && this.invoice()?.status === 'Submitted');
  protected readonly canRegenerate = computed(() => this.auth.hasPermission('TRP.INVOICE.REGENERATE'));
  // §38: "Invoices in Draft are simply edited, not regenerated. Cancelled and Inactive invoices cannot be
  // regenerated." — a fully paid invoice is also refused server-side (INVOICE_FULLY_PAID); left for the
  // wizard's own error path rather than duplicated here, since PaymentStatus alone doesn't distinguish "paid
  // via settlement" from "paid via payment" the way the backend's BalanceAmount ≤ 0 check does.
  protected readonly canBeRegenerated = computed(() => {
    const status = this.invoice()?.status;
    return status === 'Generated' || status === 'Submitted';
  });

  constructor() {
    this.route.paramMap.subscribe(() => this.load());
  }

  load(): void {
    const id = this.id();
    if (!id) return;
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (i) => this.invoice.set(i),
      error: (err: unknown) => this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This invoice was not found.' : errorMessage(err, 'The invoice could not be loaded.')),
    });
  }

  protected changed(i: InvoiceModel): void {
    this.submitOpen.set(false);
    this.invoice.set(i);
  }

  protected doCancel(reason: string): void {
    const inv = this.invoice();
    if (!inv) return;
    this.cancelBusy.set(true);
    this.api.cancel(inv.invoiceId, { rowVersion: inv.rowVersion, reason }).subscribe({
      next: (r) => { this.cancelBusy.set(false); this.cancelOpen.set(false); this.changed(r); },
      error: (err) => { this.cancelBusy.set(false); this.notify.error(err); },
    });
  }
}
