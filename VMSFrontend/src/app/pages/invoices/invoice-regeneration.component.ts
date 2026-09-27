import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { apiErrors } from '../../core/api-error';
import { InvoicesApi } from '../../core/api.services';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { CreateInvoiceAdjustmentRequest, EligibleTripRow, EligibleTripsResult, InvoiceModel } from '../../core/invoice.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { blockingReason } from './invoice-logic';

type Step = 1 | 2 | 3 | 4;
let nextAdjustmentId = 1;
interface AdjustmentRow extends CreateInvoiceAdjustmentRequest { id: number }

/**
 * Regenerate (FSD §38, §39, header action on screen 21): same shape as Invoice Generation (Parameters → Trips →
 * Adjustments → Review), but against a fixed customer (the old invoice's own) and with a required regeneration
 * reason and re-price option. Deliberately its own component rather than a mode flag on
 * `InvoiceGenerationComponent` — the two flows differ enough (fixed customer, pre-filled period, no "save as
 * Draft," `regeneratingInvoiceId` on the eligibility search, transfers/released-trips in the response) that
 * sharing one file would need more branching than it would save.
 */
@Component({
  selector: 'app-invoice-regeneration',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, RouterLink, ButtonModule, CheckboxModule, InputNumberModule, InputTextModule, SelectModule, TextareaModule, BusinessDatePipe, DatePickerComponent, FieldComponent],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/invoices">Invoices</a> <span aria-hidden="true">/</span> @if (old(); as o) { <a [routerLink]="['/invoices', o.invoiceId]">{{ o.invoiceNumber }}</a> } <span aria-hidden="true">/</span> <span>Regenerate</span></div>
      @if (old(); as o) {
        <h1>Regenerate {{ o.invoiceNumber }}</h1>
        <p class="hint">Same customer ({{ o.customerName }}) — {{ o.invoiceNumber }} will become Inactive once the new invoice is created.</p>

        @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }

        <div class="steps">
          <span [class.active]="step() === 1">1. Parameters</span> <span [class.active]="step() === 2">2. Trips</span>
          <span [class.active]="step() === 3">3. Adjustments</span> <span [class.active]="step() === 4">4. Review &amp; Regenerate</span>
        </div>

        <div class="card">
          @if (step() === 1) {
            <form [formGroup]="paramsForm" class="form-grid" novalidate>
              <vms-field label="Period from" [control]="paramsForm.controls.periodFrom" for="rg-from"><vms-date-picker inputId="rg-from" formControlName="periodFrom" /></vms-field>
              <vms-field label="Period to" [control]="paramsForm.controls.periodTo" for="rg-to"><vms-date-picker inputId="rg-to" formControlName="periodTo" /></vms-field>
              <vms-field label="Invoice date" [control]="paramsForm.controls.invoiceDate" for="rg-date"><vms-date-picker inputId="rg-date" formControlName="invoiceDate" /></vms-field>
              <vms-field class="full" label="Regeneration reason" [control]="paramsForm.controls.regenerationReason" for="rg-reason"><input pInputText id="rg-reason" formControlName="regenerationReason" /></vms-field>
              <label class="check full"><p-checkbox formControlName="repriceTrips" [binary]="true" inputId="rg-reprice" /> <label for="rg-reprice">Re-price trips from rate master (§26)</label></label>
            </form>
            <div class="foot"><span></span><p-button label="Search" icon="pi pi-search" [loading]="searching()" (onClick)="search()" /></div>
          }

          @if (step() === 2 && result(); as r) {
            <div class="tiles">
              <div class="tile"><span class="n">{{ r.summary.completedTrips }}</span><span>Completed</span></div>
              <div class="tile"><span class="n">{{ r.summary.alreadyInvoiced }}</span><span>Already invoiced</span></div>
              <div class="tile"><span class="n">{{ r.summary.blocked }}</span><span>Blocked</span></div>
              <div class="tile"><span class="n">{{ r.summary.available }}</span><span>Available</span></div>
              <div class="tile"><span class="n">{{ selected().size }}</span><span>Selected</span></div>
              <div class="tile"><span class="n">{{ selectedAmount() }}</span><span>Selected amount</span></div>
            </div>
            @if (r.overlaps.length > 0) {
              <div class="alert warning">This period overlaps {{ r.overlaps.length }} existing invoice(s): {{ overlapNumbers(r) }}.</div>
            }
            @if (r.blockingErrors.length > 0) {
              <div class="alert warning">
                <div>{{ r.blockingErrors.length }} trip(s) blocked:</div>
                <ul>@for (b of r.blockingErrors; track b.tripId) { <li>Trip {{ b.tripId }} ({{ b.tripDate | vmsDate }}): {{ reason(b.code) }}</li> }</ul>
              </div>
            }
            <label class="check"><input type="checkbox" [checked]="allAvailableSelected()" (change)="toggleAll($any($event.target).checked)" /> Select all available</label>
            <table>
              <thead><tr><th></th><th>Trip no.</th><th>Date</th><th>Route</th><th>Vehicle</th><th>Reference</th><th>Amount</th><th>Category</th></tr></thead>
              <tbody>
                @for (t of r.trips; track t.tripId) {
                  <tr [class.blocked]="t.category === 'Blocked'">
                    <td><input type="checkbox" [disabled]="t.category !== 'Available'" [checked]="selected().has(t.tripId)" (change)="toggle(t)" /></td>
                    <td class="mono">{{ t.tripNumber }}</td><td>{{ t.tripDate | vmsDate }}</td><td>{{ t.route || '—' }}</td><td>{{ t.vehicle || '—' }}</td>
                    <td>{{ t.customerTripReference || '—' }}</td><td>{{ t.amount ?? '—' }}</td><td>{{ t.category }}</td>
                  </tr>
                }
                @if (r.trips.length === 0) { <tr><td colspan="8" class="muted">No trips found for this customer and period.</td></tr> }
              </tbody>
            </table>
            <div class="foot">
              <p-button label="Back" severity="secondary" [text]="true" (onClick)="step.set(1)" />
              <p-button label="Next: Adjustments" icon="pi pi-arrow-right" [disabled]="selected().size === 0" (onClick)="step.set(3)" />
            </div>
          }

          @if (step() === 3) {
            <table>
              <thead><tr><th>Month</th><th>Amount</th><th>Note</th><th>Reference invoice</th><th></th></tr></thead>
              <tbody>
                @for (a of adjustments(); track a.id; let i = $index) {
                  <tr>
                    <td><input pInputText [ngModel]="a.adjustmentMonth" (ngModelChange)="setAdjustment(i, 'adjustmentMonth', $event)" [ngModelOptions]="{ standalone: true }" placeholder="2026-08" /></td>
                    <td><p-inputnumber [ngModel]="a.amount" (ngModelChange)="setAdjustment(i, 'amount', $event)" [ngModelOptions]="{ standalone: true }" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></td>
                    <td><input pInputText [ngModel]="a.note" (ngModelChange)="setAdjustment(i, 'note', $event)" [ngModelOptions]="{ standalone: true }" /></td>
                    <td><input pInputText [ngModel]="a.referenceInvoiceNo" (ngModelChange)="setAdjustment(i, 'referenceInvoiceNo', $event)" [ngModelOptions]="{ standalone: true }" /></td>
                    <td><p-button icon="pi pi-trash" [text]="true" severity="danger" (onClick)="removeAdjustment(i)" ariaLabel="Remove adjustment" /></td>
                  </tr>
                }
                @if (adjustments().length === 0) { <tr><td colspan="5" class="muted">No adjustments — optional.</td></tr> }
              </tbody>
            </table>
            <p-button label="Add adjustment" icon="pi pi-plus" [text]="true" size="small" (onClick)="addAdjustment()" />
            <div class="foot"><p-button label="Back" severity="secondary" [text]="true" (onClick)="step.set(2)" /><p-button label="Next: Review" icon="pi pi-arrow-right" (onClick)="step.set(4)" /></div>
          }

          @if (step() === 4 && result(); as r) {
            <div class="form-grid">
              @if (r.resolved.templateOptions.length > 1) {
                <div class="field"><label>Invoice format *</label><p-select [(ngModel)]="templateId" [options]="templateOptions(r)" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></div>
              }
              @if (r.resolved.billingAddressOptions.length > 1) {
                <div class="field"><label>Billing address</label><p-select [(ngModel)]="billingAddressId" [options]="addressOptions(r)" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></div>
              }
            </div>
            <div class="tiles">
              <div class="tile"><span class="n">{{ selected().size }}</span><span>Trips</span></div>
              <div class="tile"><span class="n">{{ selectedAmount() }}</span><span>Trip amount</span></div>
              <div class="tile"><span class="n">{{ adjustmentTotal() }}</span><span>Adjustments</span></div>
              <div class="tile"><span class="n">{{ selectedAmount() + adjustmentTotal() }}</span><span>Gross (before tax)</span></div>
            </div>
            <div class="foot">
              <p-button label="Back" severity="secondary" [text]="true" (onClick)="step.set(3)" />
              <p-button label="Regenerate" icon="pi pi-check" [loading]="busy()" (onClick)="regenerate()" />
            </div>
          }
        </div>
      } @else { <p class="muted">Loading…</p> }
    </div>
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; } h1 { font-size: 1.5rem; font-weight: 600; margin-bottom: .25rem; }
      .hint { color: var(--vms-muted); margin-bottom: 1rem; }
      .steps { display: flex; gap: 1.5rem; margin-bottom: 1rem; font-size: .9rem; color: var(--vms-muted); } .steps .active { color: var(--vms-text); font-weight: 600; }
      .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; margin-bottom: 1rem; } .full { grid-column: 1 / -1; }
      .field { display: flex; flex-direction: column; gap: .3rem; } .field label { font-size: .85rem; }
      .tiles { display: flex; flex-wrap: wrap; gap: 1rem; margin-bottom: 1rem; }
      .tile { display: flex; flex-direction: column; align-items: center; padding: .6rem 1rem; border: 1px solid var(--vms-border); border-radius: var(--vms-radius); min-width: 7rem; }
      .tile .n { font-size: 1.3rem; font-weight: 700; }
      table { width: 100%; border-collapse: collapse; margin-bottom: .75rem; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      tr.blocked td { color: var(--vms-muted); }
      .check { display: inline-flex; align-items: center; gap: .4rem; font-size: .9rem; margin-bottom: .5rem; }
      .foot { margin-top: 1rem; display: flex; justify-content: space-between; align-items: center; }
      .alert { margin-bottom: 1rem; } .alert ul { margin: .35rem 0 0; padding-left: 1.25rem; }
    `,
  ],
})
export class InvoiceRegenerationComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(InvoicesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => Number(p.get('id')))), { initialValue: 0 });
  readonly old = signal<InvoiceModel | null>(null);

  readonly step = signal<Step>(1);
  readonly searching = signal(false);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  readonly result = signal<EligibleTripsResult | null>(null);
  readonly selected = signal<Set<number>>(new Set());
  readonly adjustments = signal<AdjustmentRow[]>([]);
  templateId: number | null = null;
  billingAddressId: number | null = null;

  readonly paramsForm = this.fb.group({
    periodFrom: this.fb.control<string | null>(null, Validators.required),
    periodTo: this.fb.control<string | null>(null, Validators.required),
    invoiceDate: this.fb.control<string | null>(null),
    regenerationReason: this.fb.nonNullable.control('', Validators.required),
    repriceTrips: this.fb.nonNullable.control(false),
  });

  protected readonly reason = blockingReason;
  protected readonly allAvailableSelected = computed(() => {
    const r = this.result();
    if (!r) return false;
    const available = r.trips.filter((t) => t.category === 'Available');
    return available.length > 0 && available.every((t) => this.selected().has(t.tripId));
  });
  protected readonly selectedAmount = computed(() => {
    const r = this.result();
    if (!r) return 0;
    return r.trips.filter((t) => this.selected().has(t.tripId)).reduce((sum, t) => sum + (t.amount ?? 0), 0);
  });
  protected readonly adjustmentTotal = computed(() => this.adjustments().reduce((sum, a) => sum + (a.amount || 0), 0));

  constructor() {
    this.route.paramMap.subscribe(() => this.load());
  }

  private load(): void {
    const id = this.id();
    if (!id) return;
    this.api.get(id).subscribe({
      next: (inv) => {
        this.old.set(inv);
        this.paramsForm.reset({ periodFrom: inv.periodFrom, periodTo: inv.periodTo, invoiceDate: null, regenerationReason: '', repriceTrips: false });
      },
      error: (err) => this.notify.error(err),
    });
  }

  protected templateOptions(r: EligibleTripsResult) {
    return r.resolved.templateOptions.map((t) => ({ label: `${t.name} v${t.version}${t.isDefault ? ' (default)' : ''}`, value: t.id }));
  }

  protected addressOptions(r: EligibleTripsResult) {
    return r.resolved.billingAddressOptions.map((a) => ({ label: a.name + (a.isDefault ? ' (default)' : ''), value: a.id }));
  }

  protected overlapNumbers(r: EligibleTripsResult): string {
    return r.overlaps.map((o) => o.invoiceNumber).join(', ');
  }

  search(): void {
    const o = this.old();
    if (!o || this.paramsForm.invalid) { this.paramsForm.markAllAsTouched(); return; }
    this.problems.set([]);
    this.searching.set(true);
    const f = this.paramsForm.getRawValue();
    const oldTripIds = new Set(o.lines.filter((l) => l.tripId != null).map((l) => l.tripId!));
    this.api.searchEligibleTrips({ customerId: o.customerId, periodFrom: f.periodFrom!, periodTo: f.periodTo!, invoiceDate: f.invoiceDate, regeneratingInvoiceId: o.invoiceId }).subscribe({
      next: (r) => {
        this.searching.set(false);
        this.result.set(r);
        // §38: "everything editable and pre-filled with the old invoice's own values" — pre-select exactly the
        // trips already on the old invoice (now "Available" thanks to `regeneratingInvoiceId`), not every trip
        // that happens to be Available in range; adding a newly-eligible trip is a conscious choice here.
        this.selected.set(new Set(r.trips.filter((t) => t.category === 'Available' && oldTripIds.has(t.tripId)).map((t) => t.tripId)));
        const defaultTemplate = r.resolved.templateOptions.find((t) => t.id === o.customerInvoiceTemplateId) ?? r.resolved.templateOptions.find((t) => t.isDefault) ?? r.resolved.templateOptions[0];
        this.templateId = defaultTemplate?.id ?? null;
        const defaultAddress = r.resolved.billingAddressOptions.find((a) => a.id === o.customerBillingAddressId) ?? r.resolved.billingAddressOptions.find((a) => a.isDefault) ?? r.resolved.billingAddressOptions[0];
        this.billingAddressId = defaultAddress?.id ?? null;
        this.step.set(2);
      },
      error: (err) => { this.searching.set(false); this.notify.error(err); },
    });
  }

  protected toggle(t: EligibleTripRow): void {
    if (t.category !== 'Available') return;
    this.selected.update((s) => {
      const next = new Set(s);
      if (next.has(t.tripId)) next.delete(t.tripId); else next.add(t.tripId);
      return next;
    });
  }

  protected toggleAll(checked: boolean): void {
    const r = this.result();
    if (!r) return;
    this.selected.set(checked ? new Set(r.trips.filter((t) => t.category === 'Available').map((t) => t.tripId)) : new Set());
  }

  addAdjustment(): void {
    this.adjustments.update((rows) => [...rows, { id: nextAdjustmentId++, adjustmentMonth: '', amount: 0, note: '', referenceInvoiceNo: null }]);
  }

  removeAdjustment(index: number): void {
    this.adjustments.update((rows) => rows.filter((_, i) => i !== index));
  }

  setAdjustment(index: number, field: keyof CreateInvoiceAdjustmentRequest, value: unknown): void {
    this.adjustments.update((rows) => rows.map((r, i) => (i === index ? { ...r, [field]: value } : r)));
  }

  regenerate(): void {
    const o = this.old();
    if (!o || this.busy()) return;
    this.problems.set([]);
    this.busy.set(true);
    const f = this.paramsForm.getRawValue();
    this.api.regenerate(o.invoiceId, {
      periodFrom: f.periodFrom, periodTo: f.periodTo, invoiceDate: f.invoiceDate, customerInvoiceTemplateId: this.templateId, customerBillingAddressId: this.billingAddressId,
      tripIds: [...this.selected()], adjustments: this.adjustments().map(({ id: _id, ...a }) => a), repriceTrips: f.repriceTrips, regenerationReason: f.regenerationReason.trim(),
    }).subscribe({
      next: (result) => {
        this.busy.set(false);
        const extra = result.warnings.length > 0 ? ` ${result.warnings.join(' ')}` : '';
        this.notify.success(`Invoice regenerated as ${result.invoice.invoiceNumber}.${extra}`);
        void this.router.navigate(['/invoices', result.invoice.invoiceId]);
      },
      error: (err) => this.refused(err),
    });
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    const found = apiErrors(err);
    if (found.length === 0) { this.notify.error(err); return; }
    this.problems.set(found.map((e) => this.messages.describe(e)));
  }
}
