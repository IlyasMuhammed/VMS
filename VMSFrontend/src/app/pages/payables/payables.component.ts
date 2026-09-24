import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { PayablesApi } from '../../core/api.services';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { BulkConfirmItem, Payable } from '../../core/vehicle.models';
import { BranchPickerComponent } from '../../shared/branch-picker.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { LookupPickerComponent } from '../../shared/lookup-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { PAYMENT_MODES, optionsOf, words } from '../vehicles/vehicle-logic';

/**
 * The Payables Due workbench (FSD §19A.5, FR-VH-016): every Due and Overdue item across the fleet, of every charge type,
 * brought together with a vehicle's unpaid lease installments (BR-VH-029). The primary daily screen for the Finance user —
 * entering payments vehicle by vehicle is possible but not the intended path — so bulk confirm is the headline action.
 */
@Component({
  selector: 'app-payables',
  standalone: true,
  imports: [DecimalPipe, RouterLink, FormsModule, ReactiveFormsModule, ButtonModule, CheckboxModule, SelectModule, InputNumberModule, TagModule, BusinessDatePipe, LookupPickerComponent, PartnerPickerComponent, BranchPickerComponent, DatePickerComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Payables due</h1><div class="sub">This month's due items across the fleet, in one place. Clear a batch at once with a common payment date and mode.</div></div>
      </div>

      <div class="card filters">
        <form [formGroup]="filters" class="filter-grid">
          <vms-lookup-picker type="RECURRING_CHARGE_TYPE" formControlName="chargeTypeId" placeholder="Any charge type" ariaLabel="Charge type" />
          <vms-partner-picker formControlName="payeeId" placeholder="Any payee" [allowCreate]="false" />
          <vms-branch-picker formControlName="branchId" placeholder="Any branch" ariaLabel="Branch" />
          <vms-date-picker formControlName="dueFrom" placeholder="Due from" />
          <vms-date-picker formControlName="dueTo" placeholder="Due to" />
          <label class="overdue-toggle"><p-checkbox [binary]="true" formControlName="overdueOnly" /> Overdue only</label>
        </form>
      </div>

      @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

      <div class="card">
        <table>
          <thead>
            <tr>
              <th class="check"><p-checkbox [binary]="true" [ngModel]="allSelected()" (ngModelChange)="toggleAll($event)" [ngModelOptions]="{ standalone: true }" [disabled]="items().length === 0" /></th>
              <th>Vehicle</th><th>Charge</th><th>Payee</th><th>Branch</th><th>Due</th><th class="num">Expected</th><th>Status</th>
            </tr>
          </thead>
          <tbody>
            @for (p of items(); track p.kind + '-' + p.vehicleId + '-' + p.id) {
              <tr [class.overdue]="p.status === 'Overdue'">
                <td class="check"><p-checkbox [binary]="true" [ngModel]="selected().has(key(p))" (ngModelChange)="toggle(p, $event)" [ngModelOptions]="{ standalone: true }" /></td>
                <td><a [routerLink]="['/vehicles', p.vehicleId]" class="mono">{{ p.vehicleRegistrationNo }}</a></td>
                <td>{{ p.chargeType || words(p.kind) }}</td>
                <td>{{ p.payee?.name || '—' }}</td>
                <td>{{ p.branch || '—' }}</td>
                <td class="nowrap">{{ p.dueDate | vmsDate }}</td>
                <td class="num">@if (p.expectedAmount !== undefined && p.expectedAmount !== null) { {{ p.expectedAmount | number: '1.2-2' }} } @else { — }</td>
                <td><p-tag [value]="p.status" [severity]="p.status === 'Overdue' ? 'danger' : 'warn'" /></td>
              </tr>
            }
            @if (items().length === 0) { <tr><td colspan="8" class="muted">{{ loading() ? 'Loading…' : 'Nothing due matches these filters.' }}</td></tr> }
          </tbody>
        </table>
      </div>
    </div>

    @if (canConfirm() && selected().size > 0) {
      <div class="bulkbar">
        <span>{{ selected().size }} selected</span>
        <span class="fields">
          <vms-date-picker [ngModel]="bulkDate()" (ngModelChange)="bulkDate.set($event)" [ngModelOptions]="{ standalone: true }" placeholder="Payment date" />
          <p-select [options]="modes" optionLabel="label" optionValue="value" [ngModel]="bulkMode()" (ngModelChange)="bulkMode.set($event)" [ngModelOptions]="{ standalone: true }" placeholder="Payment mode" [showClear]="true" appendTo="body" />
        </span>
        <p-button [label]="'Confirm ' + selected().size" icon="pi pi-check" [loading]="confirming()" (onClick)="confirmSelected()" />
        <p-button label="Clear" severity="secondary" [text]="true" (onClick)="clearSelection()" [disabled]="confirming()" />
      </div>
    }
  `,
  styles: [
    `
      .filters { margin-bottom: 1rem; } .filter-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr)); gap: .75rem; align-items: center; }
      .overdue-toggle { display: flex; align-items: center; gap: .5rem; font-size: .9rem; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .check { width: 2rem; } .num { text-align: right; font-variant-numeric: tabular-nums; } .nowrap { white-space: nowrap; }
      tr.overdue td:first-child { box-shadow: inset 3px 0 0 var(--vms-danger); }
      .alert { margin-bottom: 1rem; }
      .bulkbar { position: sticky; bottom: 0; z-index: 5; display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; margin-top: 1rem; padding: .85rem 1.25rem; background: var(--vms-surface); border: 1px solid var(--vms-border); border-radius: var(--vms-radius); box-shadow: var(--vms-shadow-md); }
      .fields { display: inline-flex; gap: .5rem; }
    `,
  ],
})
export class PayablesComponent {
  private readonly api = inject(PayablesApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);

  protected readonly items = signal<Payable[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly words = words;
  protected readonly modes = optionsOf(PAYMENT_MODES);

  protected readonly canConfirm = computed(() => this.auth.hasPermission('FIN.DUE.CONFIRM'));

  protected readonly selected = signal<Set<string>>(new Set());
  protected readonly allSelected = computed(() => this.items().length > 0 && this.selected().size === this.items().length);
  protected readonly bulkDate = signal<string | null>(null);
  protected readonly bulkMode = signal<string | null>(null);
  protected readonly confirming = signal(false);

  readonly filters = this.fb.group({
    chargeTypeId: this.fb.control<number | null>(null),
    payeeId: this.fb.control<number | null>(null),
    branchId: this.fb.control<string | null>(null),
    dueFrom: this.fb.control<string | null>(null),
    dueTo: this.fb.control<string | null>(null),
    overdueOnly: this.fb.nonNullable.control(false),
  });

  constructor() {
    this.filters.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.load());
    this.load();
  }

  protected key(p: Payable): string {
    return `${p.kind}-${p.vehicleId}-${p.id}`;
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    const f = this.filters.getRawValue();
    this.api.list({
      chargeTypeId: f.chargeTypeId, payeeId: f.payeeId, branchId: f.branchId, dueFrom: f.dueFrom, dueTo: f.dueTo, overdueOnly: f.overdueOnly,
    }).subscribe({
      next: (items) => { this.loading.set(false); this.items.set(items); this.selected.set(new Set()); },
      error: (err: unknown) => { this.loading.set(false); this.error.set(errorMessage(err, 'The payables list could not be loaded.')); },
    });
  }

  protected toggle(p: Payable, checked: boolean): void {
    this.selected.update((s) => { const next = new Set(s); if (checked) next.add(this.key(p)); else next.delete(this.key(p)); return next; });
  }

  protected toggleAll(checked: boolean): void {
    this.selected.set(checked ? new Set(this.items().map((p) => this.key(p))) : new Set());
  }

  protected clearSelection(): void {
    this.selected.set(new Set());
  }

  confirmSelected(): void {
    if (this.confirming()) return;
    const chosen = this.items().filter((p) => this.selected().has(this.key(p)));
    if (chosen.length === 0) return;
    const items: BulkConfirmItem[] = chosen.map((p) => ({ kind: p.kind, vehicleId: p.vehicleId, id: p.id }));
    this.confirming.set(true);
    this.api.bulkConfirm({ items, paidOn: this.bulkDate(), paymentMode: this.bulkMode() }).subscribe({
      next: (result) => {
        this.confirming.set(false);
        if (result.failed.length === 0) this.notify.success(`${result.succeeded} confirmed.`);
        else this.notify.warn(`${result.succeeded} confirmed, ${result.failed.length} could not be: ${result.failed.map((f) => f.message).join('; ')}`, 'Some entries were not confirmed');
        this.bulkDate.set(null);
        this.bulkMode.set(null);
        this.load();
      },
      error: (err: unknown) => { this.confirming.set(false); this.notify.error(err); },
    });
  }
}
