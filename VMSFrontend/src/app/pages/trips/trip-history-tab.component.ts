import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TripsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { TripHistory } from '../../core/trip.models';

const ENTITY_LABELS: Record<string, string> = {
  Trip: 'Trip', TripStop: 'Stop', TripEvent: 'Event', TripFuel: 'Fuel', TripExpense: 'Expense', TripIncome: 'Income',
  TripDocument: 'Document', TripPOD: 'Proof of delivery', TripIssue: 'Issue', TripRateHistory: 'Re-pricing',
};
const entityLabel = (entity: string): string => ENTITY_LABELS[entity] ?? entity;

const words = (value: string | null | undefined): string => {
  if (!value) return '';
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
};

interface ChangeLike { entity: string; action: string; field?: string | null; oldValue?: string | null; newValue?: string | null; restricted: boolean }

function describeChange(c: ChangeLike): string {
  const what = entityLabel(c.entity);
  const show = (v: string | null | undefined): string => (v === null || v === undefined || v === '' ? '(empty)' : v);
  if (c.action === 'Created') return `Added ${what.toLowerCase()}`;
  if (c.action === 'Deleted') return `Removed ${what.toLowerCase()}`;
  const label = c.entity === 'Trip' ? words(c.field ?? '') : `${what} ${words(c.field ?? '').toLowerCase()}`;
  return c.restricted ? `${label} changed (values hidden)` : `${label}: ${show(c.oldValue)} → ${show(c.newValue)}`;
}

/** History tab (FSD §48.1, screen 14): every audited change to the trip and to any of its own child rows
 * (events, fuel, expenses, income, documents, POD, issues, rate history — all root back to "Trip"), grouped by
 * the save that produced them, newest first. Mirrors `VehicleHistoryTabComponent`/`CustomerHistoryTabComponent`. */
@Component({
  selector: 'app-trip-history-tab',
  standalone: true,
  imports: [ButtonModule, InstantPipe],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load(1)" /></div> }
    @if (groups().length === 0) {
      <p class="muted">{{ loading() ? 'Loading…' : 'Nothing recorded yet.' }}</p>
    } @else {
      @for (g of groups(); track g.id) {
        <div class="group">
          <div class="group-head"><strong>{{ g.at | vmsInstant }}</strong> <span class="muted">{{ g.who }}</span></div>
          <ul>@for (c of g.rows; track c.id) { <li><span class="muted">{{ entity(c.entity) }}:</span> {{ describe(c) }}@if (c.reason) { <span class="muted"> — {{ c.reason }}</span> }</li> }</ul>
        </div>
      }
      @if (changes().length < total()) { <p-button label="Show older changes" severity="secondary" [outlined]="true" [loading]="loading()" (onClick)="load(page() + 1)" /> }
    }
  `,
  styles: [
    `
      .group { border: 1px solid var(--vms-border); border-radius: var(--vms-radius); padding: .6rem .9rem; margin-bottom: .6rem; }
      .group-head { margin-bottom: .25rem; } .group ul { margin: 0; padding-left: 1.1rem; font-size: .875rem; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class TripHistoryTabComponent {
  private readonly api = inject(TripsApi);

  readonly tripId = input.required<number>();
  readonly version = input('');
  readonly history = signal<TripHistory | null>(null);
  readonly changes = signal<TripHistory['changes']['items']>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  protected readonly entity = entityLabel;
  protected readonly describe = describeChange;

  protected readonly groups = computed(() => {
    const out: { id: string; at: string; who: string; rows: TripHistory['changes']['items'] }[] = [];
    for (const c of this.changes()) {
      const last = out[out.length - 1];
      if (last && last.id === c.groupId) last.rows.push(c);
      else out.push({ id: c.groupId, at: c.occurredAt, who: c.userName || 'System', rows: [c] });
    }
    return out;
  });

  constructor() {
    effect(() => { this.tripId(); this.version(); untracked(() => this.load(1)); });
  }

  protected load(page: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.history(this.tripId(), page).subscribe({
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
