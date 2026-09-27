import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripOperationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { TripExpenseModel } from '../../core/trip-operations.models';
import { ConfirmService } from '../../shared/confirm.service';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { TripExpenseDialogComponent } from './trip-expense-dialogs.component';
import { expenseSeverity, words } from './trip-logic';

/** Expenses tab (FSD §29, screen 18): logged expenses with their own approval state. */
@Component({
  selector: 'app-trip-expenses-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, InstantPipe, TripExpenseDialogComponent, ReasonDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }
    <div class="head-row">
      @if (canEdit()) { <p-button label="Log expense" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <table>
      <thead><tr><th>Date</th><th>Type</th><th>Description</th><th>Amount</th><th>Method</th><th>Approval</th><th></th></tr></thead>
      <tbody>
        @for (e of expenses(); track e.tripExpenseId) {
          <tr [class.voided]="e.isVoided">
            <td class="nowrap">{{ e.expenseDate | vmsInstant }}</td><td>#{{ e.expenseTypeId }}</td><td>{{ e.description || e.otherExpenseType || '—' }}</td>
            <td>{{ e.amount }}</td><td>{{ words(e.paymentMethod) }}</td>
            <td><p-tag [value]="e.approvalStatus" [severity]="severity(e.approvalStatus)" /></td>
            <td class="row-actions">
              @if (e.isVoided) { <p-tag value="Voided" severity="danger" /> }
              @else {
                @if (canApprove() && e.approvalStatus === 'Pending') {
                  <p-button label="Approve" [text]="true" size="small" (onClick)="approve(e)" />
                  <p-button label="Reject" [text]="true" size="small" severity="danger" (onClick)="rejecting.set(e)" />
                }
                @if (canEdit()) { <p-button label="Void" [text]="true" size="small" severity="danger" (onClick)="voiding.set(e)" /> }
              }
            </td>
          </tr>
        }
        @if (expenses().length === 0) { <tr><td colspan="7" class="muted">{{ loading() ? 'Loading…' : 'No expenses logged yet.' }}</td></tr> }
      </tbody>
    </table>

    <app-trip-expense-dialog [tripId]="addOpen() ? tripId() : null" (closed)="addOpen.set(false)" (saved)="addOpen.set(false); load()" />
    <vms-reason-dialog [open]="!!rejecting()" title="Reject expense" confirmLabel="Reject" [minLength]="10" [busy]="actionBusy()" (closed)="rejecting.set(null)" (confirmed)="doReject($event)" />
    <vms-reason-dialog [open]="!!voiding()" title="Void expense" confirmLabel="Void" [minLength]="10" [busy]="actionBusy()" (closed)="voiding.set(null)" (confirmed)="doVoid($event)" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      tr.voided td { color: var(--vms-muted); text-decoration: line-through; } .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripExpensesTabComponent {
  private readonly api = inject(TripOperationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);

  readonly tripId = input.required<number>();
  protected readonly expenses = signal<TripExpenseModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canEdit = () => this.auth.hasPermission('TRP.EXPENSE.EDIT');
  protected readonly canApprove = () => this.auth.hasPermission('TRP.EXPENSE.APPROVE');
  protected readonly severity = expenseSeverity;
  protected readonly words = words;

  protected readonly addOpen = signal(false);
  protected readonly rejecting = signal<TripExpenseModel | null>(null);
  protected readonly voiding = signal<TripExpenseModel | null>(null);
  protected readonly actionBusy = signal(false);

  constructor() {
    effect(() => { this.tripId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.expenses(this.tripId()).subscribe({
      next: (rows) => { this.loading.set(false); this.expenses.set(rows); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Expenses could not be loaded.')); },
    });
  }

  protected approve(e: TripExpenseModel): void {
    void this.confirm.ask({ title: 'Approve expense', message: `Approve this expense of ${e.amount}?`, confirmLabel: 'Approve', cancelLabel: 'Cancel', icon: 'pi pi-check' }).then((go) => {
      if (!go) return;
      this.api.decideExpense(e.tripExpenseId, { approved: true }).subscribe({ next: () => this.load(), error: (err) => this.notify.error(err) });
    });
  }

  protected doReject(reason: string): void {
    const e = this.rejecting();
    if (!e) return;
    this.actionBusy.set(true);
    this.api.decideExpense(e.tripExpenseId, { approved: false, reason }).subscribe({
      next: () => { this.actionBusy.set(false); this.rejecting.set(null); this.notify.success('Expense rejected'); this.load(); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }

  protected doVoid(reason: string): void {
    const e = this.voiding();
    if (!e) return;
    this.actionBusy.set(true);
    this.api.voidExpense(e.tripExpenseId, { reason }).subscribe({
      next: () => { this.actionBusy.set(false); this.voiding.set(null); this.notify.success('Expense voided'); this.load(); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }
}
