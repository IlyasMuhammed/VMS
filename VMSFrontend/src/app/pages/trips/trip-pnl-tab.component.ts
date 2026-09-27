import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TripOperationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { TripPnLModel } from '../../core/trip-operations.models';

/** Operational P&L tab (FSD §31): `TripAmount + Approved Income − (Fuel + Approved Expenses)`. An unpriced trip
 * shows "Not priced" instead of a number — never a silent zero. */
@Component({
  selector: 'app-trip-pnl-tab',
  standalone: true,
  imports: [ButtonModule],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    @if (pnl(); as p) {
      <dl class="grid">
        <div><dt>Revenue (trip amount)</dt><dd>{{ p.isPriced ? p.revenue + ' ' + p.currencyCode : 'Not priced' }}</dd></div>
        <div><dt>Approved income</dt><dd>{{ p.approvedIncome }} {{ p.currencyCode }}</dd></div>
        <div><dt>Fuel</dt><dd>{{ p.fuel }} {{ p.currencyCode }}</dd></div>
        <div><dt>Approved expenses</dt><dd>{{ p.approvedExpenses }} {{ p.currencyCode }}</dd></div>
        <div class="total"><dt>Operational P&amp;L</dt><dd [class.negative]="(p.operationalPnL ?? 0) < 0">{{ p.isPriced ? p.operationalPnL + ' ' + p.currencyCode : 'Not priced' }}</dd></div>
      </dl>
    } @else if (!error()) { <p class="muted">Loading…</p> }
  `,
  styles: [
    `
      .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 1rem 2rem; margin: 0; }
      dt { color: var(--vms-muted); font-size: .8rem; } dd { margin: .1rem 0 0; font-weight: 500; }
      .total dd { font-size: 1.2rem; font-weight: 700; } .negative { color: var(--vms-danger-text); } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripPnLTabComponent {
  private readonly api = inject(TripOperationsApi);

  readonly tripId = input.required<number>();
  protected readonly pnl = signal<TripPnLModel | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => { this.tripId(); untracked(() => this.load()); });
  }

  load(): void {
    this.error.set(null);
    this.api.pnl(this.tripId()).subscribe({
      next: (p) => this.pnl.set(p),
      error: (err) => this.error.set(errorMessage(err, 'The P&L could not be loaded.')),
    });
  }
}
