import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { InvoicesApi } from '../../core/api.services';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { InvoiceEvidenceModel } from '../../core/invoice.models';
import { NotifyService } from '../../core/notify.service';
import { evidenceStatusSeverity, formatBytes, words } from './invoice-logic';

/**
 * Evidence tab (FSD §41, screen 24: "Evidence tab: versions list + PDF viewer" — Version, generated on/by,
 * pages, lines, status, hash; Download, Retry (failed), Re-render (Admin)). No in-page PDF viewer here — the
 * evidence file is stored/served like every other document download in this codebase (`window.open` on a
 * short-lived signed URL, the same shape `TripOperationsApi.documentDownloadLink`/`podDownloadLink` already use)
 * rather than a bespoke embedded viewer.
 */
@Component({
  selector: 'app-invoice-evidence-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, InstantPipe],
  template: `
    <div class="head">
      <p class="hint">§41: "Superseded when the invoice becomes Inactive" — older versions are kept, not deleted.</p>
      @if (canRerender()) { <p-button label="Re-render" icon="pi pi-refresh" size="small" [loading]="rerendering()" (onClick)="rerender()" /> }
    </div>
    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (versions().length === 0) {
      <p class="muted">Invoice evidence is not ready.</p>
    } @else {
      @for (e of versions(); track e.invoiceEvidenceId) {
        <div class="version">
          <div class="version-head">
            <span class="v">v{{ e.evidenceVersion }}</span>
            <p-tag [value]="words(e.status)" [severity]="severity(e.status)" />
            <span class="muted">{{ e.pageCount }} page(s) · {{ e.lineCount }} line(s) · {{ bytes(e.sizeBytes) }}</span>
            @if (e.generatedAtUtc) { <span class="muted">Generated {{ e.generatedAtUtc | vmsInstant }}</span> }
            <span class="grow"></span>
            @if (e.status === 'Generated') { <p-button label="Download" [text]="true" size="small" (onClick)="download(e)" /> }
            @if (e.status === 'Failed' && canRetry()) { <p-button label="Retry" [text]="true" size="small" [loading]="retrying() === e.invoiceEvidenceId" (onClick)="retry(e)" /> }
          </div>
          @if (e.errorMessage) { <div class="alert warning">{{ e.errorMessage }}</div> }
          @if (e.vehiclePages.length > 0) {
            <table>
              <thead><tr><th>Vehicle</th><th>Pages</th><th>Lines</th><th>Subtotal</th></tr></thead>
              <tbody>
                @for (p of e.vehiclePages; track p.vehicleRegNo) {
                  <tr><td class="mono">{{ p.vehicleRegNo }}</td><td>{{ p.firstPage }}–{{ p.lastPage }}</td><td>{{ p.lineCount }}</td><td>{{ p.subtotal }}</td></tr>
                }
              </tbody>
            </table>
          }
        </div>
      }
    }
  `,
  styles: [
    `
      .head { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: .75rem; }
      .hint { color: var(--vms-muted); font-size: .85rem; margin: 0; }
      .version { border: 1px solid var(--vms-border); border-radius: var(--vms-radius); padding: .75rem 1rem; margin-bottom: .75rem; }
      .version-head { display: flex; align-items: center; gap: .75rem; flex-wrap: wrap; } .v { font-weight: 600; }
      .grow { flex: 1 1 auto; } .muted { color: var(--vms-muted); font-size: .85rem; }
      .alert { margin-top: .5rem; } table { width: 100%; border-collapse: collapse; margin-top: .5rem; }
      th, td { text-align: left; padding: .4rem .6rem; border-bottom: 1px solid var(--vms-border); font-size: .85rem; } th { color: var(--vms-muted); font-weight: 600; }
    `,
  ],
})
export class InvoiceEvidenceTabComponent {
  private readonly api = inject(InvoicesApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly invoiceId = input.required<number>();

  protected readonly words = words;
  protected readonly severity = evidenceStatusSeverity;
  protected readonly bytes = formatBytes;
  protected readonly canRetry = computed(() => this.auth.hasPermission('TRP.INVOICE.EVIDENCE.RETRY'));
  protected readonly canRerender = computed(() => this.auth.hasPermission('TRP.INVOICE.EVIDENCE.RERENDER'));

  readonly loading = signal(true);
  readonly versions = signal<InvoiceEvidenceModel[]>([]);
  readonly rerendering = signal(false);
  readonly retrying = signal<number | null>(null);

  constructor() {
    effect(() => { this.invoiceId(); untracked(() => this.load()); });
  }

  private load(): void {
    this.loading.set(true);
    this.api.evidence(this.invoiceId()).subscribe({
      next: (found) => { this.loading.set(false); this.versions.set(found); },
      error: (err) => { this.loading.set(false); this.notify.error(err); },
    });
  }

  protected download(e: InvoiceEvidenceModel): void {
    this.api.downloadEvidence(this.invoiceId(), e.invoiceEvidenceId).subscribe({
      next: (link) => window.open(link.url, '_blank'),
      error: (err) => this.notify.error(err),
    });
  }

  protected retry(e: InvoiceEvidenceModel): void {
    this.retrying.set(e.invoiceEvidenceId);
    this.api.retryEvidence(this.invoiceId(), e.invoiceEvidenceId).subscribe({
      next: () => { this.retrying.set(null); this.notify.success('Evidence generation retried'); this.load(); },
      error: (err) => { this.retrying.set(null); this.notify.error(err); },
    });
  }

  protected rerender(): void {
    this.rerendering.set(true);
    this.api.rerenderEvidence(this.invoiceId()).subscribe({
      next: () => { this.rerendering.set(false); this.notify.success('Evidence re-render queued'); this.load(); },
      error: (err) => { this.rerendering.set(false); this.notify.error(err); },
    });
  }
}
