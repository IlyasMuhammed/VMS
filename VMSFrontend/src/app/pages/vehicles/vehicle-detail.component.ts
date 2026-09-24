import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TabsModule } from 'primeng/tabs';
import { map } from 'rxjs';
import { VehiclesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { BusinessDatePipe, InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { AttachedItem, OdometerReading, Vehicle, VehicleHistory } from '../../core/vehicle.models';
import { VehicleStatusComponent } from './vehicle-bits.component';
import { VehicleFinanceTabComponent, VehicleSummaryComponent } from './vehicle-finance-tab.component';
import { RecurringChargesTabComponent } from './vehicle-recurring-charges-tab.component';
import { DocumentsTabComponent } from '../documents/documents-tab.component';
import {
  AssignDriverDialogComponent, AttachItemDialogComponent, ChangeCategoryDialogComponent, DisposeDialogComponent, ItemAction, ItemActionDialogComponent, OdometerDialogComponent,
  VehicleStatusDialogComponent,
} from './vehicle-dialogs.component';
import { canDispose, describeChange, describeLifecycle, entityLabel, isDraft, isInFleet, statusMoves, words } from './vehicle-logic';

/** The vehicle's history: every change to it and what belongs to it, and its lifecycle (FSD §23.2). Read-only. */
@Component({
  selector: 'app-vehicle-history-tab',
  standalone: true,
  imports: [ButtonModule, InstantPipe, BusinessDatePipe],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load(1)" /></div> }
    <section>
      <h2>Lifecycle</h2>
      @if (history()?.lifecycle?.length) {
        <table>
          <thead><tr><th>When</th><th>Who</th><th>What</th><th>Effective</th><th>Reason</th></tr></thead>
          <tbody>@for (l of history()!.lifecycle; track $index) { <tr><td class="nowrap">{{ l.occurredOn | vmsInstant }}</td><td>{{ l.userName || 'System' }}</td><td>{{ lifecycleText(l) }}@if (l.amount !== undefined && l.amount !== null) { <span class="muted"> · PKR {{ l.amount }}</span> }</td><td class="nowrap">{{ l.effectiveDate | vmsDate }}</td><td>{{ l.reason || '—' }}</td></tr> }</tbody>
        </table>
      } @else { <p class="muted">{{ loading() ? 'Loading…' : 'Nothing recorded yet.' }}</p> }
    </section>
    <section>
      <h2>Changes</h2>
      @if (groups().length === 0) { <p class="muted">{{ loading() ? 'Loading…' : 'Nothing recorded yet.' }}</p> }
      @else {
        <!-- Everything one save did is one group, so a reviewer sees the whole induction at once (FSD §23.2). -->
        @for (g of groups(); track g.id) {
          <div class="group">
            <div class="group-head"><strong>{{ g.at | vmsInstant }}</strong> <span class="muted">{{ g.who }}</span></div>
            <ul>@for (c of g.rows; track c.id) { <li><span class="muted">{{ entity(c.entity) }}:</span> {{ describe(c) }}@if (c.reason) { <span class="muted"> — {{ c.reason }}</span> }</li> }</ul>
          </div>
        }
        @if (changes().length < total()) { <p-button label="Show older changes" severity="secondary" [outlined]="true" [loading]="loading()" (onClick)="load(page() + 1)" /> }
      }
    </section>
  `,
  styles: [`section { margin-bottom: 2rem; } h2 { font-size: 1.05rem; margin-bottom: .6rem; } table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; } .nowrap { white-space: nowrap; } .group { border: 1px solid var(--vms-border); border-radius: var(--vms-radius); padding: .6rem .9rem; margin-bottom: .6rem; } .group-head { margin-bottom: .25rem; } .group ul { margin: 0; padding-left: 1.1rem; font-size: .875rem; }`],
})
export class VehicleHistoryTabComponent {
  private readonly api = inject(VehiclesApi);
  readonly vehicleId = input.required<number>();
  readonly version = input('');
  readonly history = signal<VehicleHistory | null>(null);
  readonly changes = signal<VehicleHistory['changes']['items']>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  protected readonly entity = entityLabel;
  protected readonly describe = describeChange;
  protected readonly lifecycleText = describeLifecycle;
  /** The changes grouped by the save that made them, newest first. */
  protected readonly groups = computed(() => {
    const out: { id: string; at: string; who: string; rows: VehicleHistory['changes']['items'] }[] = [];
    for (const c of this.changes()) {
      const last = out[out.length - 1];
      if (last && last.id === c.groupId) last.rows.push(c);
      else out.push({ id: c.groupId, at: c.occurredAt, who: c.userName || 'System', rows: [c] });
    }
    return out;
  });

  constructor() {
    effect(() => { this.vehicleId(); this.version(); untracked(() => this.load(1)); });
  }

  protected load(page: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.history(this.vehicleId(), page).subscribe({
      next: (h) => {
        this.loading.set(false);
        this.page.set(page);
        this.history.set(h);
        this.total.set(h.changes.totalCount);
        this.changes.update((current) => (page === 1 ? h.changes.items : [...current, ...h.changes.items]));
      },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'The history could not be loaded.')); },
    });
  }
}

/**
 * A vehicle already saved (FSD §19, §20, §23): a header with the code, registration, status, category and driver; and tabs for its
 * details, ownership (with the Change category action), attached items, finance, driver and odometer, and history. Above the tabs a
 * financial summary (paid to date, cost, outstanding) is drawn for a vehicle that is not a draft; each figure only for those who may see it.
 */
@Component({
  selector: 'app-vehicle-detail',
  standalone: true,
  imports: [
    RouterLink, ButtonModule, TabsModule, BusinessDatePipe, VehicleStatusComponent, VehicleHistoryTabComponent, VehicleStatusDialogComponent, ChangeCategoryDialogComponent, VehicleFinanceTabComponent, VehicleSummaryComponent,
    RecurringChargesTabComponent, DisposeDialogComponent, AssignDriverDialogComponent, OdometerDialogComponent, AttachItemDialogComponent, ItemActionDialogComponent, DocumentsTabComponent,
  ],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/vehicles">Vehicles</a> <span aria-hidden="true">/</span> <span>{{ vehicle()?.registrationNo ?? '…' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (vehicle()) {
        @let v = vehicle()!;
        <header class="head">
          <div>
            <h1>{{ v.registrationNo }}</h1>
            <div class="meta"><span class="mono">{{ v.vehicleCode }}</span> <vms-vehicle-status [status]="v.status" />
              @if (v.currentCategory) { <span class="chip chip--info">{{ words(v.currentCategory) }}</span> }
              @if (v.currentCounterparty) { <span class="muted">with {{ v.currentCounterparty.name }}</span> }
            </div>
          </div>
          <div class="actions-bar">
            @if (canEdit()) { <p-button label="Edit details" icon="pi pi-pencil" severity="secondary" [outlined]="true" size="small" [routerLink]="['/vehicles', v.id, 'edit']" /> }
            @if (canStatus()) { <p-button label="Change status" icon="pi pi-flag" severity="secondary" [outlined]="true" size="small" (onClick)="statusOpen.set(true)" /> }
            @if (canCategory()) { <p-button label="Change category" icon="pi pi-sitemap" severity="secondary" [outlined]="true" size="small" (onClick)="categoryOpen.set(true)" /> }
            @if (canDisposeIt()) { <p-button label="Retire, sell or transfer" icon="pi pi-sign-out" severity="danger" [outlined]="true" size="small" (onClick)="disposeOpen.set(true)" /> }
          </div>
        </header>

        @if (!draft()) { <app-vehicle-summary [vehicleId]="v.id" [version]="financeVersion()" /> }

        @if (draft()) {
          <div class="alert info">This vehicle is a draft. Finish entering it in the wizard; ownership, items and drivers are for vehicles in the fleet.
            @if (canEdit()) { <a [routerLink]="['/vehicles', v.id, 'edit']">Continue in the wizard</a> }</div>
        }
        @if (disposed()) { <div class="alert info">This vehicle has left the fleet ({{ words(v.status) }}). Its history stays as it was.</div> }

        <div class="card body">
          <p-tabs [value]="tab()" (valueChange)="tab.set($any($event))" [scrollable]="true">
            <p-tablist>
              <p-tab value="details">Details</p-tab>
              <p-tab value="ownership">Ownership</p-tab>
              <p-tab value="items">Attached items</p-tab>
              @if (!draft()) { <p-tab value="finance">Finance</p-tab> }
              @if (!draft()) { <p-tab value="charges">Recurring charges</p-tab> }
              @if (canSeeDocuments()) { <p-tab value="documents">Documents</p-tab> }
              <p-tab value="driver">Driver & odometer</p-tab>
              <p-tab value="history">History</p-tab>
            </p-tablist>
            <p-tabpanels>
              <p-tabpanel value="details">
                <dl class="grid">
                  @for (row of detailRows(); track row[0]) { <div><dt>{{ row[0] }}</dt><dd>{{ row[1] || '—' }}</dd></div> }
                </dl>
              </p-tabpanel>

              <p-tabpanel value="ownership">
                @if (v.relation; as r) {
                  <h2>Now: {{ words(r.category) }}</h2>
                  <dl class="grid">
                    <div><dt>With</dt><dd>{{ r.counterparty?.name || '—' }}</dd></div>
                    <div><dt>From</dt><dd>{{ r.effectiveFrom | vmsDate }}</dd></div>
                    <div><dt>Until</dt><dd>{{ r.agreementEndDate ? (r.agreementEndDate | vmsDate) : 'No end date' }}</dd></div>
                    @for (row of relationRows(r); track row[0]) { <div><dt>{{ row[0] }}</dt><dd>{{ row[1] }}</dd></div> }
                  </dl>
                } @else { <p class="muted">{{ v.currentCategory === 'SelfOwned' ? 'Self owned: no counterparty.' : 'No ownership category yet: it is set when the vehicle is activated.' }}</p> }

                @if (v.relationHistory.length > 0) {
                  <h2>All arrangements</h2>
                  <table>
                    <thead><tr><th>Category</th><th>With</th><th>From</th><th>To</th></tr></thead>
                    <tbody>@for (r of v.relationHistory; track r.id) { <tr><td>{{ words(r.category) }}</td><td>{{ r.counterparty?.name || '—' }}</td><td>{{ r.effectiveFrom | vmsDate }}</td><td>{{ r.effectiveTo ? (r.effectiveTo | vmsDate) : 'Current' }}</td></tr> }</tbody>
                  </table>
                }
              </p-tabpanel>

              <p-tabpanel value="items">
                @if (canItems()) { <p-button label="Attach an item" icon="pi pi-plus" size="small" (onClick)="attachOpen.set(true)" /> }
                <table>
                  <thead><tr><th>Type</th><th>Description</th><th>Serial</th><th>Supplier</th><th>Installed</th><th>Status</th><th></th></tr></thead>
                  <tbody>
                    @for (i of items(); track i.id) {
                      <tr>
                        <td>{{ i.itemType }}</td><td>{{ i.description }}@if (i.cost !== undefined && i.cost !== null) { <div class="muted small">PKR {{ i.cost }}</div> }</td><td class="mono">{{ i.serialNo || '—' }}</td>
                        <td>{{ i.supplier?.name || '—' }}</td><td>{{ i.installationDate | vmsDate }}</td><td>{{ i.status }}@if (i.detachedOn) { <div class="muted small">{{ i.detachedOn | vmsDate }}</div> }</td>
                        <td class="row-actions">@if (canItems() && i.status === 'Attached') {
                          <p-button label="Transfer" [text]="true" size="small" (onClick)="itemAction.set({ vehicleId: v.id, item: i, kind: 'transfer' })" />
                          <p-button label="Detach" [text]="true" size="small" severity="danger" (onClick)="itemAction.set({ vehicleId: v.id, item: i, kind: 'detach' })" /> }</td>
                      </tr>
                    }
                    @if (items().length === 0) { <tr><td colspan="7" class="muted">{{ itemsLoading() ? 'Loading…' : 'No items attached.' }}</td></tr> }
                  </tbody>
                </table>
                @if (!fleet()) { <p class="muted">Items can be attached once the vehicle is in the fleet.</p> }
              </p-tabpanel>

              @if (!draft()) { <p-tabpanel value="finance">@if (tab() === 'finance') { <app-vehicle-finance-tab [vehicleId]="v.id" (changed)="financeVersion.set(financeVersion() + 1)" /> }</p-tabpanel> }
              @if (!draft()) { <p-tabpanel value="charges">@if (tab() === 'charges') { <app-recurring-charges-tab [vehicleId]="v.id" /> }</p-tabpanel> }
              @if (canSeeDocuments()) { <p-tabpanel value="documents">@if (tab() === 'documents') { <app-documents-tab ownerType="Vehicle" [ownerId]="v.id" /> }</p-tabpanel> }

              <p-tabpanel value="driver">
                <h2>Default driver</h2>
                <p>{{ v.defaultDriver ? v.defaultDriver.name + ' (' + v.defaultDriver.bpCode + ')' : 'No driver assigned.' }}</p>
                @if (canDriver() && fleet()) {
                  <p-button label="Assign a driver" icon="pi pi-user" size="small" (onClick)="assignOpen.set(true)" />
                  @if (v.defaultDriver) { <p-button label="Release" severity="secondary" [outlined]="true" size="small" (onClick)="release()" /> }
                }
                <h2>Odometer</h2>
                @if (canEdit() && fleet()) { <p-button label="Add a reading" icon="pi pi-plus" size="small" (onClick)="odoOpen.set(v.id)" /> }
                <table>
                  <thead><tr><th>Date</th><th>Reading (km)</th><th>Source</th><th>Notes</th></tr></thead>
                  <tbody>
                    @for (r of odometer(); track r.id) { <tr><td>{{ r.readingDate | vmsDate }}</td><td>{{ r.km }}</td><td>{{ words(r.source) }}</td><td>{{ r.notes || '—' }}</td></tr> }
                    @if (odometer().length === 0) { <tr><td colspan="4" class="muted">No readings yet.</td></tr> }
                  </tbody>
                </table>
              </p-tabpanel>

              <p-tabpanel value="history">@if (tab() === 'history') { <app-vehicle-history-tab [vehicleId]="v.id" [version]="v.rowVersion" /> }</p-tabpanel>
            </p-tabpanels>
          </p-tabs>
        </div>
      } @else { <p class="muted">Loading…</p> }
    </div>

    <app-vehicle-status-dialog [vehicle]="statusOpen() ? vehicle() : null" (closed)="statusOpen.set(false)" (changed)="changed($event)" />
    <app-change-category-dialog [vehicle]="categoryOpen() ? vehicle() : null" (closed)="categoryOpen.set(false)" (changed)="changed($event)" />
    <app-dispose-dialog [vehicle]="disposeOpen() ? vehicle() : null" (closed)="disposeOpen.set(false)" (changed)="changed($event)" />
    <app-assign-driver-dialog [vehicle]="assignOpen() ? vehicle() : null" (closed)="assignOpen.set(false)" (changed)="changed($event)" />
    <app-odometer-dialog [vehicleId]="odoOpen()" (closed)="odoOpen.set(null)" (saved)="odoOpen.set(null); loadOdometer()" />
    <app-attach-item-dialog [vehicleId]="attachOpen() ? vehicle()?.id ?? null : null" (closed)="attachOpen.set(false)" (saved)="attachOpen.set(false); loadItems()" />
    <app-item-action-dialog [action]="itemAction()" (closed)="itemAction.set(null)" (saved)="itemAction.set(null); loadItems()" />
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
      .head h1 { font-size: 1.5rem; font-weight: 600; } .meta { display: flex; flex-wrap: wrap; align-items: center; gap: .6rem; margin-top: .35rem; }
      .actions-bar { display: flex; flex-wrap: wrap; gap: .5rem; } .alert { margin-bottom: 1rem; }
      h2 { font-size: 1.05rem; margin: 1.25rem 0 .6rem; } h2:first-child { margin-top: 0; }
      .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem 2rem; margin: 0; }
      dt { color: var(--vms-muted); font-size: .8rem; } dd { margin: .1rem 0 0; font-weight: 500; }
      table { width: 100%; border-collapse: collapse; margin-top: .75rem; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .small { font-size: .8rem; } .row-actions { white-space: nowrap; text-align: right; }
    `,
  ],
})
export class VehicleDetailComponent {
  private readonly api = inject(VehiclesApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);

