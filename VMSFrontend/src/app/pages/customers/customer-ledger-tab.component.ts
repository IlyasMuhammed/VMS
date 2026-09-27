import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { AuthService } from '../../core/auth.service';
import { LedgerApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { CustomerBalanceModel } from '../../core/customer.models';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CustomerLedgerStatementModel } from '../../core/ledger.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { applyServerErrors } from '../../shared/server-errors';
import { balanceLabel, entryTypeLabel } from '../ledger/ledger-logic';

/**
 * Customer Ledger — statement (FSD §40A.4, §48.5's own unnumbered "Customer Ledger" row): filters, the
 * opening/period/closing/overdue header, and the running-balance grid, one currency at a time (LR-9). Also
 * where the go-live opening balance (L16, §47.2) is posted — the ledger tab is its own natural home for that
 * one Admin action, rather than a separate screen for a single-use, one-time-per-customer-per-currency action.
 *
 * Not built here: the "Invoice" filter (§40A.4 lists it, but there is no invoice-scoped-to-this-customer picker
 * yet to drive it) — a row's own invoice link (and the Invoice Detail's own Ledger tab it opens) covers the
 * same need in practice; the bulk opening-balance CSV importer (`POST /api/opening-balances/import`, CC-40's
 * own still-blocked scope); and "Email statement" (§40A.4: itself Client-Confirmation-Required, not built).
 */
@Component({
  selector: 'app-customer-ledger-tab',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, RouterLink, ButtonModule, CheckboxModule, DialogModule, InputNumberModule, InputTextModule, SelectModule, BusinessDatePipe, DatePickerComponent, FieldComponent],
  template: `
    <div class="toolbar">
      <div class="field"><label>From</label><vms-date-picker [ngModel]="from()" (ngModelChange)="from.set($event)" /></div>
      <div class="field"><label>To</label><vms-date-picker [ngModel]="to()" (ngModelChange)="to.set($event)" /></div>
      @if (currencyOptions().length > 1) {
        <div class="field"><label>Currency</label><p-select [ngModel]="currencyCode()" (ngModelChange)="currencyCode.set($event)" [options]="currencyOptions()" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></div>
      }
      <label class="check"><p-checkbox [ngModel]="includeReversedPairs()" (ngModelChange)="includeReversedPairs.set($event)" [binary]="true" inputId="cl-reversed" /> <label for="cl-reversed">Include reversed pairs</label></label>
      <span class="grow"></span>
      @if (canPost()) { <p-button label="Post opening balance" severity="secondary" [outlined]="true" size="small" (onClick)="openingOpen.set(true)" /> }
      <p-button label="Export PDF" icon="pi pi-file-pdf" severity="secondary" [outlined]="true" size="small" [loading]="exporting()" [disabled]="!statement()" (onClick)="exportPdf()" />
    </div>

    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (statement()) {
      @let s = statement()!;
      <div class="totals">
        <div><dt>Opening balance</dt><dd>{{ label(s.openingBalance) }}</dd></div>
        <div><dt>Period debits</dt><dd>{{ s.periodDebits }}</dd></div>
        <div><dt>Period credits</dt><dd>{{ s.periodCredits }}</dd></div>
        <div class="closing"><dt>Closing balance</dt><dd>{{ label(s.closingBalance) }}</dd></div>
        <div><dt>Overdue</dt><dd>{{ s.overdueAmount }}</dd></div>
      </div>

      @if (s.rows.length === 0) {
        <p class="muted">No ledger entries in this period. Opening balance: {{ s.currencyCode }} {{ label(s.openingBalance) }}.</p>
      } @else {
        <table>
          <thead><tr><th>Date</th><th>Entry no.</th><th>Doc no.</th><th>Type</th><th>Invoice</th><th>Narration</th><th>Debit</th><th>Credit</th><th>Running balance</th></tr></thead>
          <tbody>
            @for (r of s.rows; track r.customerLedgerEntryId) {
              <tr>
                <td>{{ r.entryDate | vmsDate }}</td>
                <td class="mono">{{ r.entryNumber }}</td>
                <td class="mono">{{ r.documentNo }}</td>
                <td>{{ type(r.entryType) }}</td>
                <td>@if (r.invoiceId) { <a [routerLink]="['/invoices', r.invoiceId]">{{ r.invoiceNumber }}</a> } @else { — }</td>
                <td>{{ r.narration }}</td>
                <td>{{ r.debitAmount || '—' }}</td>
                <td>{{ r.creditAmount || '—' }}</td>
                <td>{{ label(r.runningBalance) }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    }

    <p-dialog [visible]="openingOpen()" (visibleChange)="!$event && openingOpen.set(false)" [modal]="true" [style]="{ width: '440px' }" header="Post opening balance" [closable]="!obBusy()">
      @if (obProblems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of obProblems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="obForm" class="stack" novalidate>
        <vms-field label="Amount" [control]="obForm.controls.amount" for="ob-amount" hint="Positive = Dr (receivable), negative = Cr (credit)."><p-inputnumber inputId="ob-amount" formControlName="amount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Currency" [control]="obForm.controls.currencyCode" for="ob-currency"><input pInputText id="ob-currency" formControlName="currencyCode" maxlength="3" style="text-transform: uppercase" /></vms-field>
        <vms-field label="As of date" [control]="obForm.controls.asOfDate" for="ob-date"><vms-date-picker inputId="ob-date" formControlName="asOfDate" /></vms-field>
        <vms-field label="Reason" [control]="obForm.controls.reason" for="ob-reason"><input pInputText id="ob-reason" formControlName="reason" /></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="openingOpen.set(false)" [disabled]="obBusy()" />
        <p-button label="Post" icon="pi pi-check" [loading]="obBusy()" (onClick)="postOpeningBalance()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .toolbar { display: flex; align-items: flex-end; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .field { display: flex; flex-direction: column; gap: .3rem; min-width: 9rem; } .field label { font-size: .8rem; color: var(--vms-muted); }
      .check { display: inline-flex; align-items: center; gap: .4rem; font-size: .85rem; } .grow { flex: 1 1 auto; }
      .totals { display: flex; flex-wrap: wrap; gap: 1.5rem; margin-bottom: 1rem; padding: .75rem 1rem; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); }
      .totals dt { font-size: .8rem; color: var(--vms-muted); } .totals dd { margin: .1rem 0 0; font-weight: 600; } .totals .closing dd { font-size: 1.1rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .muted { color: var(--vms-muted); } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; } ul { margin: .25rem 0; padding-left: 1.25rem; }
    `,
  ],
})
export class CustomerLedgerTabComponent {
  private readonly api = inject(LedgerApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessagesService);

