import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { AdvancesApi } from '../../core/api.services';
import { CustomerAdvanceModel } from '../../core/advance.models';
import { NotifyService } from '../../core/notify.service';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';
import { MoveAdvanceDialogComponent, NewAdvanceDialogComponent } from './advance-dialogs.component';
import { canAct, remaining, statusSeverity } from './advance-logic';

/**
 * Advances (FSD §37.4) — a genuinely separate, Open-trip-first workflow from the invoice-centric Payments
 * cluster: an advance is recorded against a specific Open trip, then applied automatically once that trip's own
 * invoice is Submitted (no action here for that — it happens as a side effect of Submit). Not one of the FSD's
 * own numbered screens; built as its own small list so Move/Refund/Reverse have somewhere to be reached from,
 * the same reasoning `ReceiptsComponent` documents for its own list.
 */
@Component({
  selector: 'app-advances',
  standalone: true,
  imports: [FormsModule, ButtonModule, TagModule, BusinessDatePipe, TripCustomerPickerComponent, ReasonDialogComponent, NewAdvanceDialogComponent, MoveAdvanceDialogComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Advances</h1><div class="sub">Advances recorded against Open trips, and where each one stands.</div></div>
        @if (canRecord()) { <p-button label="New advance" icon="pi pi-plus" (onClick)="adding.set(true)" /> }
      </div>

      <div class="card">
        <div class="toolbar">
          <div class="field"><label>Customer</label><vms-trip-customer-picker [ngModel]="customerId()" (ngModelChange)="onCustomerChange($event)" /></div>
          <p-button label="Clear" [text]="true" size="small" (onClick)="onCustomerChange(null)" />
        </div>

        @if (loading()) {
          <p class="hint">Loading…</p>
        } @else if (advances().length === 0) {
          <p class="hint">{{ customerId() ? 'No advances for this customer.' : 'No advances recorded yet.' }}</p>
        } @else {
          <table>
            <thead><tr><th>Advance no.</th><th>Trip</th><th>Date</th><th>Amount</th><th>Applied</th><th>Refunded</th><th>Remaining</th><th>Status</th><th></th></tr></thead>
            <tbody>
              @for (a of advances(); track a.customerAdvanceId) {
                <tr>
                  <td class="mono">{{ a.advanceNumber }}</td>
                  <td class="mono">{{ a.tripNumber }}</td>
                  <td>{{ a.advanceDate | vmsDate }}</td>
                  <td>{{ a.amount }}</td>
                  <td>{{ a.appliedAmount }}</td>
                  <td>{{ a.refundedAmount }}</td>
                  <td>{{ remainingOf(a) }}</td>
                  <td><p-tag [value]="a.status" [severity]="severity(a.status)" /></td>
                  <td class="actions">
                    @if (canAct(a.status) && canMove()) { <p-button label="Move" [text]="true" size="small" (onClick)="moving.set(a)" /> }
                    @if (canAct(a.status) && canRecord()) { <p-button label="Refund" [text]="true" size="small" (onClick)="refunding.set(a)" /> }
                    @if (canAct(a.status) && canRecord()) { <p-button label="Reverse" [text]="true" size="small" severity="danger" (onClick)="reversing.set(a)" /> }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </div>

    <app-new-advance-dialog [adding]="adding()" (closed)="adding.set(false)" (saved)="onSaved()" />
    <app-move-advance-dialog [advance]="moving()" (closed)="moving.set(null)" (saved)="onSaved()" />
    <vms-reason-dialog [open]="!!refunding()" title="Refund advance" confirmLabel="Refund" [minLength]="10" [busy]="actionBusy()" (closed)="refunding.set(null)" (confirmed)="doRefund($event)" />
    <vms-reason-dialog [open]="!!reversing()" title="Reverse advance" confirmLabel="Reverse" [minLength]="10" [busy]="actionBusy()" (closed)="reversing.set(null)" (confirmed)="doReverse($event)" />
  `,
  styles: [
    `
      .toolbar { display: flex; align-items: flex-end; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      .field { display: flex; flex-direction: column; gap: .3rem; min-width: 14rem; } .field label { font-size: .8rem; color: var(--vms-muted); }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .actions { display: flex; gap: .25rem; } .hint { color: var(--vms-muted); }
    `,
  ],
})
export class AdvancesComponent {
  private readonly api = inject(AdvancesApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  protected readonly canRecord = computed(() => this.auth.hasPermission('TRP.PAYMENT.ADVANCE'));
  protected readonly canMove = this.canRecord;

  readonly customerId = signal<number | null>(null);
  readonly advances = signal<CustomerAdvanceModel[]>([]);
  readonly loading = signal(false);

  readonly adding = signal(false);
  readonly moving = signal<CustomerAdvanceModel | null>(null);
  readonly refunding = signal<CustomerAdvanceModel | null>(null);
  readonly reversing = signal<CustomerAdvanceModel | null>(null);
  readonly actionBusy = signal(false);

  protected readonly severity = statusSeverity;
  protected readonly canAct = canAct;
  protected readonly remainingOf = remaining;

  constructor() {
    this.load();
  }

  protected onCustomerChange(customerId: number | null): void {
    this.customerId.set(customerId);
    this.load();
  }

  protected onSaved(): void {
    this.adding.set(false);
    this.moving.set(null);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.list(this.customerId()).subscribe({
      next: (found) => { this.loading.set(false); this.advances.set(found); },
      error: (err) => { this.loading.set(false); this.notify.error(err); },
    });
  }

  protected doRefund(reason: string): void {
    const a = this.refunding();
    if (!a) return;
    this.actionBusy.set(true);
    this.api.refund(a.customerAdvanceId, { reason }).subscribe({
      next: () => { this.actionBusy.set(false); this.refunding.set(null); this.notify.success('Advance refunded'); this.load(); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }

  protected doReverse(reason: string): void {
    const a = this.reversing();
    if (!a) return;
    this.actionBusy.set(true);
    this.api.reverse(a.customerAdvanceId, { reason }).subscribe({
      next: () => { this.actionBusy.set(false); this.reversing.set(null); this.notify.success('Advance reversed'); this.load(); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }
}