  readonly vehicle = signal<Vehicle | null>(null);
  readonly loadError = signal<string | null>(null);
  readonly tab = signal<'details' | 'ownership' | 'items' | 'finance' | 'charges' | 'documents' | 'driver' | 'history'>('details');
  /** Bumped when something was paid or reversed on the Finance tab, so the summary above the tabs asks again. */
  readonly financeVersion = signal(0);
  readonly items = signal<AttachedItem[]>([]);
  readonly itemsLoading = signal(false);
  readonly odometer = signal<OdometerReading[]>([]);

  readonly statusOpen = signal(false);
  readonly categoryOpen = signal(false);
  readonly disposeOpen = signal(false);
  readonly assignOpen = signal(false);
  readonly attachOpen = signal(false);
  readonly odoOpen = signal<number | null>(null);
  readonly itemAction = signal<ItemAction | null>(null);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => Number(p.get('id')))), { initialValue: 0 });
  protected readonly words = words;

  readonly draft = computed(() => isDraft(this.vehicle()?.status ?? ''));
  readonly fleet = computed(() => isInFleet(this.vehicle()?.status ?? ''));
  readonly disposed = computed(() => ['Sold', 'Transferred'].includes(this.vehicle()?.status ?? ''));
  readonly canEdit = computed(() => this.auth.hasPermission('VEH.EDIT') && !this.disposed());
  readonly canStatus = computed(() => this.auth.hasPermission('VEH.STATUS.CHANGE') && statusMoves(this.vehicle()?.status ?? '').length > 0);
  readonly canCategory = computed(() => this.auth.hasPermission('VEH.CATEGORY.CHANGE') && this.fleet());
  readonly canDisposeIt = computed(() => this.auth.hasPermission('VEH.DISPOSE') && canDispose(this.vehicle()?.status ?? ''));
  readonly canItems = computed(() => this.auth.hasPermission('VEH.ITEM.MANAGE') && this.fleet());
  readonly canDriver = computed(() => this.auth.hasPermission('VEH.DRIVER.ASSIGN'));
  readonly canSeeDocuments = computed(() => this.auth.hasPermission('DOC.VIEW'));

  /** The vehicle's fields as label and value rows for the Details tab. */
  readonly detailRows = computed<[string, string][]>(() => {
    const v = this.vehicle();
    if (!v) return [];
    const n = (x: number | null | undefined, unit = ''): string => (x === null || x === undefined ? '' : `${x}${unit}`);
    return [
      ['Registration number', v.registrationNo], ['Chassis number', v.chassisNo ?? ''], ['Engine number', v.engineNo ?? ''], ['Model', v.model], ['Year', n(v.manufacturingYear)], ['Colour', v.colour ?? ''],
      ['Fuel type', v.fuelType], ['Tank capacity', n(v.tankCapacity, ' litres')], ['Load capacity', v.loadCapacity === null || v.loadCapacity === undefined ? '' : `${v.loadCapacity} ${v.capacityUnit ?? ''}`],
      ['Tyres', n(v.tyreCount)], ['GVW', n(v.gvw, ' kg')], ['Opening odometer', v.openingOdometer === null || v.openingOdometer === undefined ? '' : `${v.openingOdometer} km (${v.openingOdometerDate ?? ''})`],
      ['Fuel card', v.fuelCardNumber ? `${v.fuelCardNumber} (${v.fuelCardCompany?.name ?? ''})` : ''], ['Tracker', v.trackerCompany ? `${v.trackerCompany.name}${v.trackerDeviceId ? ' · ' + v.trackerDeviceId : ''}` : ''],
      ['Acquired', v.acquisitionDate ? `${v.acquisitionDate}${v.acquisitionType ? ' · ' + words(v.acquisitionType) : ''}` : ''], ['Remarks', v.remarks ?? ''],
    ];
  });

  constructor() {
    effect(() => { this.id(); untracked(() => this.load()); });
    effect(() => {
      const t = this.tab();
      untracked(() => { if (t === 'items' && this.items().length === 0) this.loadItems(); if (t === 'driver') this.loadOdometer(); });
    });
  }

  /** The terms of the current arrangement, only the ones it has (and the caller may see: an absent amount is not drawn). */
  relationRows(r: NonNullable<Vehicle['relation']>): [string, string][] {
    const rows: [string, string | null | undefined][] = [
      ['Our share', r.sharePercent === null || r.sharePercent === undefined ? null : `${r.sharePercent}%`], ['Basis', r.sharingBasis ? words(r.sharingBasis) : null],
      ['Fixed monthly amount', r.fixedMonthlyAmount === null || r.fixedMonthlyAmount === undefined ? null : `PKR ${r.fixedMonthlyAmount}`], ['Expenses', r.expenseSharingRule ? words(r.expenseSharingRule) : null],
      ['Rent', r.rentAmount === null || r.rentAmount === undefined ? null : `PKR ${r.rentAmount} ${r.rentFrequency ? words(r.rentFrequency).toLowerCase() : ''}`], ['Rent due day', r.rentDueDay ? String(r.rentDueDay) : null],
      ['Security deposit', r.securityDeposit === null || r.securityDeposit === undefined ? null : `PKR ${r.securityDeposit}`], ['Arrangement', r.arrangementType ? words(r.arrangementType) : null],
      ['Agreed amount', r.agreedAmount === null || r.agreedAmount === undefined ? null : `PKR ${r.agreedAmount}`], ['Revenue share', r.revenueSharePercent === null || r.revenueSharePercent === undefined ? null : `${r.revenueSharePercent}%`],
      ['Agreement', r.agreementReference],
    ];
    return rows.filter((row): row is [string, string] => !!row[1]);
  }

  load(): void {
    const id = this.id();
    if (!id) return;
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (v) => this.vehicle.set(v),
      error: (err: unknown) => this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This vehicle was not found. It may belong to another company, or the link is wrong.' : errorMessage(err, 'The vehicle could not be loaded.')),
    });
    this.loadItems();
    this.loadOdometer();
  }

  loadItems(): void {
    const id = this.id();
    if (!id) return;
    this.itemsLoading.set(true);
    this.api.items(id, true).subscribe({ next: (i) => { this.itemsLoading.set(false); this.items.set(i); }, error: () => this.itemsLoading.set(false) });
  }

  loadOdometer(): void {
    const id = this.id();
    if (id) this.api.odometer(id).subscribe({ next: (o) => this.odometer.set(o), error: () => undefined });
  }

  /** After any action that returns the vehicle as it is now. */
  changed(v: Vehicle): void {
    this.statusOpen.set(false); this.categoryOpen.set(false); this.disposeOpen.set(false); this.assignOpen.set(false);
    this.vehicle.set(v);
    this.loadOdometer();
  }

  release(): void {
    const v = this.vehicle();
    if (!v) return;
    this.api.releaseDriver(v.id).subscribe({ next: (r) => { this.notify.success('Driver released'); this.vehicle.set(r); }, error: (err) => this.notify.error(err) });
  }

  protected goList(): void {
    void this.router.navigate(['/vehicles']);
  }
}
