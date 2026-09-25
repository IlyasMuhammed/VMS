import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { FuelCardsApi, PartnersApi, VehiclesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { FuelCardAssignmentModel, FuelCardModel } from '../../core/fuel-card.models';
import { FuelCardAssignDialogComponent } from './fuel-card-assign-dialog.component';
import { FuelCardDialogComponent } from './fuel-card-dialog.component';
import { isExpiringSoon, isPastExpiry, statusSeverity } from './fuel-card-logic';

/** Fuel Card list (FSD §28, §48.3 screen 17): "List + form + assignment history" — all on one screen, since
 * there is nowhere else a fuel card is shown (unlike Customer or Vehicle, it has no other detail page). Partner
 * and vehicle names are resolved lazily, once per unique id, into local caches — there is no bulk "names for
 * these ids" endpoint, and a card list is small enough that a handful of one-off lookups is not a real cost. */
@Component({
  selector: 'app-fuel-cards',
  standalone: true,
  imports: [FormsModule, ButtonModule, InputTextModule, TagModule, DialogModule, BusinessDatePipe, FuelCardDialogComponent, FuelCardAssignDialogComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Fuel Cards</h1><div class="sub">Cards, their issuing company, and which vehicle or driver holds each one.</div></div>
        <div class="header-actions">
          @if (canEdit()) { <p-button label="Add fuel card" icon="pi pi-plus" (onClick)="addOpen.set(true)" /> }
        </div>
      </div>

      @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

      <div class="card">
        <div class="toolbar">
          <input pInputText [ngModel]="search()" (ngModelChange)="search.set($event)" placeholder="Search card holder or company" style="max-width: 22rem" />
        </div>

        <table>
          <thead><tr><th>Card</th><th>Company</th><th>Holder</th><th>Assigned to</th><th>Expiry</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (c of filtered(); track c.fuelCardId) {
              <tr>
                <td class="mono">{{ c.maskedCardNumber }}</td><td>{{ companyName(c.fuelCardCompanyId) }}</td><td>{{ c.cardHolderName || '—' }}</td>
                <td>{{ assignedToText(c) }}</td>
                <td class="nowrap">{{ c.expiryDate | vmsDate }}
                  @if (isPast(c.expiryDate)) { <p-tag value="Expired" severity="warn" /> }
                  @else if (isSoon(c.expiryDate)) { <p-tag value="Expiring soon" severity="warn" /> }
                </td>
                <td><p-tag [value]="c.status" [severity]="severity(c.status)" /></td>
                <td class="row-actions">
                  @if (canEdit()) {
                    <p-button label="Edit" [text]="true" size="small" (onClick)="editing.set(c)" />
                    <p-button label="Assign" [text]="true" size="small" (onClick)="assigning.set(c)" />
                  }
                  <p-button label="History" [text]="true" size="small" (onClick)="showHistory(c)" />
                </td>
              </tr>
            }
            @if (filtered().length === 0) { <tr><td colspan="7" class="muted">{{ loading() ? 'Loading…' : 'No fuel cards found.' }}</td></tr> }
          </tbody>
        </table>
      </div>
    </div>

    <app-fuel-card-dialog [adding]="addOpen()" [card]="editing()" (closed)="addOpen.set(false); editing.set(null)" (saved)="addOpen.set(false); editing.set(null); load()" />
    <app-fuel-card-assign-dialog [card]="assigning()" (closed)="assigning.set(null)" (saved)="assigning.set(null); load()" />

    <p-dialog [visible]="!!historyFor()" (visibleChange)="!$event && historyFor.set(null)" [modal]="true" [style]="{ width: '560px' }" [header]="'Assignment history — ' + (historyFor()?.maskedCardNumber ?? '')">
      <table>
        <thead><tr><th>From</th><th>To</th><th>Vehicle</th><th>Driver</th><th>Reason</th></tr></thead>
        <tbody>
          @for (a of history(); track a.fuelCardAssignmentId) {
            <tr>
              <td class="nowrap">{{ a.assignedFrom | vmsDate }}</td><td class="nowrap">{{ a.assignedTo ? (a.assignedTo | vmsDate) : 'Current' }}</td>
              <td>{{ a.vehicleId ? vehicleName(a.vehicleId) : '—' }}</td><td>{{ a.driverId ? companyName(a.driverId) : '—' }}</td><td>{{ a.reason || '—' }}</td>
            </tr>
          }
          @if (history().length === 0) { <tr><td colspan="5" class="muted">{{ historyLoading() ? 'Loading…' : 'No assignments yet.' }}</td></tr> }
        </tbody>
      </table>
    </p-dialog>
  `,
  styles: [
    `
      .header-actions { display: flex; gap: .5rem; }
      .toolbar { display: flex; align-items: center; gap: 1rem; margin-bottom: .75rem; flex-wrap: wrap; }
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class FuelCardsComponent {
  private readonly api = inject(FuelCardsApi);
  private readonly partners = inject(PartnersApi);
  private readonly vehicles = inject(VehiclesApi);
  private readonly auth = inject(AuthService);

  readonly canEdit = computed(() => this.auth.hasPermission('TRP.FUELCARD.EDIT'));
  readonly cards = signal<FuelCardModel[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly search = signal('');

  readonly addOpen = signal(false);
  readonly editing = signal<FuelCardModel | null>(null);
  readonly assigning = signal<FuelCardModel | null>(null);
  readonly historyFor = signal<FuelCardModel | null>(null);
  readonly history = signal<FuelCardAssignmentModel[]>([]);
  readonly historyLoading = signal(false);

  protected readonly severity = statusSeverity;
  private readonly today = new Date().toISOString().slice(0, 10);
  protected readonly isPast = (d: string) => isPastExpiry(d, this.today);
  protected readonly isSoon = (d: string) => isExpiringSoon(d, this.today);

  private readonly partnerNames = signal<Map<number, string>>(new Map());
  private readonly vehicleNames = signal<Map<number, string>>(new Map());

  readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();
    if (!term) return this.cards();
    return this.cards().filter((c) => (c.cardHolderName ?? '').toLowerCase().includes(term) || this.companyName(c.fuelCardCompanyId).toLowerCase().includes(term));
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list().subscribe({
      next: (rows) => {
        this.loading.set(false);
        this.cards.set(rows);
        this.resolveNames(
          rows.flatMap((r) => [r.fuelCardCompanyId, r.driverId ?? null]).filter((id): id is number => id !== null),
          rows.map((r) => r.vehicleId).filter((id): id is number => id !== null && id !== undefined),
        );
      },
      error: (err) => { this.loading.set(false); this.error.set(errorMessage(err, 'Fuel cards could not be loaded.')); },
    });
  }

  private resolveNames(partnerIdList: number[], vehicleIdList: number[]): void {
    const partnerIds = new Set(partnerIdList);
    const vehicleIds = new Set(vehicleIdList);
    for (const id of partnerIds) {
      if (this.partnerNames().has(id)) continue;
      this.partners.get(id).subscribe({
        next: (p) => this.partnerNames.update((m) => new Map(m).set(id, p.displayName || p.legalName)),
        error: () => this.partnerNames.update((m) => new Map(m).set(id, `#${id}`)),
      });
    }
    for (const id of vehicleIds) {
      if (this.vehicleNames().has(id)) continue;
      this.vehicles.get(id).subscribe({
        next: (v) => this.vehicleNames.update((m) => new Map(m).set(id, v.registrationNo)),
        error: () => this.vehicleNames.update((m) => new Map(m).set(id, `#${id}`)),
      });
    }
  }

  protected companyName(id: number): string {
    return this.partnerNames().get(id) ?? '…';
  }

  protected vehicleName(id: number): string {
    return this.vehicleNames().get(id) ?? '…';
  }

  protected assignedToText(c: FuelCardModel): string {
    const parts: string[] = [];
    if (c.vehicleId) parts.push(this.vehicleName(c.vehicleId));
    if (c.driverId) parts.push(this.companyName(c.driverId));
    return parts.length > 0 ? parts.join(' / ') : '—';
  }

  protected showHistory(c: FuelCardModel): void {
    this.historyFor.set(c);
    this.historyLoading.set(true);
    this.history.set([]);
    this.api.assignments(c.fuelCardId).subscribe({
      next: (rows) => {
        this.historyLoading.set(false);
        this.history.set(rows);
        this.resolveNames(rows.map((r) => r.driverId).filter((id): id is number => id !== null && id !== undefined), rows.map((r) => r.vehicleId).filter((id): id is number => id !== null && id !== undefined));
      },
      error: () => this.historyLoading.set(false),
    });
  }
}
