import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { PartnersApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe, InstantPipe } from '../../core/datetime/datetime.pipes';
import { HistoryChange, RoleLogItem, StatusLogItem } from '../../core/partner.models';
import { describeChange, entityLabel, roleLabel } from './partner-logic';

/**
 * What happened to a partner (FSD §9.2): every change with who made it and when, and the two logs the FSD asks for by name,
 * roles added or removed and status changes with their reasons. Read-only: the audit trail is append-only (BR-BP-022).
 */
@Component({
  selector: 'app-history-tab',
  standalone: true,
  imports: [ButtonModule, InstantPipe, BusinessDatePipe],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load(1)" /></div> }

    <section>
      <h2>Changes</h2>
      @if (changes().length === 0) {
        <p class="muted">{{ loading() ? 'Loading…' : 'Nothing has been recorded yet.' }}</p>
      } @else {
        <table>
          <thead><tr><th>When</th><th>Who</th><th>Record</th><th>What changed</th></tr></thead>
          <tbody>
            @for (c of changes(); track c.id) {
              <tr>
                <td class="nowrap">{{ c.occurredAt | vmsInstant }}</td>
                <td>{{ c.userName || 'System' }}</td>
                <td>{{ entity(c.entity) }}</td>
                <td>{{ describe(c) }}@if (c.reason) { <span class="muted"> — {{ c.reason }}</span> }</td>
              </tr>
            }
          </tbody>
        </table>
        @if (changes().length < total()) {
          <p-button label="Show older changes" severity="secondary" [outlined]="true" [loading]="loading()" (onClick)="load(page() + 1)" />
        }
      }
    </section>

    <section>
      <h2>Role changes</h2>
      @if (roles().length === 0) { <p class="muted">No role has been added or removed since the partner was created.</p> }
      @else {
        <table>
          <thead><tr><th>When</th><th>Who</th><th>Role</th><th>Change</th><th>Effective</th><th>Reason</th></tr></thead>
          <tbody>
            @for (r of roles(); track $index) {
              <tr>
                <td class="nowrap">{{ r.occurredOn | vmsInstant }}</td><td>{{ r.userName || 'System' }}</td><td>{{ roleName(r.roleCode) }}</td>
                <td>{{ r.action }}</td><td class="nowrap">{{ r.effectiveDate | vmsDate }}</td><td>{{ r.reason || '—' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>

    <section>
      <h2>Status changes</h2>
      @if (statuses().length === 0) { <p class="muted">The status has not changed since the partner was created.</p> }
      @else {
        <table>
          <thead><tr><th>When</th><th>Who</th><th>From</th><th>To</th><th>Effective</th><th>Reason</th></tr></thead>
          <tbody>
            @for (s of statuses(); track $index) {
              <tr>
                <td class="nowrap">{{ s.occurredOn | vmsInstant }}</td><td>{{ s.userName || 'System' }}</td><td>{{ s.fromStatus }}</td><td>{{ s.toStatus }}</td>
                <td class="nowrap">{{ s.effectiveDate | vmsDate }}</td><td>{{ s.reason || '—' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
  styles: [
    `
      section { margin-bottom: 2rem; }
      h2 { font-size: 1.05rem; margin-bottom: .6rem; }
      table { width: 100%; border-collapse: collapse; }
      th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; }
      th { color: var(--vms-muted); font-weight: 600; }
      .nowrap { white-space: nowrap; }
    `,
  ],
})
export class HistoryTabComponent {
  private readonly api = inject(PartnersApi);

  /** The partner to show the history of. It reloads when this changes, and whenever `version` does (after a save). */
  readonly partnerId = input.required<number>();
  readonly version = input<string>('');

  protected readonly changes = signal<HistoryChange[]>([]);
  protected readonly roles = signal<RoleLogItem[]>([]);
  protected readonly statuses = signal<StatusLogItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly entity = entityLabel;
  protected readonly roleName = roleLabel;
  protected readonly describe = describeChange;

  constructor() {
    effect(() => {
      this.partnerId();
      this.version();
      untracked(() => this.load(1));
    });
  }

  protected load(page: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.history(this.partnerId(), page).subscribe({
      next: (h) => {
        this.loading.set(false);
        this.page.set(page);
        this.total.set(h.changes.totalCount);
        this.changes.update((current) => (page === 1 ? h.changes.items : [...current, ...h.changes.items]));
        this.roles.set(h.roles);
        this.statuses.set(h.statuses);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(errorMessage(err, 'The history could not be loaded.'));
      },
    });
  }
}
