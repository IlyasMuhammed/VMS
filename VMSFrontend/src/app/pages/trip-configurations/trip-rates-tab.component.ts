import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripConfigurationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { TripRateModel } from '../../core/trip-configuration.models';
import { ConfirmService } from '../../shared/confirm.service';
import { TripRateDialogComponent, TripRateSplitDialogComponent } from './trip-rate-dialogs.component';
import { findGaps, hasOpenEndedCoverage, statusSeverity } from './trip-configuration-logic';

/**
 * Trip Rates tab (FSD §26, §48.3 screen 11): effective-dated rates for this configuration. Stands in for the
 * FSD's "calendar strip showing covered/uncovered days" with the same information reduced to its gaps
 * (`findGaps`) — see `trip-configuration-logic.ts` for why.
 */
@Component({
  selector: 'app-trip-rates-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, BusinessDatePipe, TripRateDialogComponent, TripRateSplitDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

    @if (!openEnded() && rates().length > 0) { <div class="alert warning">This configuration has no open-ended rate — trips after the last covered date will be RateMissing.</div> }
    @for (g of gaps(); track g.from) { <div class="alert warning">No rate for {{ g.from | vmsDate }} to {{ g.to | vmsDate }}.</div> }

    <div class="head-row">
      @if (canConfigure()) {
        <p-button label="Add rate" icon="pi pi-plus" size="small" (onClick)="addOpen.set(true)" />
        <p-button label="Insert one-day rate" [text]="true" size="small" (onClick)="splitOpen.set(true)" />
      }
      @if (canReprice()) { <p-button label="Resolve missing rates" [text]="true" size="small" (onClick)="resolveMissing()" [loading]="resolving()" /> }
    </div>

    <table>
      <thead><tr><th>Effective</th><th>Rate</th><th>Currency</th><th>Status</th><th>Remarks</th><th></th></tr></thead>
      <tbody>
        @for (r of rates(); track r.tripRateId) {
          <tr>
            <td class="nowrap">{{ r.effectiveFrom | vmsDate }}@if (r.effectiveTo) { – {{ r.effectiveTo | vmsDate }} } @else { <span class="muted"> (open-ended)</span> }</td>
            <td>{{ r.rateAmount }}</td><td>{{ r.currencyCode }}</td>
            <td><p-tag [value]="r.status" [severity]="severity(r.status)" /></td>
            <td>{{ r.remarks || '—' }}</td>
            <td class="row-actions">
              @if (canConfigure()) {
                <p-button label="Edit" [text]="true" size="small" (onClick)="editing.set(r)" />
                @if (r.status === 'Active') { <p-button label="Inactivate" [text]="true" size="small" severity="danger" (onClick)="inactivate(r)" /> }
              }
            </td>
          </tr>
        }
        @if (rates().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'No rates yet — trips on this configuration will be RateMissing until one is added.' }}</td></tr> }
      </tbody>
    </table>

    <app-trip-rate-dialog [configurationId]="configurationId()" [adding]="addOpen()" [rate]="editing()" (closed)="addOpen.set(false); editing.set(null)" (saved)="addOpen.set(false); editing.set(null); load()" />
    <app-trip-rate-split-dialog [configurationId]="configurationId()" [adding]="splitOpen()" (closed)="splitOpen.set(false)" (saved)="splitOpen.set(false); load()" />
  `,
  styles: [
    `
      .head-row { margin-bottom: .75rem; display: flex; gap: .5rem; flex-wrap: wrap; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: .75rem; }
    `,
  ],
})
export class TripRatesTabComponent {
  private readonly api = inject(TripConfigurationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);

  readonly configurationId = input.required<number>();
  readonly customerId = input.required<number>();
  protected readonly rates = signal<TripRateModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly resolving = signal(false);
  protected readonly canConfigure = () => this.auth.hasPermission('TRP.RATE.CONFIGURE');
  protected readonly canReprice = () => this.auth.hasPermission('TRP.RATE.REPRICE');
  protected readonly severity = statusSeverity;
  protected readonly gaps = () => findGaps(this.rates());
  protected readonly openEnded = () => hasOpenEndedCoverage(this.rates());

  protected readonly addOpen = signal(false);
  protected readonly splitOpen = signal(false);
  protected readonly editing = signal<TripRateModel | null>(null);

  constructor() {
    effect(() => { this.configurationId(); untracked(() => this.load()); });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.rates(this.configurationId(), true).subscribe({
      next: (r) => { this.loading.set(false); this.rates.set(r); },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Rates could not be loaded.')); },
    });
  }

  protected inactivate(r: TripRateModel): void {
    void this.confirm.ask({ title: 'Inactivate rate', message: `Inactivate the rate effective ${r.effectiveFrom}? Its range becomes free for a new rate.`, confirmLabel: 'Inactivate', cancelLabel: 'Keep it', icon: 'pi pi-ban' }).then((go) => {
      if (!go) return;
      this.api.inactivateRate(r.tripRateId, { rowVersion: r.rowVersion }).subscribe({ next: () => this.load(), error: (err) => this.notify.error(err) });
    });
  }

  protected resolveMissing(): void {
    this.resolving.set(true);
    this.api.resolveMissingRates({ customerId: this.customerId(), tripConfigurationId: this.configurationId() }).subscribe({
      next: (r) => {
        this.resolving.set(false);
        this.notify.success(`${r.updated} of ${r.considered} trip(s) re-priced. ${r.stillMissing} still missing a rate.`);
      },
      error: (err) => { this.resolving.set(false); this.notify.error(err); },
    });
  }
}
