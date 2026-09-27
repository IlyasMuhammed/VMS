import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripOperationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { TripIncomeModel } from '../../core/trip-operations.models';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { TripIncomeDialogComponent } from './trip-income-dialogs.component';

/** Income tab (FSD §30): additional revenue beyond the trip's own rate, billable or not. */
@Component({
  selector: 'app-trip-income-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, BusinessDatePipe, TripIncomeDialogComponent, ReasonDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Record income" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Date</th><th>Type</th><th>Amount</th><th>Billable</th><th>Reference</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (i of income(); track i.tripIncomeId) {
          <tr [class.voided]="i.isVoided">
            <td class="nowrap">{{ i.incomeDate | vmsDate }}</td><td>#{{ i.incomeTypeId }}</td><td>{{ i.amount }} {{ i.currencyCode }}</td>
            <td>@if (i.isBillable) { <p-tag value="Billable" severity="info" /> } @else { — }</td>
            <td>{{ i.reference || '—' }}</td>
            <td>
              @if (i.isVoided) { <p-tag value="Voided" severity="danger" /> }
              @else if (i.invoiceLineId) { <p-tag value="Billed" severity="success" /> }
              @else { <p-tag value="Unbilled" severity="secondary" /> }
            </td>
            <td class="row-actions">@if (canEdit() && !i.isVoided && !i.invoiceLineId) { <p-button label="Void" [text]="true" size="small" severity="danger" (onClick)="voiding.set(i)" /> }</td>
          </tr>
        }
        @if (income().length === 0) { <tr><td colspan="7" class="muted">{{ loading() ? 'Loading…' : 'No income recorded yet.' }}</td></tr> }
      </tbody>
    </table>

    <app-trip-income-dialog [tripId]="addOpen() ? tripId() : null" (closed)="addOpen.set(false)" (saved)="addOpen.set(false); load()" />
    <vms-reason-dialog [open]="!!voiding()" title="Void income" confirmLabel="Void" [minLength]="10" [busy]="voidBusy()" (closed)="voiding.set(null)" (confirmed)="doVoid($event)" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      tr.voided td { color: var(--vms-muted); text-decoration: line-through; } .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripIncomeTabComponent {
  private readonly api = inject(TripOperationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly tripId = input.required<number>();
  protected readonly income = signal<TripIncomeModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.INCOME.EDIT');

  protected readonly addOpen = signal(false);
  protected readonly voiding = signal<TripIncomeModel | null>(null);
  protected readonly voidBusy = signal(false);

  constructor() {
    effect(() => { this.tripId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.income(this.tripId()).subscribe({
      next: (rows) => { this.loading.set(false); this.income.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Income could not be loaded.')); },
    });
  }

  protected doVoid(reason: string): void {
    const i = this.voiding();
    if (!i) return;
    this.voidBusy.set(true);
    this.api.voidIncome(i.tripIncomeId, { reason }).subscribe({
      next: () => { this.voidBusy.set(false); this.voiding.set(null); this.notify.success('Income voided'); this.load(); },
      error: (err) => { this.voidBusy.set(false); this.notify.error(err); },
    });
  }
}
