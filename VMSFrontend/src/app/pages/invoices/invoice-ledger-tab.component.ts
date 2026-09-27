import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { TagModule } from 'primeng/tag';
import { LedgerApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { InvoiceLedgerModel } from '../../core/ledger.models';
import { NotifyService } from '../../core/notify.service';
import { balanceLabel, entryTypeLabel } from '../ledger/ledger-logic';

/**
 * Invoice Ledger tab (FSD §40A.4: "on Invoice Detail: every entry for that invoice, invoice-level running
 * balance, and a reconciliation line 'Ledger balance = Invoice balance ✓'").
 */
@Component({
  selector: 'app-invoice-ledger-tab',
  standalone: true,
  imports: [TagModule, BusinessDatePipe],
  template: `
    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (ledger()) {
      @let l = ledger()!;
      <div class="reconcile">
        <p-tag [value]="l.reconciled ? 'Reconciled' : 'Not reconciled'" [severity]="l.reconciled ? 'success' : 'danger'" />
        <span>Ledger balance {{ label(l.ledgerBalance) }} · Invoice balance {{ l.invoiceBalance }}</span>
      </div>
      @if (l.entries.length === 0) {
        <p class="muted">No ledger entries yet — entries post only once this invoice is Submitted.</p>
      } @else {
        <table>
          <thead><tr><th>Date</th><th>Entry no.</th><th>Doc no.</th><th>Type</th><th>Narration</th><th>Debit</th><th>Credit</th></tr></thead>
          <tbody>
            @for (e of l.entries; track e.customerLedgerEntryId) {
              <tr>
                <td>{{ e.entryDate | vmsDate }}</td>
                <td class="mono">{{ e.entryNumber }}</td>
                <td class="mono">{{ e.documentNo }}</td>
                <td>{{ type(e.entryType) }}</td>
                <td>{{ e.narration }}</td>
                <td>{{ e.debitAmount || '—' }}</td>
                <td>{{ e.creditAmount || '—' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    }
  `,
  styles: [
    `
      .reconcile { display: flex; align-items: center; gap: .75rem; margin-bottom: 1rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; }
      th { color: var(--vms-muted); font-weight: 600; } .muted { color: var(--vms-muted); }
    `,
  ],
})
export class InvoiceLedgerTabComponent {
  private readonly api = inject(LedgerApi);
  private readonly notify = inject(NotifyService);

  readonly invoiceId = input.required<number>();
  readonly loading = signal(true);
  readonly ledger = signal<InvoiceLedgerModel | null>(null);
  protected readonly type = entryTypeLabel;
  protected readonly label = balanceLabel;

  constructor() {
    effect(() => { this.invoiceId(); untracked(() => this.load()); });
  }

  private load(): void {
    this.loading.set(true);
    this.api.invoiceLedger(this.invoiceId()).subscribe({
      next: (l) => { this.loading.set(false); this.ledger.set(l); },
      error: (err) => { this.loading.set(false); this.notify.error(err); },
    });
  }
}
