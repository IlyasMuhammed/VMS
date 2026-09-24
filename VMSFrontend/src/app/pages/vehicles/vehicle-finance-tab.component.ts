import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { VehiclesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';
import { formatDate, todayDateOnly } from '../../core/datetime/datetime';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { FinancialSummary, Installment, LedgerEntry } from '../../core/vehicle.models';
import { PayInstallmentDialogComponent, ReverseEntryDialogComponent } from './vehicle-finance-dialogs.component';
import { words } from './vehicle-logic';

/**
 * A vehicle's money at a glance: paid to date, cost, and what is outstanding on the bank agreement, worked out on the server from the
 * ledger and the schedule each time (BR-VH-003). A figure the person may not see is not sent, so its tile is not drawn.
 */
@Component({
  selector: 'app-vehicle-summary',
  standalone: true,
  imports: [],
  template: `
    @if (tiles().length > 0) {
      <section class="summary" aria-label="Financial summary">
        @for (t of tiles(); track t.label) {
          <div class="tile" [class.alert-tile]="t.alert">
            <div class="label">{{ t.label }}</div>
            <div class="value">{{ t.value }}</div>
            @if (t.note) { <div class="note">{{ t.note }}</div> }
          </div>
        }
      </section>
    }
  `,
  styles: [
    `
      .summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr)); gap: .75rem; margin-bottom: 1rem; }
      .tile { border: 1px solid var(--vms-border); border-radius: var(--vms-radius); background: var(--vms-surface); padding: .75rem 1rem; }
      .alert-tile { border-color: var(--vms-danger-border); background: var(--vms-danger-bg); color: var(--vms-danger-text); }
      .label { font-size: .8rem; color: var(--vms-muted); } .alert-tile .label { color: inherit; }
      .value { font-size: 1.15rem; font-weight: 600; font-variant-numeric: tabular-nums; } .note { font-size: .8rem; color: var(--vms-muted); } .alert-tile .note { color: inherit; }
    `,
  ],
})
export class VehicleSummaryComponent {
  private readonly api = inject(VehiclesApi);
  readonly vehicleId = input.required<number>();
  /** Changes when something was paid or reversed, so the figures are asked for again. */
  readonly version = input(0);
  readonly summary = signal<FinancialSummary | null>(null);

  private readonly pkr = (n: number): string => `PKR ${new DecimalPipe('en-US').transform(n, '1.2-2')}`;
  private readonly day = (iso: string): string => formatDate(iso);

  protected readonly tiles = computed<{ label: string; value: string; note?: string; alert?: boolean }[]>(() => {
    const s = this.summary();
    if (!s) return [];
    const out: { label: string; value: string; note?: string; alert?: boolean }[] = [];
    if (s.paidToDate !== undefined) out.push({ label: 'Paid to date', value: this.pkr(s.paidToDate) });
    if (s.totalCost !== undefined) out.push({ label: 'Total cost', value: this.pkr(s.totalCost), note: s.majorExpenses ? `including ${this.pkr(s.majorExpenses)} of major expenses` : undefined });
    const a = s.agreement;
    if (a?.outstanding !== undefined) out.push({ label: 'Outstanding on the lease', value: this.pkr(a.outstanding), note: `${a.installmentsPaid} of ${a.installmentsTotal} paid` });
    if (a && a.nextDueDate) out.push({ label: 'Next installment due', value: this.day(a.nextDueDate), note: a.nextDueAmount !== undefined && a.nextDueAmount !== null ? this.pkr(a.nextDueAmount) : undefined });
    if (a && a.overdueCount > 0) out.push({ label: 'Overdue', value: `${a.overdueCount} installment${a.overdueCount === 1 ? '' : 's'}`, note: a.overdueAmount !== undefined ? this.pkr(a.overdueAmount) : undefined, alert: true });
    return out;
  });

  constructor() {
    effect(() => { this.vehicleId(); this.version(); untracked(() => this.load()); });
  }

  private load(): void {
    this.api.financialSummary(this.vehicleId()).subscribe({ next: (s) => this.summary.set(s), error: () => this.summary.set(null) });
  }
}

/**
 * The Finance tab of the vehicle screen (source §5): the bank agreement, its installment schedule with the Record payment action, and the
 * ledger with the Reverse action. An entry is never edited or deleted here; a mistake is reversed with a reason (BR-VH-011).
 */
@Component({
  selector: 'app-vehicle-finance-tab',
  standalone: true,
  imports: [DecimalPipe, ButtonModule, TagModule, BusinessDatePipe, PayInstallmentDialogComponent, ReverseEntryDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

    @if (agreement(); as a) {
      <h2>Bank agreement</h2>
      <dl class="facts">
        <div><dt>Bank</dt><dd>{{ a.bank?.name || '—' }}</dd></div>
        <div><dt>Status</dt><dd>{{ words(a.status) }}</dd></div>
        <div><dt>Installments paid</dt><dd>{{ a.installmentsPaid }} of {{ a.installmentsTotal }}</dd></div>
        @if (a.totalPayable !== undefined) { <div><dt>Total payable</dt><dd>PKR {{ a.totalPayable | number: '1.2-2' }}</dd></div> }
        @if (a.residual) { <div><dt>Residual, due at the end</dt><dd>PKR {{ a.residual | number: '1.2-2' }}</dd></div> }
        @if (a.outstanding !== undefined) { <div><dt>Outstanding</dt><dd>PKR {{ a.outstanding | number: '1.2-2' }}</dd></div> }
      </dl>

      <h2>Installment schedule</h2>
      <div class="scroll">
        <table>
          <thead><tr><th>No.</th><th>Due</th><th class="num">Expected</th><th class="num">Paid</th><th class="num">Remaining</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (i of installments(); track i.id) {
              <tr [class.overdue]="isOverdue(i)">
                <td>{{ i.isResidual ? 'Final' : i.installmentNo }}</td><td class="nowrap">{{ i.dueDate | vmsDate }}@if (isOverdue(i)) { <span class="muted"> · overdue</span> }</td>
                <td class="num">@if (i.expectedAmount !== undefined) { {{ i.expectedAmount | number: '1.2-2' }} } @else { — }</td>
                <td class="num">@if (i.paidAmount !== undefined) { {{ i.paidAmount | number: '1.2-2' }} } @else { — }</td>
                <td class="num">@if (i.remainingAmount !== undefined) { {{ i.remainingAmount | number: '1.2-2' }} } @else { — }</td>
                <td><p-tag [value]="words(i.status)" [severity]="i.status === 'Paid' ? 'success' : i.status === 'PartiallyPaid' ? 'warn' : 'secondary'" /></td>
                <td class="row-actions">@if (canPay() && a.status === 'Active' && i.status !== 'Paid') { <p-button label="Record payment" [text]="true" size="small" (onClick)="pay.set(i)" /> }</td>
              </tr>
            }
            @if (installments().length === 0) { <tr><td colspan="7" class="muted">{{ loading() ? 'Loading…' : 'No installments.' }}</td></tr> }
          </tbody>
        </table>
      </div>
    } @else if (!loading()) {
      <p class="muted">This vehicle has no bank finance agreement.</p>
    }

    <h2>Ledger</h2>
    <div class="scroll">
      <table>
        <thead><tr><th>Date</th><th>Entry</th><th>With</th><th>Reference</th><th class="num">Amount</th><th></th></tr></thead>
        <tbody>
          @for (e of ledger(); track e.id) {
            <tr [class.reversed]="e.isReversed">
              <td class="nowrap">{{ e.date | vmsDate }}</td>
              <td>{{ words(e.type) }}@if (e.subType) { <span class="muted"> · {{ words(e.subType) }}</span> }@if (e.isReversed) { <span class="muted"> · reversed</span> }</td>
              <td>{{ e.partner?.name || '—' }}</td>
              <td>{{ e.reference || '—' }}@if (e.reason) { <div class="muted small">Reason: {{ e.reason }}</div> }@if (e.hasReceipt) { <div class="small"><button type="button" class="link" (click)="download(e)">{{ e.receiptFileName || 'Receipt' }}</button></div> }</td>
              <td class="num">@if (e.amount !== undefined) { {{ e.amount | number: '1.2-2' }} } @else { — }</td>
              <td class="row-actions">@if (canReverse() && e.type !== 'Adjustment' && !e.isReversed) { <p-button label="Reverse" [text]="true" size="small" severity="danger" (onClick)="reverse.set(e)" /> }</td>
            </tr>
          }
          @if (ledger().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'Nothing has been posted yet. A draft posts nothing until it is activated.' }}</td></tr> }
        </tbody>
      </table>
    </div>

    <app-pay-installment-dialog [vehicleId]="vehicleId()" [installment]="pay()" (closed)="pay.set(null)" (paid)="changedAfter()" (receiptAttached)="load()" />
    <app-reverse-entry-dialog [vehicleId]="vehicleId()" [entry]="reverse()" (closed)="reverse.set(null)" (reversed)="reverse.set(null); changedAfter()" />
  `,
  styles: [
    `
      h2 { font-size: 1.05rem; margin: 1.25rem 0 .6rem; } h2:first-child { margin-top: 0; }
      .facts { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem 2rem; margin: 0; } dt { color: var(--vms-muted); font-size: .8rem; } dd { margin: .1rem 0 0; font-weight: 500; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .num { text-align: right; font-variant-numeric: tabular-nums; } .nowrap { white-space: nowrap; } .small { font-size: .8rem; } .row-actions { white-space: nowrap; text-align: right; }
      .scroll { max-height: 26rem; overflow: auto; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); }
      tr.reversed td { color: var(--vms-muted); } tr.reversed td:nth-child(5) { text-decoration: line-through; } tr.overdue td:first-child { box-shadow: inset 3px 0 0 var(--vms-danger); }
      .alert { margin-bottom: 1rem; }
      .link { border: 0; background: none; padding: 0; font: inherit; cursor: pointer; color: var(--vms-brand-text); text-decoration: underline; }
    `,
  ],
})
export class VehicleFinanceTabComponent {
  private readonly api = inject(VehiclesApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly vehicleId = input.required<number>();
  /** Something was paid or reversed here: the summary above the tabs must ask again. */
  readonly changed = output<void>();

  protected readonly installments = signal<Installment[]>([]);
  protected readonly ledger = signal<LedgerEntry[]>([]);
  protected readonly agreement = signal<NonNullable<FinancialSummary['agreement']> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pay = signal<Installment | null>(null);
  protected readonly reverse = signal<LedgerEntry | null>(null);
  protected readonly words = words;

  protected readonly canPay = computed(() => this.auth.hasPermission('FIN.INSTALLMENT.PAY'));
  protected readonly canReverse = computed(() => this.auth.hasPermission('FIN.ADJUSTMENT.POST'));

  constructor() {
    effect(() => { this.vehicleId(); untracked(() => this.load()); });
  }

  /** Overdue is judged against the person's own today, and is never stored: it is a fact about the date. */
  protected isOverdue(i: Installment): boolean {
    return i.status !== 'Paid' && i.dueDate < todayDateOnly();
  }

  load(): void {
    const id = this.vehicleId();
    this.loading.set(true);
    this.error.set(null);
    let pending = 3;
    const done = (): void => { if (--pending === 0) this.loading.set(false); };
    const failed = (err: unknown): void => { this.error.set(errorMessage(err, 'The finance details could not be loaded.')); done(); };
    this.api.installments(id).subscribe({ next: (r) => { this.installments.set(r); done(); }, error: failed });
    this.api.transactions(id).subscribe({ next: (r) => { this.ledger.set(r); done(); }, error: failed });
    this.api.financialSummary(id).subscribe({ next: (s) => { this.agreement.set(s.agreement ?? null); done(); }, error: failed });
  }

  protected changedAfter(): void {
    this.load();
    this.changed.emit();
  }

  protected download(entry: LedgerEntry): void {
    this.api.downloadReceipt(this.vehicleId(), entry.id).subscribe({
      next: ({ blob, fileName }) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err: unknown) => this.notify.error(err),
    });
  }
}
