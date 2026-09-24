import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { DocumentsApi } from '../../core/api.services';
import { DocumentOwnerType, DocumentType } from '../../core/document.models';
import { uploadFile } from '../../core/upload';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { VehiclePickerComponent } from '../../shared/vehicle-picker.component';
import { NotifyService } from '../../core/notify.service';

type RowStatus = 'pending' | 'uploading' | 'done' | 'failed';

interface BulkRow {
  ownerType: DocumentOwnerType;
  ownerId: number | null;
  documentTypeId: number | null;
  documentNumber: string;
  issueDate: string | null;
  expiryDate: string | null;
  file: File | null;
  status: RowStatus;
  error: string | null;
}

const OWNER_TYPES: { label: string; value: DocumentOwnerType }[] = [
  { label: 'Vehicle', value: 'Vehicle' },
  { label: 'Business partner', value: 'BusinessPartner' },
];

const blankRow = (ownerType: DocumentOwnerType = 'Vehicle'): BulkRow => ({
  ownerType, ownerId: null, documentTypeId: null, documentNumber: '', issueDate: null, expiryDate: null, file: null, status: 'pending', error: null,
});

/**
 * Administration → Bulk document upload (S8-QA-03, FSD §23A.4): loading the paperwork already on file for every
 * vehicle and partner at go-live, without opening each record's own Documents tab one at a time. No dedicated
 * backend endpoint — each row is the same single-document upload the Documents tab itself uses
 * (`POST .../documents`), sent one at a time so a bad row's error names exactly that row and does not stop the
 * rest (the same "one bad item does not stop the batch" principle as `POST /api/payables/bulk-confirm`, Stage 4).
 */
