import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { CustomersApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { CustomerHistory } from '../../core/customer.models';
import { describeChange, entityLabel } from './customer-logic';

/** Every change to this customer or its child records (FSD §48.1: "History button on every record"). Read-only.
 * Unlike Vehicle, there is no separate lifecycle list — a status change already shows as an ordinary Status
 * field-change row, so one grouped list is all this tab needs. */
@Component({
  selector: 'app-customer-history-tab',
  standalone: true,
  imports: [ButtonModule, InstantPipe],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load(1)" /></div> }
    @if (groups().length === 0) {
      <p class="muted">{{ loading() ? 'Loading…' : 'Nothing recorded yet.' }}</p>
    } @else {
      <!-- Everything one save did is one group, so a reviewer sees the whole change at once. -->
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
export class CustomerHistoryTabComponent {
  private readonly api = inject(CustomersApi);

  readonly customerId = input.required<number>();
  readonly version = input('');
  readonly history = signal<CustomerHistory | null>(null);
  readonly changes = signal<CustomerHistory['changes']['items']>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  protected readonly entity = entityLabel;
  protected readonly describe = describeChange;

  protected readonly groups = computed(() => {
    const out: { id: string; at: string; who: string; rows: CustomerHistory['changes']['items'] }[] = [];
    for (const c of this.changes()) {
      const last = out[out.length - 1];
      if (last && last.id === c.groupId) last.rows.push(c);
      else out.push({ id: c.groupId, at: c.occurredAt, who: c.userName || 'System', rows: [c] });
    }
    return out;
  });

  constructor() {
    effect(() => { this.customerId(); this.version(); untracked(() => this.load(1)); });
  }

  protected load(page: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.history(this.customerId(), page).subscribe({
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
