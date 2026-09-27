import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TagModule } from 'primeng/tag';
import { InvoicesApi } from '../../core/api.services';
import { BusinessDatePipe, InstantPipe } from '../../core/datetime/datetime.pipes';
import { InvoiceVersionModel } from '../../core/invoice.models';
import { NotifyService } from '../../core/notify.service';
import { statusSeverity, words } from './invoice-logic';

/**
 * Versions tab (FSD §38, §39, §48.5's own literal `GET /api/invoices/{id}/versions`) — the whole regeneration
 * chain this invoice belongs to, oldest first. "Regenerate" itself is a header-level action on Invoice Detail
 * (opens its own wizard, `invoice-regeneration.component.ts`), not a button in here — this tab is read-only.
 */
@Component({
  selector: 'app-invoice-versions-tab',
  standalone: true,
  imports: [RouterLink, TagModule, InstantPipe, BusinessDatePipe],
  template: `
    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else {
      <table>
        <thead><tr><th>Version</th><th>Invoice no.</th><th>Date</th><th>Status</th><th>Net</th><th>Balance</th><th>Regeneration reason</th><th>Regenerated on</th></tr></thead>
        <tbody>
          @for (v of versions(); track v.invoiceId) {
            <tr [class.current]="v.invoiceId === invoiceId()">
              <td>v{{ v.version }}</td>
              <td class="mono"><a [routerLink]="['/invoices', v.invoiceId]">{{ v.invoiceNumber }}</a> @if (!v.isActive) { <span class="muted">(replaced)</span> }</td>
              <td>{{ v.invoiceDate | vmsDate }}</td>
              <td><p-tag [value]="words(v.status)" [severity]="severity(v.status)" /></td>
              <td>{{ v.netAmount }}</td>
              <td>{{ v.balanceAmount }}</td>
              <td>{{ v.regenerationReason || '—' }}</td>
              <td>{{ v.regeneratedOn ? (v.regeneratedOn | vmsInstant) : '—' }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: [
    `
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; }
      th { color: var(--vms-muted); font-weight: 600; } tr.current { background: var(--vms-surface-soft); } .muted { color: var(--vms-muted); }
    `,
  ],
})
export class InvoiceVersionsTabComponent {
  private readonly api = inject(InvoicesApi);
  private readonly notify = inject(NotifyService);

  readonly invoiceId = input.required<number>();
  readonly loading = signal(true);
  readonly versions = signal<InvoiceVersionModel[]>([]);
  protected readonly words = words;
  protected readonly severity = statusSeverity;

  constructor() {
    effect(() => { this.invoiceId(); untracked(() => this.load()); });
  }

  private load(): void {
    this.loading.set(true);
    this.api.versions(this.invoiceId()).subscribe({
      next: (found) => { this.loading.set(false); this.versions.set(found); },
      error: (err) => { this.loading.set(false); this.notify.error(err); },
    });
  }
}
