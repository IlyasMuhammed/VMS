import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { VehiclesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { Installment, Payable, RecurringCharge } from '../../core/vehicle.models';
import { EndChargeDialogComponent, RecurringChargeDialogComponent } from './recurring-charge-dialogs.component';
import { ConfirmEntryDialogComponent, WaiveEntryDialogComponent } from './payable-dialogs.component';
import { PayInstallmentDialogComponent } from './vehicle-finance-dialogs.component';
import { words } from './vehicle-logic';

/**
 * The vehicle screen's Recurring Charges tab and Due & Payments panel together (FSD §19A.5): what is configured on this vehicle,
 * and what of it is Due or Overdue right now — a generated entry, or a Bank Installment surfaced from the lease schedule with no
 * parallel schedule of its own (BR-VH-029). Confirming a Bank Installment item still goes through the existing payment dialog.
 */
@Component({
  selector: 'app-recurring-charges-tab',
  standalone: true,
  imports: [
    DecimalPipe, ButtonModule, TagModule, BusinessDatePipe, RecurringChargeDialogComponent, EndChargeDialogComponent, ConfirmEntryDialogComponent, WaiveEntryDialogComponent, PayInstallmentDialogComponent,
  ],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

    <div class="head-row">
      <h2>Due & Payments</h2>
    </div>
    <div class="scroll">
      <table>
        <thead><tr><th>Due</th><th>Charge</th><th>Payee</th><th class="num">Expected</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (p of payables(); track p.kind + '-' + p.id) {
            <tr [class.overdue]="p.status === 'Overdue'">
              <td class="nowrap">{{ p.dueDate | vmsDate }}</td>
              <td>{{ p.chargeType || words(p.kind) }}</td>
              <td>{{ p.payee?.name || '—' }}</td>
              <td class="num">@if (p.expectedAmount !== undefined && p.expectedAmount !== null) { {{ p.expectedAmount | number: '1.2-2' }} } @else { — }</td>
              <td><p-tag [value]="p.status" [severity]="p.status === 'Overdue' ? 'danger' : 'warn'" /></td>
              <td class="row-actions">
                @if (canConfirm()) { <p-button label="Confirm" [text]="true" size="small" (onClick)="confirm(p)" /> }
                @if (canWaive() && p.kind === 'RecurringCharge') { <p-button label="Waive" [text]="true" size="small" severity="secondary" (onClick)="waiveOrCancel(p, 'waive')" /> }
                @if (canManage() && p.kind === 'RecurringCharge') { <p-button label="Cancel" [text]="true" size="small" severity="danger" (onClick)="waiveOrCancel(p, 'cancel')" /> }
              </td>
            </tr>
          }
          @if (payables().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'Nothing due right now.' }}</td></tr> }
        </tbody>
      </table>
    </div>

    <div class="head-row">
      <h2>Recurring charges</h2>
      @if (canManage()) { <p-button label="Add a charge" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" /> }
    </div>
    <div class="scroll">
      <table>
        <thead><tr><th>Type</th><th>Payee</th><th class="num">Amount</th><th>Frequency</th><th>Next due</th><th>Posting</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (c of currentCharges(); track c.id) {
            <tr>
              <td>{{ c.chargeType }}</td><td>{{ c.payee?.name || '—' }}</td>
              <td class="num">@if (c.amount !== undefined && c.amount !== null) { {{ c.amount | number: '1.2-2' }} } @else { {{ words(c.amountBasis) }} }</td>
              <td>{{ words(c.frequency) }}</td><td class="nowrap">{{ c.nextDueDate ? (c.nextDueDate | vmsDate) : '—' }}</td>
              <td>{{ words(c.postingMode) }}</td><td><p-tag [value]="c.isActive ? 'Active' : 'Ended'" [severity]="c.isActive ? 'success' : 'secondary'" /></td>
              <td class="row-actions">@if (canManage() && c.isActive) {
                <p-button label="Amend" [text]="true" size="small" (onClick)="amendCharge.set(c)" />
                <p-button label="End" [text]="true" size="small" severity="danger" (onClick)="endCharge.set(c)" />
              }</td>
            </tr>
          }
          @if (currentCharges().length === 0) { <tr><td colspan="8" class="muted">{{ loading() ? 'Loading…' : 'No recurring charges configured.' }}</td></tr> }
        </tbody>
      </table>
    </div>
    @if (history().length > 0) {
      <details class="history">
        <summary>{{ history().length }} ended or superseded</summary>
        <table>
          <thead><tr><th>Type</th><th>Payee</th><th>Started</th><th>Ended</th><th>Reason</th></tr></thead>
          <tbody>@for (c of history(); track c.id) { <tr><td>{{ c.chargeType }}</td><td>{{ c.payee?.name || '—' }}</td><td>{{ c.startDate | vmsDate }}</td><td>{{ c.effectiveTo ? (c.effectiveTo | vmsDate) : '—' }}</td><td>{{ c.endReason || '—' }}</td></tr> }</tbody>
        </table>
      </details>
    }

    <app-recurring-charge-dialog [vehicleId]="vehicleId()" [adding]="addOpen()" [charge]="amendCharge()" (closed)="addOpen.set(false); amendCharge.set(null)" (saved)="addOpen.set(false); amendCharge.set(null); load()" />
    <app-end-charge-dialog [vehicleId]="vehicleId()" [charge]="endCharge()" (closed)="endCharge.set(null)" (ended)="endCharge.set(null); load()" />
    <app-confirm-entry-dialog [vehicleId]="vehicleId()" [entry]="confirmEntry()" (closed)="confirmEntry.set(null)" (confirmed)="confirmEntry.set(null); load()" />
    <app-waive-entry-dialog [vehicleId]="vehicleId()" [entry]="waiveEntry()" [kind]="waiveKind()" (closed)="waiveEntry.set(null)" (done)="waiveEntry.set(null); load()" />
    <app-pay-installment-dialog [vehicleId]="vehicleId()" [installment]="payInstallment()" (closed)="payInstallment.set(null)" (paid)="payInstallment.set(null); load()" (receiptAttached)="load()" />
  `,
  styles: [
    `
      h2 { font-size: 1.05rem; margin: 0; } .head-row { display: flex; align-items: center; justify-content: space-between; margin: 1.25rem 0 .6rem; } .head-row:first-child { margin-top: 0; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .num { text-align: right; font-variant-numeric: tabular-nums; } .nowrap { white-space: nowrap; } .row-actions { white-space: nowrap; text-align: right; }
      .scroll { max-height: 22rem; overflow: auto; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); }
      tr.overdue td:first-child { box-shadow: inset 3px 0 0 var(--vms-danger); }
      .alert { margin-bottom: 1rem; } .history { margin-top: 1.5rem; } .history summary { cursor: pointer; color: var(--vms-muted); font-size: .875rem; margin-bottom: .5rem; }
    `,
  ],
})
export class RecurringChargesTabComponent {
  private readonly api = inject(VehiclesApi);
  private readonly auth = inject(AuthService);

  readonly vehicleId = input.required<number>();

  protected readonly charges = signal<RecurringCharge[]>([]);
  protected readonly payables = signal<Payable[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly words = words;

  protected readonly currentCharges = computed(() => this.charges().filter((c) => c.isActive));
  protected readonly history = computed(() => this.charges().filter((c) => !c.isActive));

  protected readonly canManage = computed(() => this.auth.hasPermission('FIN.RECURRING.MANAGE'));
  protected readonly canConfirm = computed(() => this.auth.hasPermission('FIN.DUE.CONFIRM'));
  protected readonly canWaive = computed(() => this.auth.hasPermission('FIN.DUE.WAIVE'));

  protected readonly addOpen = signal(false);
  protected readonly amendCharge = signal<RecurringCharge | null>(null);
  protected readonly endCharge = signal<RecurringCharge | null>(null);
  protected readonly confirmEntry = signal<Payable | null>(null);
  protected readonly waiveEntry = signal<Payable | null>(null);
  protected readonly waiveKind = signal<'waive' | 'cancel'>('waive');
  protected readonly payInstallment = signal<Installment | null>(null);

  constructor() {
    effect(() => { this.vehicleId(); untracked(() => this.load()); });
  }

  load(): void {
    const id = this.vehicleId();
    this.loading.set(true);
    this.error.set(null);
    let pending = 2;
    const done = (): void => { if (--pending === 0) this.loading.set(false); };
    const failed = (err: unknown): void => { this.error.set(errorMessage(err, 'This could not be loaded.')); done(); };
    this.api.recurringCharges(id).subscribe({ next: (r) => { this.charges.set(r); done(); }, error: failed });
    this.api.payables(id).subscribe({ next: (r) => { this.payables.set(r); done(); }, error: failed });
  }

  /** A Bank Installment payable is confirmed through the existing installment-payment dialog, with its own row version. */
  protected confirm(p: Payable): void {
    if (p.kind === 'Installment') {
      this.api.installments(this.vehicleId()).subscribe({
        next: (rows) => { const row = rows.find((r) => r.id === p.id); if (row) this.payInstallment.set(row); },
        error: () => undefined,
      });
      return;
    }
    this.confirmEntry.set(p);
  }

  protected waiveOrCancel(p: Payable, kind: 'waive' | 'cancel'): void {
    this.waiveKind.set(kind);
    this.waiveEntry.set(p);
  }
}
