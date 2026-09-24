import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { apiFileUrl, DocumentsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { BusinessDatePipe, InstantPipe } from '../../core/datetime/datetime.pipes';
import { DocumentOwnerType, DocumentSlot, DocumentType, DocumentVersion } from '../../core/document.models';
import { NotifyService } from '../../core/notify.service';
import { RejectDocumentDialogComponent, UploadRenewDialogComponent } from './document-dialogs.component';
import { daysRemainingText, mandatorySeverity, statusSeverity, words } from './document-logic';

/**
 * A vehicle's or a partner's Documents tab (§23A.4): one slot per applicable type, its current version, and — on request —
 * the versions it replaced. The same component is embedded on both screens; only `ownerType`/`ownerId` differ.
 */
@Component({
  selector: 'app-documents-tab',
  standalone: true,
  imports: [ButtonModule, TagModule, BusinessDatePipe, InstantPipe, UploadRenewDialogComponent, RejectDocumentDialogComponent],
  template: `
    @if (error(); as message) { <div class="alert error" role="alert">{{ message }} <p-button label="Try again" [text]="true" size="small" (onClick)="load()" /></div> }

    <table>
      <thead><tr><th>Type</th><th>Number</th><th>Status</th><th>Expiry</th><th>File</th><th></th></tr></thead>
      <tbody>
        @for (slot of slots(); track slot.documentTypeId) {
          <tr>
            <td>
              {{ slot.documentTypeName }}
              @if (slot.mandatoryLevel !== 'None') { <p-tag [value]="slot.mandatoryLevel" [severity]="severityOf(slot.mandatoryLevel)" [rounded]="true" class="mand" /> }
            </td>
            @if (slot.current; as c) {
              <td>{{ c.documentNumber || '—' }}</td>
              <td><p-tag [value]="words(c.status)" [severity]="statusOf(c.status)" /></td>
              <td class="nowrap">@if (c.expiryDate) { {{ c.expiryDate | vmsDate }} <span class="muted small">({{ daysText(c.daysRemaining) }})</span> } @else { — }</td>
              <td>{{ c.originalFileName }}</td>
              <td class="row-actions">
                @if (canDownload()) { <p-button label="Download" [text]="true" size="small" (onClick)="download(c)" [loading]="downloading() === c.id" /> }
                @if (canRenew()) { <p-button label="Renew" [text]="true" size="small" (onClick)="openRenew(slot)" /> }
                @if (canReject()) { <p-button label="Reject" [text]="true" size="small" severity="danger" (onClick)="rejectId.set(c.id)" /> }
              </td>
            } @else {
              <td class="muted">—</td>
              <td><span class="muted">Not on file</span></td>
              <td class="muted">—</td>
              <td class="muted">—</td>
              <td class="row-actions">@if (canUpload()) { <p-button label="Upload" icon="pi pi-upload" [text]="true" size="small" (onClick)="openUpload(slot)" /> }</td>
            }
          </tr>
          @if (slot.history.length > 0) {
            <tr class="history-row">
              <td colspan="6">
                <details>
                  <summary>{{ slot.history.length }} earlier version{{ slot.history.length === 1 ? '' : 's' }}</summary>
                  <table class="nested">
                    <thead><tr><th>Version</th><th>Number</th><th>Status</th><th>Expiry</th><th>File</th><th>Uploaded</th><th>Reason</th></tr></thead>
                    <tbody>
                      @for (h of slot.history; track h.id) {
                        <tr>
                          <td>{{ h.versionNo }}</td><td>{{ h.documentNumber || '—' }}</td><td><p-tag [value]="words(h.status)" [severity]="statusOf(h.status)" /></td>
                          <td>{{ h.expiryDate ? (h.expiryDate | vmsDate) : '—' }}</td><td>{{ h.originalFileName }}</td><td>{{ h.createdOn | vmsInstant }}</td>
                          <td>{{ h.rejectReason || '—' }}@if (canDownload()) { <p-button label="Download" [text]="true" size="small" (onClick)="download(h)" [loading]="downloading() === h.id" /> }</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </details>
              </td>
            </tr>
          }
        }
        @if (slots().length === 0) { <tr><td colspan="6" class="muted">{{ loading() ? 'Loading…' : 'No document types apply here.' }}</td></tr> }
      </tbody>
    </table>

    <app-upload-renew-dialog [ownerType]="ownerType()" [ownerId]="ownerId()" [type]="active()" [current]="renewing() ? activeCurrent() : null"
      (closed)="active.set(null)" (saved)="active.set(null); load()" />
    <app-reject-document-dialog [documentId]="rejectId()" (closed)="rejectId.set(null)" (rejected)="rejectId.set(null); load()" />
  `,
  styles: [
    `
      table { width: 100%; border-collapse: collapse; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; vertical-align: top; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .small { font-size: .8rem; } .mand { margin-left: .4rem; }
      .history-row td { padding: 0 .65rem .5rem; border-bottom: 1px solid var(--vms-border); } details summary { cursor: pointer; color: var(--vms-muted); font-size: .82rem; padding: .35rem 0; }
      table.nested { margin: .25rem 0 .5rem; } table.nested th, table.nested td { font-size: .8rem; padding: .35rem .5rem; }
      .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class DocumentsTabComponent {
  private readonly api = inject(DocumentsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly ownerType = input.required<DocumentOwnerType>();
  readonly ownerId = input.required<number>();
  readonly includeHistory = input(true);

  protected readonly slots = signal<DocumentSlot[]>([]);
  protected readonly types = signal<DocumentType[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly downloading = signal<number | null>(null);
  protected readonly rejectId = signal<number | null>(null);

  protected readonly active = signal<DocumentType | null>(null);
  protected readonly renewing = signal(false);
  protected readonly activeCurrent = signal<DocumentVersion | null>(null);

  protected readonly canUpload = computed(() => this.auth.hasPermission('DOC.UPLOAD'));
  protected readonly canRenew = computed(() => this.auth.hasPermission('DOC.RENEW'));
  protected readonly canReject = computed(() => this.auth.hasPermission('DOC.REJECT'));
  protected readonly canDownload = computed(() => this.auth.hasPermission('DOC.DOWNLOAD') || this.auth.hasPermission('DOC.DOWNLOAD.SENSITIVE'));

  protected readonly words = words;
  protected readonly statusOf = statusSeverity;
  protected readonly severityOf = mandatorySeverity;
  protected readonly daysText = daysRemainingText;

  constructor() {
    effect(() => { this.ownerType(); this.ownerId(); untracked(() => this.load()); });
  }

  load(): void {
    const ownerType = this.ownerType();
    const ownerId = this.ownerId();
    this.loading.set(true);
    this.error.set(null);
    let pending = 2;
    const done = (): void => { if (--pending === 0) this.loading.set(false); };
    const failed = (err: unknown): void => { this.error.set(errorMessage(err, 'The documents could not be loaded.')); done(); };
    this.api.slots(ownerType, ownerId, this.includeHistory()).subscribe({ next: (rows) => { this.slots.set(rows); done(); }, error: failed });
    this.api.types(ownerType).subscribe({ next: (rows) => { this.types.set(rows); done(); }, error: failed });
  }

  protected openUpload(slot: DocumentSlot): void {
    const type = this.types().find((t) => t.id === slot.documentTypeId);
    if (!type) return;
    this.renewing.set(false);
    this.activeCurrent.set(null);
    this.active.set(type);
  }

  protected openRenew(slot: DocumentSlot): void {
    const type = this.types().find((t) => t.id === slot.documentTypeId);
    if (!type || !slot.current) return;
    this.renewing.set(true);
    this.activeCurrent.set(slot.current);
    this.active.set(type);
  }

  protected download(doc: DocumentVersion): void {
    // Opened synchronously, inside the click itself: a tab opened only once the link comes back (an async step later)
    // is outside the click's "user activation" window and Chrome silently blocks it as a popup. Filled in once the
    // link arrives instead, and closed again if the request fails.
    const tab = window.open('', '_blank');
    this.downloading.set(doc.id);
    this.api.downloadLink(doc.id).subscribe({
      next: (link) => { this.downloading.set(null); if (tab) tab.location.href = apiFileUrl(link.url); },
      error: (err) => { this.downloading.set(null); tab?.close(); this.notify.error(errorMessage(err, 'Could not open the file.')); },
    });
  }
}
