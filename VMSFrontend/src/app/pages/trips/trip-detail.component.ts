import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { map } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { TripsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { TripModel } from '../../core/trip.models';
import { ConfirmService } from '../../shared/confirm.service';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { TripDocumentsTabComponent } from './trip-documents-tab.component';
import { TripExpensesTabComponent } from './trip-expenses-tab.component';
import { TripFuelTabComponent } from './trip-fuel-tab.component';
import { TripHistoryTabComponent } from './trip-history-tab.component';
import { TripIncomeTabComponent } from './trip-income-tab.component';
import { TripIssuesTabComponent } from './trip-issues-tab.component';
import { TripPnLTabComponent } from './trip-pnl-tab.component';
import { TripTimelineTabComponent } from './trip-timeline-tab.component';
import { TripTransitionDialogComponent } from './trip-transition-dialog.component';
import { canCancel, canHold, cancelNeedsElevatedPermission, nextMoves, statusSeverity, words } from './trip-logic';

type Tab = 'timeline' | 'stops' | 'fuel' | 'expenses' | 'income' | 'documents' | 'issues' | 'pnl' | 'history';

/**
 * Trip Details (FSD §21-§31, §48.4 screen 14): header with the trip's own identity and status, next-status
 * buttons plus Hold/Resume/Cancel/Reopen/Inactivate/Reactivate, and every operational tab. There is no Edit
 * action on this screen — §33/§46.5 make an invoiced trip's own amount locked, and nothing in this cluster's own
 * scope (§21/§22) describes editing an un-invoiced trip's core fields after creation either; the lock banner
 * below is the one thing this screen says about that.
 */
@Component({
  selector: 'app-trip-detail',
  standalone: true,
  imports: [
    RouterLink, ButtonModule, TabsModule, TagModule, BusinessDatePipe, ReasonDialogComponent, TripTransitionDialogComponent,
    TripTimelineTabComponent, TripFuelTabComponent, TripExpensesTabComponent, TripIncomeTabComponent, TripDocumentsTabComponent, TripIssuesTabComponent, TripPnLTabComponent, TripHistoryTabComponent,
  ],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/trips">Trip Desk</a> <span aria-hidden="true">/</span> <span>{{ trip()?.tripNumber ?? '…' }}</span></div>

      @if (loadError(); as message) {
        <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div>
      } @else if (trip()) {
        @let t = trip()!;
        <header class="head">
          <div>
            <h1>{{ t.tripNumber }}</h1>
            <div class="meta">
              <span class="chip">{{ words(t.tripType) }}</span> <p-tag [value]="words(t.status)" [severity]="severity(t.status)" />
              @if (!t.isActive) { <span class="chip chip--danger">Inactive</span> }
              <span class="muted">{{ t.routeLabel || '—' }} · {{ t.tripDate | vmsDate }}</span>
            </div>
          </div>
          <div class="actions-bar">
            @if (canStatus()) {
              @for (m of moves(); track m) { <p-button [label]="'Move to ' + words(m)" severity="secondary" [outlined]="true" size="small" (onClick)="transitioning.set(m)" /> }
              @if (canHoldNow()) { <p-button label="Hold" severity="secondary" [outlined]="true" size="small" (onClick)="holding.set(true)" /> }
              @if (t.status === 'OnHold') { <p-button label="Resume" severity="secondary" [outlined]="true" size="small" (onClick)="resume()" /> }
              @if (canCancelNow()) { <p-button label="Cancel trip" severity="danger" [outlined]="true" size="small" (onClick)="cancelling.set(true)" /> }
            }
            @if (canReopen() && t.status === 'Completed') { <p-button label="Reopen" severity="secondary" [outlined]="true" size="small" (onClick)="reopen()" /> }
            @if (canInactivate()) {
              @if (t.isActive) { <p-button label="Inactivate" severity="danger" [outlined]="true" size="small" (onClick)="inactivating.set(true)" /> }
              @else { <p-button label="Reactivate" severity="secondary" [outlined]="true" size="small" (onClick)="reactivate()" /> }
            }
          </div>
        </header>

        @if (t.invoiceId) { <div class="alert info">On invoice #{{ t.invoiceId }}; amount locked.</div> }
        @for (w of t.warnings; track w) { <div class="alert warning">{{ w }}</div> }
        @if (t.rateMissing) { <div class="alert warning">Rate not configured for this trip.</div> }

        <div class="card body">
          <p-tabs [value]="tab()" (valueChange)="tab.set($any($event))" [scrollable]="true">
            <p-tablist>
              <p-tab value="timeline">Timeline</p-tab>
              <p-tab value="stops">Stops</p-tab>
              <p-tab value="fuel">Fuel</p-tab>
              <p-tab value="expenses">Expenses</p-tab>
              <p-tab value="income">Income</p-tab>
              @if (canSeeDocuments()) { <p-tab value="documents">Documents/POD</p-tab> }
              <p-tab value="issues">Issues</p-tab>
              <p-tab value="pnl">P&amp;L</p-tab>
              <p-tab value="history">History</p-tab>
            </p-tablist>
            <p-tabpanels>
              <p-tabpanel value="timeline">@if (tab() === 'timeline') { <app-trip-timeline-tab [tripId]="t.tripId" /> }</p-tabpanel>
              <p-tabpanel value="stops">
                @if (t.tripType === 'Fixed') {
                  <p>{{ t.routeLabel || 'No route.' }}</p>
                } @else {
                  <dl class="grid">
                    <div><dt>From</dt><dd>{{ t.from?.label || '—' }}</dd></div>
                    @for (s of t.stops; track $index) { <div><dt>Stop</dt><dd>{{ s.label }}</dd></div> }
                    <div><dt>To</dt><dd>{{ t.to?.label || '—' }}</dd></div>
                    <div><dt>Round trip</dt><dd>{{ t.isRoundTrip ? 'Yes' : 'No' }}</dd></div>
                  </dl>
                }
              </p-tabpanel>
              <p-tabpanel value="fuel">@if (tab() === 'fuel') { <app-trip-fuel-tab [tripId]="t.tripId" /> }</p-tabpanel>
              <p-tabpanel value="expenses">@if (tab() === 'expenses') { <app-trip-expenses-tab [tripId]="t.tripId" /> }</p-tabpanel>
              <p-tabpanel value="income">@if (tab() === 'income') { <app-trip-income-tab [tripId]="t.tripId" /> }</p-tabpanel>
              @if (canSeeDocuments()) { <p-tabpanel value="documents">@if (tab() === 'documents') { <app-trip-documents-tab [tripId]="t.tripId" /> }</p-tabpanel> }
              <p-tabpanel value="issues">@if (tab() === 'issues') { <app-trip-issues-tab [tripId]="t.tripId" /> }</p-tabpanel>
              <p-tabpanel value="pnl">@if (tab() === 'pnl') { <app-trip-pnl-tab [tripId]="t.tripId" /> }</p-tabpanel>
              <p-tabpanel value="history">@if (tab() === 'history') { <app-trip-history-tab [tripId]="t.tripId" [version]="t.rowVersion" /> }</p-tabpanel>
            </p-tabpanels>
          </p-tabs>
        </div>
      } @else { <p class="muted">Loading…</p> }
    </div>

    <app-trip-transition-dialog [trip]="transitioning() ? trip() : null" [toStatus]="transitioning() ?? ''" (closed)="transitioning.set(null)" (changed)="changed($event)" />
    <vms-reason-dialog [open]="holding()" title="Hold trip" confirmLabel="Hold" [minLength]="1" [busy]="actionBusy()" (closed)="holding.set(false)" (confirmed)="doHold($event)" />
    <vms-reason-dialog [open]="cancelling()" title="Cancel trip" confirmLabel="Cancel trip" [minLength]="10" [busy]="actionBusy()" (closed)="cancelling.set(false)" (confirmed)="doCancel($event)" />
    <vms-reason-dialog [open]="inactivating()" title="Inactivate trip" confirmLabel="Inactivate" [minLength]="10" [busy]="actionBusy()" (closed)="inactivating.set(false)" (confirmed)="doInactivate($event)" />
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; }
      .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
      .head h1 { font-size: 1.5rem; font-weight: 600; } .meta { display: flex; flex-wrap: wrap; gap: .6rem; align-items: center; margin-top: .35rem; }
      .actions-bar { display: flex; flex-wrap: wrap; gap: .5rem; } .alert { margin-bottom: 1rem; } .body { padding-bottom: 1.5rem; }
      .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 1rem 2rem; margin: 0; }
      dt { color: var(--vms-muted); font-size: .8rem; } dd { margin: .1rem 0 0; font-weight: 500; }
    `,
  ],
})
export class TripDetailComponent {
  private readonly api = inject(TripsApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);

  readonly trip = signal<TripModel | null>(null);
  readonly loadError = signal<string | null>(null);
  readonly tab = signal<Tab>('timeline');
  protected readonly words = words;
  protected readonly severity = statusSeverity;

  readonly transitioning = signal<string | null>(null);
  readonly holding = signal(false);
  readonly cancelling = signal(false);
  readonly inactivating = signal(false);
  readonly actionBusy = signal(false);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => Number(p.get('id')))), { initialValue: 0 });

  protected readonly canStatus = computed(() => this.auth.hasPermission('TRP.TRIP.STATUS'));
  protected readonly canReopen = computed(() => this.auth.hasPermission('TRP.TRIP.REOPEN'));
  protected readonly canInactivate = computed(() => this.auth.hasPermission('TRP.TRIP.INACTIVATE'));
  protected readonly canSeeDocuments = computed(() => this.auth.hasPermission('TRP.TRIP.DOCUMENTS'));
  protected readonly moves = computed(() => nextMoves(this.trip()?.status ?? ''));
  protected readonly canHoldNow = computed(() => canHold(this.trip()?.status ?? ''));
  protected readonly canCancelNow = computed(() => {
    const status = this.trip()?.status ?? '';
    if (!canCancel(status)) return false;
    return cancelNeedsElevatedPermission(status) ? this.auth.hasPermission('TRP.TRIP.STATUS') : true;
  });

  constructor() {
    this.route.paramMap.subscribe(() => this.load());
  }

  load(): void {
    const id = this.id();
    if (!id) return;
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: (t) => this.trip.set(t),
      error: (err: unknown) => this.loadError.set(err instanceof HttpErrorResponse && err.status === 404 ? 'This trip was not found.' : errorMessage(err, 'The trip could not be loaded.')),
    });
  }

  protected changed(t: TripModel): void {
    this.transitioning.set(null);
    this.trip.set(t);
  }

  protected resume(): void {
    const t = this.trip();
    if (!t) return;
    this.api.resume(t.tripId, { rowVersion: t.rowVersion }).subscribe({ next: (r) => this.changed(r), error: (err) => this.notify.error(err) });
  }

  protected reopen(): void {
    const t = this.trip();
    if (!t) return;
    void this.confirm.ask({ title: 'Reopen trip', message: 'Move this Completed trip back to Delivered?', confirmLabel: 'Reopen', cancelLabel: 'Cancel', icon: 'pi pi-replay' }).then((go) => {
      if (!go) return;
      this.api.reopen(t.tripId, { rowVersion: t.rowVersion }).subscribe({ next: (r) => this.changed(r), error: (err) => this.notify.error(err) });
    });
  }

  protected reactivate(): void {
    const t = this.trip();
    if (!t) return;
    void this.confirm.ask({ title: 'Reactivate trip', message: 'Reactivate this trip?', confirmLabel: 'Reactivate', cancelLabel: 'Cancel', icon: 'pi pi-refresh' }).then((go) => {
      if (!go) return;
      this.api.reactivate(t.tripId, { rowVersion: t.rowVersion }).subscribe({ next: (r) => this.changed(r), error: (err) => this.notify.error(err) });
    });
  }

  protected doHold(reason: string): void {
    const t = this.trip();
    if (!t) return;
    this.actionBusy.set(true);
    this.api.hold(t.tripId, { reason, rowVersion: t.rowVersion }).subscribe({
      next: (r) => { this.actionBusy.set(false); this.holding.set(false); this.changed(r); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }

  protected doCancel(reason: string): void {
    const t = this.trip();
    if (!t) return;
    this.actionBusy.set(true);
    this.api.cancel(t.tripId, { reason, rowVersion: t.rowVersion }).subscribe({
      next: (r) => { this.actionBusy.set(false); this.cancelling.set(false); this.changed(r); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }

  protected doInactivate(reason: string): void {
    const t = this.trip();
    if (!t) return;
    this.actionBusy.set(true);
    this.api.inactivate(t.tripId, { reason, rowVersion: t.rowVersion }).subscribe({
      next: (r) => { this.actionBusy.set(false); this.inactivating.set(false); this.changed(r); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }
}
