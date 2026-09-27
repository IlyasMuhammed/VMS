import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { LedgerApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CustomerBalanceSummaryModel } from '../../core/ledger.models';
import { NotifyService } from '../../core/notify.service';
import { balanceLabel, balanceSeverity } from './ledger-logic';

type Sign = 'All' | 'Dr' | 'Cr';

/**
 * Customer Balances (FSD §40A.4, §48.5's own unnumbered row): "all customers with Balance, Overdue, Credit,
 * Last payment date, Last invoice date; drill-down to statement." Reads `GET /api/customer-balances` in one
 * call (no true pagination on that endpoint — the whole customer list comes back at once, the same shape
 * `AdvancesApi.list` already established) and filters client-side, since the filters here (balance sign,
 * overdue only) are cheap boolean/sign checks, not worth a round trip each time.
 */
@Component({
  selector: 'app-customer-balances',
  standalone: true,
  imports: [FormsModule, RouterLink, ButtonModule, SelectModule, BusinessDatePipe],
  template: `
    <div class="page">
      <div class="page-header"><div><h1>Customer Balances</h1><div class="sub">What every customer owes, or is owed, right now.</div></div></div>

      <div class="card">
        <div class="toolbar">
          <div class="field"><label>Balance</label><p-select [ngModel]="sign()" (ngModelChange)="sign.set($event)" [options]="signOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></div>
          <label class="check"><input type="checkbox" [ngModel]="overdueOnly()" (ngModelChange)="overdueOnly.set($event)" /> Overdue only</label>
          <span class="grow"></span>
          <p-button label="Refresh" severity="secondary" [outlined]="true" size="small" [loading]="loading()" (onClick)="load()" />
        </div>

        @if (loading()) {
          <p class="hint">Loading…</p>
        } @else if (filtered().length === 0) {
          <p class="hint">No customers match these filters.</p>
        } @else {
          <table>
            <thead><tr><th>Customer</th><th>Currency</th><th>Balance</th><th>Overdue</th><th>Credit</th><th>Last payment</th><th>Last invoice</th><th></th></tr></thead>
            <tbody>
              @for (b of filtered(); track b.customerId + b.currencyCode) {
                <tr>
                  <td>{{ b.customerName }} <span class="mono muted">({{ b.customerCode }})</span></td>
                  <td>{{ b.currencyCode }}</td>
                  <td><span [class]="'chip chip--' + severity(b.balanceAmount)">{{ label(b.balanceAmount) }}</span></td>
                  <td>{{ b.overdueAmount || '—' }}</td>
                  <td>{{ b.creditAmount || '—' }}</td>
                  <td>{{ b.lastPaymentDate ? (b.lastPaymentDate | vmsDate) : '—' }}</td>
                  <td>{{ b.lastInvoiceDate ? (b.lastInvoiceDate | vmsDate) : '—' }}</td>
                  <td><a [routerLink]="['/customers', b.customerId]" [queryParams]="{ tab: 'ledger' }">Statement</a></td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: [
    `
      .toolbar { display: flex; align-items: flex-end; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .field { display: flex; flex-direction: column; gap: .3rem; min-width: 9rem; } .field label { font-size: .8rem; color: var(--vms-muted); }
      .check { display: inline-flex; align-items: center; gap: .4rem; font-size: .9rem; } .grow { flex: 1 1 auto; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; }
      th { color: var(--vms-muted); font-weight: 600; } .hint { color: var(--vms-muted); } .muted { color: var(--vms-muted); }
    `,
  ],
})
export class CustomerBalancesComponent {
  private readonly api = inject(LedgerApi);
  private readonly notify = inject(NotifyService);

  readonly loading = signal(true);
  readonly balances = signal<CustomerBalanceSummaryModel[]>([]);
  readonly sign = signal<Sign>('All');
  readonly overdueOnly = signal(false);
  protected readonly signOptions = [{ label: 'All', value: 'All' }, { label: 'Owed (Dr)', value: 'Dr' }, { label: 'Credit (Cr)', value: 'Cr' }];
  protected readonly label = balanceLabel;
  protected readonly severity = balanceSeverity;

  protected readonly filtered = computed(() => {
    const sign = this.sign();
    const overdueOnly = this.overdueOnly();
    return this.balances().filter((b) => {
      if (overdueOnly && b.overdueAmount <= 0) return false;
      if (sign === 'Dr' && b.balanceAmount <= 0) return false;
      if (sign === 'Cr' && b.balanceAmount >= 0) return false;
      return true;
    });
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.allBalances().subscribe({
      next: (found) => { this.loading.set(false); this.balances.set(found); },
      error: (err) => { this.loading.set(false); this.notify.error(err); },
    });
  }
}