  readonly customerId = input.required<number>();
  readonly balances = input<CustomerBalanceModel[]>([]);
  readonly posted = output<void>();

  protected readonly canPost = computed(() => this.auth.hasPermission('TRP.LEDGER.OPENINGBALANCE'));
  protected readonly currencyOptions = computed(() => this.balances().map((b) => ({ label: b.currencyCode, value: b.currencyCode })));
  protected readonly type = entryTypeLabel;
  protected readonly label = balanceLabel;

  readonly from = signal<string | null>(null);
  readonly to = signal<string | null>(null);
  readonly currencyCode = signal<string | null>(null);
  readonly includeReversedPairs = signal(true);
  readonly loading = signal(true);
  readonly exporting = signal(false);
  readonly statement = signal<CustomerLedgerStatementModel | null>(null);

  readonly openingOpen = signal(false);
  readonly obBusy = signal(false);
  readonly obProblems = signal<string[]>([]);
  readonly obForm = this.fb.group({
    amount: this.fb.control<number | null>(null, Validators.required),
    currencyCode: this.fb.nonNullable.control(''),
    asOfDate: this.fb.control<string | null>(null, Validators.required),
    reason: this.fb.nonNullable.control(''),
  });

  constructor() {
    effect(() => {
      this.customerId();
      const currencies = this.currencyOptions();
      untracked(() => {
        if (this.currencyCode() === null && currencies.length > 0) this.currencyCode.set(currencies[0].value);
        this.load();
      });
    });
    effect(() => {
      if (!this.openingOpen()) return;
      untracked(() => {
        this.obProblems.set([]);
        this.obForm.reset({ amount: null, currencyCode: this.currencyCode() ?? '', asOfDate: null, reason: '' });
      });
    });
  }

  load(): void {
    this.loading.set(true);
    this.api.statement(this.customerId(), {
      from: this.from(), to: this.to(), currencyCode: this.currencyCode(), includeReversedPairs: this.includeReversedPairs(),
    }).subscribe({
      next: (s) => { this.loading.set(false); this.statement.set(s); },
      error: (err) => { this.loading.set(false); this.notify.error(err); },
    });
  }

  protected exportPdf(): void {
    this.exporting.set(true);
    this.api.statementPdf(this.customerId(), { from: this.from(), to: this.to(), currencyCode: this.currencyCode() }).subscribe({
      next: ({ blob, fileName }) => {
        this.exporting.set(false);
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => { this.exporting.set(false); this.notify.error(err); },
    });
  }

  protected postOpeningBalance(): void {
    if (this.obBusy() || this.obForm.invalid) { this.obForm.markAllAsTouched(); return; }
    this.obBusy.set(true);
    this.obProblems.set([]);
    const f = this.obForm.getRawValue();
    this.api.postOpeningBalance(this.customerId(), {
      amount: f.amount!, currencyCode: f.currencyCode.trim().toUpperCase() || null, asOfDate: f.asOfDate!, reason: f.reason.trim() || null,
    }).subscribe({
      next: () => {
        this.obBusy.set(false);
        this.openingOpen.set(false);
        this.notify.success('Opening balance posted');
        this.load();
        this.posted.emit();
      },
      error: (err) => {
        this.obBusy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) { this.notify.error(err); return; }
        this.obProblems.set(applyServerErrors(this.obForm, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
