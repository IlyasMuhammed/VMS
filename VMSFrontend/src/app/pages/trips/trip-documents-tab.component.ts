import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../core/auth.service';
import { TripOperationsApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { InstantPipe } from '../../core/datetime/datetime.pipes';
import { NotifyService } from '../../core/notify.service';
import { uploadFile } from '../../core/upload';
import { TripDocumentModel, TripPodModel } from '../../core/trip-operations.models';
import { ConfirmService } from '../../shared/confirm.service';
import { FileUploadComponent } from '../../shared/file-upload.component';
import { ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { TRIP_DOCUMENT_TYPES, optionsOf, podSeverity, words } from './trip-logic';

/** Documents & POD tab (FSD §23, screen 14): everything uploaded for the trip, plus the one file that carries an
 * approval workflow — proof of delivery. Two independent sections rather than one merged list, since a POD's
 * own status/approve/reject actions have nothing in common with an ordinary document's plain list-and-download. */
@Component({
  selector: 'app-trip-documents-tab',
  standalone: true,
  imports: [FormsModule, ButtonModule, DialogModule, SelectModule, TagModule, InstantPipe, FileUploadComponent, ReasonDialogComponent],
  template: `
    <h2>Documents</h2>
    @if (docError(); as message) { <div class="alert error" role="alert">{{ message }}</div> }
    <table>
      <thead><tr><th>Type</th><th>File</th><th>Uploaded</th><th>Source</th><th></th></tr></thead>
      <tbody>
        @for (d of documents(); track d.tripDocumentId) {
          <tr>
            <td>{{ words(d.documentType) }}</td><td>{{ d.originalFileName }}</td><td class="nowrap">{{ d.uploadedAtUtc | vmsInstant }}</td><td>{{ words(d.source) }}</td>
            <td class="row-actions"><p-button label="Download" [text]="true" size="small" (onClick)="downloadDocument(d)" /></td>
          </tr>
        }
        @if (documents().length === 0) { <tr><td colspan="5" class="muted">{{ docLoading() ? 'Loading…' : 'No documents uploaded yet.' }}</td></tr> }
      </tbody>
    </table>
    @if (canManage()) {
      <div class="upload-row">
        <p-select class="doc-type" [(ngModel)]="newDocType" [options]="docTypeOptions" optionLabel="label" optionValue="value" appendTo="body" />
        <vms-file-upload [kinds]="['pdf', 'png', 'jpeg']" label="Choose a document" [upload]="uploadDocument" (uploaded)="load()" />
      </div>
    }

    <h2>Proof of Delivery</h2>
    @if (podError(); as message) { <div class="alert error" role="alert">{{ message }}</div> }
    <table>
      <thead><tr><th>File</th><th>Uploaded</th><th>Status</th><th>Notes</th><th></th></tr></thead>
      <tbody>
        @for (p of pods(); track p.tripPODId) {
          <tr>
            <td>{{ p.originalFileName }}</td><td class="nowrap">{{ p.uploadedAtUtc | vmsInstant }}</td>
            <td><p-tag [value]="p.status" [severity]="pod(p.status)" /></td>
            <td>{{ p.rejectedReason || '—' }}</td>
            <td class="row-actions">
              <p-button label="Download" [text]="true" size="small" (onClick)="downloadPod(p)" />
              @if (canApprove() && p.status === 'Uploaded') {
                <p-button label="Approve" [text]="true" size="small" (onClick)="approvePod(p)" />
                <p-button label="Reject" [text]="true" size="small" severity="danger" (onClick)="rejecting.set(p)" />
              }
            </td>
          </tr>
        }
        @if (pods().length === 0) { <tr><td colspan="5" class="muted">{{ podLoading() ? 'Loading…' : 'No proof of delivery uploaded yet.' }}</td></tr> }
      </tbody>
    </table>
    @if (canManage()) {
      <div class="upload-row"><vms-file-upload [kinds]="['pdf', 'png', 'jpeg']" label="Choose a POD file" [upload]="uploadPod" (uploaded)="load()" /></div>
    }

    <vms-reason-dialog [open]="!!rejecting()" title="Reject proof of delivery" confirmLabel="Reject" [minLength]="10" [busy]="actionBusy()" (closed)="rejecting.set(null)" (confirmed)="doReject($event)" />
  `,
  styles: [
    `
      h2 { font-size: 1.05rem; margin: 1.5rem 0 .75rem; } h2:first-child { margin-top: 0; }
      table { width: 100%; border-collapse: collapse; margin-bottom: .75rem; } th, td { text-align: left; padding: .5rem .65rem; border-bottom: 1px solid var(--vms-border); font-size: .875rem; } th { color: var(--vms-muted); font-weight: 600; }
      .row-actions { white-space: nowrap; text-align: right; } .nowrap { white-space: nowrap; } .upload-row { display: flex; gap: .75rem; align-items: flex-start; margin-bottom: 1rem; } .alert { margin-bottom: 1rem; }
      .doc-type { max-width: 12rem; }
    `,
  ],
})
export class TripDocumentsTabComponent {
  private readonly api = inject(TripOperationsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  protected readonly http = inject(HttpClient);

  readonly tripId = input.required<number>();
  protected readonly documents = signal<TripDocumentModel[]>([]);
  protected readonly pods = signal<TripPodModel[]>([]);
  protected readonly docLoading = signal(false);
  protected readonly podLoading = signal(false);
  protected readonly docError = signal<string | null>(null);
  protected readonly podError = signal<string | null>(null);
  protected readonly canManage = () => this.auth.hasPermission('TRP.TRIP.DOCUMENTS');
  protected readonly canApprove = () => this.auth.hasPermission('TRP.POD.APPROVE');
  protected readonly words = words;
  protected readonly pod = podSeverity;

  protected readonly docTypeOptions = optionsOf(TRIP_DOCUMENT_TYPES);
  protected newDocType: string = TRIP_DOCUMENT_TYPES[0];
  protected readonly rejecting = signal<TripPodModel | null>(null);
  protected readonly actionBusy = signal(false);

  protected readonly uploadDocument = (file: File): ReturnType<typeof uploadFile> =>
    uploadFile(this.http, this.api.documentUploadUrl(this.tripId()), file, { documentType: this.newDocType });

  protected readonly uploadPod = (file: File): ReturnType<typeof uploadFile> => uploadFile(this.http, this.api.podUploadUrl(this.tripId()), file);

  constructor() {
    effect(() => { this.tripId(); untracked(() => this.load()); });
  }

  load(): void {
    this.docLoading.set(true);
    this.docError.set(null);
    this.api.documents(this.tripId()).subscribe({
      next: (rows) => { this.docLoading.set(false); this.documents.set(rows); },
      error: (err) => { this.docLoading.set(false); this.docError.set(errorMessage(err, 'Documents could not be loaded.')); },
    });
    this.podLoading.set(true);
    this.podError.set(null);
    this.api.pods(this.tripId()).subscribe({
      next: (rows) => { this.podLoading.set(false); this.pods.set(rows); },
      error: (err) => { this.podLoading.set(false); this.podError.set(errorMessage(err, 'Proof of delivery could not be loaded.')); },
    });
  }

  protected downloadDocument(d: TripDocumentModel): void {
    this.api.documentDownloadLink(d.tripDocumentId).subscribe({ next: (link) => window.open(link.url, '_blank'), error: (err) => this.notify.error(err) });
  }

  protected downloadPod(p: TripPodModel): void {
    this.api.podDownloadLink(p.tripPODId).subscribe({ next: (link) => window.open(link.url, '_blank'), error: (err) => this.notify.error(err) });
  }

  protected approvePod(p: TripPodModel): void {
    void this.confirm.ask({ title: 'Approve proof of delivery', message: 'Approve this proof of delivery?', confirmLabel: 'Approve', cancelLabel: 'Cancel', icon: 'pi pi-check' }).then((go) => {
      if (!go) return;
      this.api.approvePod(p.tripPODId).subscribe({ next: () => this.load(), error: (err) => this.notify.error(err) });
    });
  }

  protected doReject(reason: string): void {
    const p = this.rejecting();
    if (!p) return;
    this.actionBusy.set(true);
    this.api.rejectPod(p.tripPODId, { reason }).subscribe({
      next: () => { this.actionBusy.set(false); this.rejecting.set(null); this.notify.success('Proof of delivery rejected'); this.load(); },
      error: (err) => { this.actionBusy.set(false); this.notify.error(err); },
    });
  }
}