@Component({
  selector: 'app-bulk-documents',
  standalone: true,
  imports: [FormsModule, ButtonModule, SelectModule, TableModule, DatePickerComponent, PartnerPickerComponent, VehiclePickerComponent],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Bulk document upload</h1>
          <div class="sub">Load the paperwork already on file for many vehicles and partners in one sitting — for go-live, not day-to-day use. Each row uploads on its own; a mistake in one row does not stop the rest.</div>
        </div>
        <p-button label="Add row" icon="pi pi-plus" severity="secondary" [outlined]="true" (onClick)="addRow()" />
      </div>

      <div class="card">
        <p-table [value]="rows()">
          <ng-template pTemplate="header">
            <tr>
              <th style="width: 9rem">Owner type</th>
              <th style="width: 16rem">Owner</th>
              <th style="width: 14rem">Document type</th>
              <th style="width: 10rem">Number</th>
              <th style="width: 9rem">Issue date</th>
              <th style="width: 9rem">Expiry date</th>
              <th style="width: 14rem">File</th>
              <th style="width: 10rem">Status</th>
              <th></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row let-i="rowIndex">
            <tr>
              <td>
                <p-select [options]="ownerTypes" optionLabel="label" optionValue="value" [(ngModel)]="row.ownerType" (ngModelChange)="onOwnerTypeChanged(row)"
                          [disabled]="row.status === 'uploading' || row.status === 'done'" [ariaLabel]="'Owner type, row ' + (i + 1)" [fluid]="true" appendTo="body" />
              </td>
              <td>
                @if (row.ownerType === 'Vehicle') {
                  <vms-vehicle-picker [(ngModel)]="row.ownerId" />
                } @else {
                  <vms-partner-picker [(ngModel)]="row.ownerId" [allowCreate]="false" />
                }
              </td>
              <td>
                <p-select [options]="typesFor(row.ownerType)" optionLabel="name" optionValue="id" [(ngModel)]="row.documentTypeId"
                          [disabled]="row.status === 'uploading' || row.status === 'done'" placeholder="Choose…" ariaLabel="Document type" [fluid]="true" appendTo="body" />
              </td>
              <td><input type="text" [(ngModel)]="row.documentNumber" [disabled]="row.status === 'uploading' || row.status === 'done'" class="cell-input" /></td>
              <td><vms-date-picker [(ngModel)]="row.issueDate" [disabled]="row.status === 'uploading' || row.status === 'done'" /></td>
              <td><vms-date-picker [(ngModel)]="row.expiryDate" [disabled]="row.status === 'uploading' || row.status === 'done'" /></td>
              <td>
                @if (row.file) {
                  <span class="file-name">{{ row.file.name }}</span>
                } @else {
                  <button type="button" class="choose" [disabled]="row.status === 'uploading' || row.status === 'done'" (click)="picker.click()">Choose file</button>
                }
                <input #picker type="file" class="sr-only" accept=".pdf,.png,.jpg,.jpeg" [disabled]="row.status === 'uploading' || row.status === 'done'" (change)="fileChosen(row, $event)" />
              </td>
              <td>
                @switch (row.status) {
                  @case ('pending') { <span class="muted">Not sent</span> }
                  @case ('uploading') { <span class="chip chip--info">Uploading…</span> }
                  @case ('done') { <span class="chip chip--success">Uploaded</span> }
                  @case ('failed') { <span class="chip chip--danger" [title]="row.error">Failed</span> }
                }
              </td>
              <td class="actions">
                <p-button icon="pi pi-trash" [text]="true" severity="secondary" size="small" [ariaLabel]="'Remove row ' + (i + 1)" [disabled]="row.status === 'uploading'" (onClick)="removeRow(i)" />
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="9" class="muted">No rows yet. Add one to start loading documents.</td></tr>
          </ng-template>
        </p-table>

        @if (rows().length > 0) {
          <div class="toolbar">
            <span class="muted">{{ doneCount() }} of {{ rows().length }} uploaded{{ failedCount() > 0 ? ', ' + failedCount() + ' failed' : '' }}.</span>
            <p-button label="Upload all" icon="pi pi-upload" [loading]="uploading()" [disabled]="!canUploadAny()" (onClick)="uploadAll()" />
          </div>
        }
      </div>
    </div>
  `,
  styles: [
    `
      .toolbar { display: flex; align-items: center; justify-content: space-between; padding: .85rem 1rem; border-top: 1px solid var(--vms-border); }
      .cell-input { width: 100%; padding: .45rem .6rem; border: 1px solid var(--vms-border); border-radius: var(--vms-radius-sm); background: var(--vms-surface); color: var(--vms-text); font: inherit; }
      .choose { border: 1px dashed var(--vms-border-strong); background: var(--vms-surface-soft); color: var(--vms-text); border-radius: var(--vms-radius-sm); padding: .4rem .7rem; font: inherit; cursor: pointer; }
      .file-name { font-size: .85rem; word-break: break-all; }
      .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    `,
  ],
})
export class BulkDocumentsComponent implements OnInit {
  private readonly documentsApi = inject(DocumentsApi);
  private readonly http = inject(HttpClient);
  private readonly notify = inject(NotifyService);

  protected readonly ownerTypes = OWNER_TYPES;
  protected readonly rows = signal<BulkRow[]>([blankRow()]);
  protected readonly uploading = signal(false);

  private readonly vehicleTypes = signal<DocumentType[]>([]);
  private readonly partnerTypes = signal<DocumentType[]>([]);

  ngOnInit(): void {
    this.documentsApi.types('Vehicle').subscribe((t) => this.vehicleTypes.set(t));
    this.documentsApi.types('BusinessPartner').subscribe((t) => this.partnerTypes.set(t));
  }

  protected typesFor(ownerType: DocumentOwnerType): DocumentType[] {
    return ownerType === 'Vehicle' ? this.vehicleTypes() : this.partnerTypes();
  }

  protected onOwnerTypeChanged(row: BulkRow): void {
    row.ownerId = null;
    row.documentTypeId = null;
  }

  protected addRow(): void {
    this.rows.update((rows) => [...rows, blankRow()]);
  }

  protected removeRow(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected fileChosen(row: BulkRow, event: Event): void {
    const input = event.target as HTMLInputElement;
    row.file = input.files?.[0] ?? null;
    input.value = '';
  }

  protected doneCount(): number {
    return this.rows().filter((r) => r.status === 'done').length;
  }

  protected failedCount(): number {
    return this.rows().filter((r) => r.status === 'failed').length;
  }

  private isReady(row: BulkRow): boolean {
    return row.status !== 'done' && row.status !== 'uploading' && !!row.ownerId && !!row.documentTypeId && !!row.file;
  }

  protected canUploadAny(): boolean {
    return !this.uploading() && this.rows().some((r) => this.isReady(r));
  }

  /** One row at a time, not in parallel: several uploads for the same owner and type racing would collide on the "one current version" rule (BR-DOC-001) for no benefit — go-live loading is not latency-sensitive. */
  protected async uploadAll(): Promise<void> {
    this.uploading.set(true);
    const rows = this.rows();
    let succeeded = 0;
    let failed = 0;
    for (const row of rows) {
      if (!this.isReady(row)) continue;
      row.status = 'uploading';
      this.rows.update((r) => [...r]);   // re-render this row's status
      try {
        await this.uploadOne(row);
        row.status = 'done';
        row.error = null;
        succeeded++;
      } catch (err) {
        row.status = 'failed';
        row.error = err instanceof Error ? err.message : 'Upload failed.';
        failed++;
      }
      this.rows.update((r) => [...r]);
    }
    this.uploading.set(false);
    if (failed === 0) this.notify.success(`${succeeded} document${succeeded === 1 ? '' : 's'} uploaded.`, 'Done');
    else this.notify.warn(`${succeeded} uploaded, ${failed} failed. Fix the failed rows and upload again.`, 'Some rows failed');
  }

  private uploadOne(row: BulkRow): Promise<void> {
    const url = this.documentsApi.uploadUrl(row.ownerType, row.ownerId!);
    const fields: Record<string, string> = { documentTypeId: String(row.documentTypeId) };
    if (row.documentNumber.trim()) fields['documentNumber'] = row.documentNumber.trim();
    if (row.issueDate) fields['issueDate'] = row.issueDate;
    if (row.expiryDate) fields['expiryDate'] = row.expiryDate;

    return new Promise((resolve, reject) => {
      uploadFile(this.http, url, row.file!, fields).subscribe({
        next: (e) => { if (e.progress === 100) resolve(); },
        error: (err) => reject(new Error(err?.error?.message ?? err?.error?.errors?.[0]?.message ?? 'Upload failed.')),
      });
    });
  }
}
