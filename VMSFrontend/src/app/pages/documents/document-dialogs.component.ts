import { HttpClient } from '@angular/common/http';
import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { DocumentsApi, LookupsApi, VehiclesApi } from '../../core/api.services';
import { errorMessage } from '../../core/api-error';
import { BusinessDatePipe } from '../../core/datetime/datetime.pipes';
import { todayDateOnly } from '../../core/datetime/datetime';
import { DocumentOwnerType, DocumentType, DocumentVersion, UploadDocumentFields } from '../../core/document.models';
import { NotifyService } from '../../core/notify.service';
import { Payable } from '../../core/vehicle.models';
import { UploadEvent, uploadFile } from '../../core/upload';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { FileUploadComponent } from '../../shared/file-upload.component';
import { addValidity, words } from './document-logic';

/**
 * Uploading a document into an empty slot, or renewing one that already has a current version (§23A.4, BR-DOC-005). The
 * metadata is filled in first — the drop zone only accepts a file once it is valid, since the file and its fields go up together.
 * When renewing a "Has cost" type with a matching recurring charge (BR-DOC-006), it can confirm that entry and tag the new
 * version with the resulting transaction, so the premium is never recorded twice — the confirm is the only posting; this
 * dialog's own upload never posts anything of its own.
 */
@Component({
  selector: 'app-upload-renew-dialog',
  standalone: true,
  imports: [
    DecimalPipe, ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, InputNumberModule, TextareaModule, CheckboxModule,
    FieldComponent, DatePickerComponent, FileUploadComponent, BusinessDatePipe,
  ],
  template: `
    <p-dialog [visible]="!!type()" (visibleChange)="!$event && close()" [modal]="true" [style]="{ width: '560px' }" [header]="current() ? 'Renew ' + type()?.name : 'Upload ' + type()?.name" [closable]="!linking()">
      @if (type(); as t) {
        <form [formGroup]="form" class="stack" novalidate>
          @if (t.requiresDocumentNumber) {
            <vms-field label="Document number" [control]="form.controls.documentNumber" for="doc-number"><input pInputText id="doc-number" formControlName="documentNumber" autocomplete="off" /></vms-field>
          }
          <vms-field label="Provider" [control]="form.controls.provider" for="doc-provider" hint="Optional — who issued it.">
            <input pInputText id="doc-provider" formControlName="provider" autocomplete="off" />
          </vms-field>
          @if (t.isExpirable) {
            <vms-field label="Issue date" [control]="form.controls.issueDate" for="doc-issue" hint="Optional."><vms-date-picker inputId="doc-issue" formControlName="issueDate" [notFuture]="true" /></vms-field>
            <vms-field label="Expiry date" [control]="form.controls.expiryDate" for="doc-expiry" [hint]="expiryHint()"><vms-date-picker inputId="doc-expiry" formControlName="expiryDate" /></vms-field>
          }

          @if (current() && t.hasCost && t.linkedChargeTypeCode) {
            <div class="full link-panel">
              @if (matching(); as m) {
                <label class="link-check"><p-checkbox [binary]="true" formControlName="linkPayment" inputId="doc-link" />
                  <span>Confirm the matching charge — {{ m.chargeType }}, PKR {{ m.expectedAmount | number: '1.2-2' }} due {{ m.dueDate | vmsDate }} — and link this renewal to it, so it is not recorded twice (BR-DOC-006).</span>
                </label>
                @if (form.controls.linkPayment.value) {
                  <div class="link-fields">
                    <vms-field label="Amount paid (PKR)" [control]="form.controls.linkAmount" for="doc-link-amount"><p-inputnumber inputId="doc-link-amount" formControlName="linkAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
                    <vms-field label="Paid on" [control]="form.controls.linkPaidOn" for="doc-link-date"><vms-date-picker inputId="doc-link-date" formControlName="linkPaidOn" [notFuture]="true" /></vms-field>
                  </div>
                }
              } @else {
                <p class="muted small">No due or overdue matching charge entry to link right now. Renew this on its own; confirm the charge separately from Payables when it is due.</p>
              }
            </div>
          }
        </form>

        @if (problem(); as p) { <div class="alert error" role="alert">{{ p }}</div> }
        @if (ready()) {
          <vms-file-upload [kinds]="kindsOf(t)" [maxBytes]="t.maxFileSizeMb * 1024 * 1024" [label]="current() ? 'Choose the renewed file' : 'Choose a file'" [disabled]="linking()"
            [upload]="upload" (uploaded)="onUploaded()" />
        } @else {
          <p class="muted small">Fill in the required fields above to choose a file.</p>
        }
      }
      <ng-template pTemplate="footer"><p-button label="Close" severity="secondary" [text]="true" (onClick)="close()" [disabled]="linking()" /></ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .stack { display: flex; flex-direction: column; gap: 1rem; margin-bottom: 1rem; } .full { grid-column: 1 / -1; }
      .link-panel { border: 1px solid var(--vms-border); border-radius: var(--vms-radius); padding: .75rem; background: var(--vms-surface-soft); }
      .link-check { display: flex; align-items: flex-start; gap: .5rem; cursor: pointer; font-size: .875rem; }
      .link-fields { display: grid; grid-template-columns: 1fr 1fr; gap: .75rem; margin-top: .75rem; }
      .small { font-size: .82rem; } .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class UploadRenewDialogComponent {
  private readonly http = inject(HttpClient);
  private readonly docs = inject(DocumentsApi);
  private readonly vehicles = inject(VehiclesApi);
  private readonly lookups = inject(LookupsApi);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);

  readonly ownerType = input.required<DocumentOwnerType>();
  readonly ownerId = input.required<number>();
  /** Drives the dialog's visibility: open while a type is given. */
  readonly type = input<DocumentType | null>(null);
  /** Null for a fresh upload into an empty slot; the slot's current version when renewing it. */
  readonly current = input<DocumentVersion | null>(null);

  readonly closed = output<void>();
  readonly saved = output<void>();

  protected readonly problem = signal<string | null>(null);
  protected readonly linking = signal(false);
  protected readonly matching = signal<Payable | null>(null);
  private chargeTypeCodeById = new Map<number, string>();

  protected readonly words = words;

  readonly form = this.fb.group({
    documentNumber: [''],
    provider: [''],
    issueDate: this.fb.control<string | null>(null),
    expiryDate: this.fb.control<string | null>(null),
    linkPayment: [false],
    linkAmount: this.fb.control<number | null>(null),
    linkPaidOn: this.fb.control<string | null>(todayDateOnly()),
  });

  /** The form's own value as a signal — `computed()` only reacts to signals, not to `FormGroup.valueChanges` directly. */
  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  /** The metadata fields the file needs to go up: valid once the type's own requirements are met. */
  protected readonly ready = computed(() => {
    const t = this.type();
    if (!t) return false;
    const v = this.formValue();
    if (t.requiresDocumentNumber && !(v.documentNumber ?? '').trim()) return false;
    if (t.isExpirable && !v.expiryDate) return false;
    return true;
  });

  /** BR-DOC-005: the computed expiry, once there is a previous version to compute it from — shown pre-filled, not just hinted at, so confirming it is a glance, not arithmetic. */
  private static computedExpiry(current: DocumentVersion | null, type: DocumentType | null): string | null {
    return type && current?.expiryDate && type.defaultValidityValue ? addValidity(current.expiryDate, type.defaultValidityValue, type.defaultValidityUnit) : null;
  }

  protected readonly expiryHint = computed(() => {
    const computed = UploadRenewDialogComponent.computedExpiry(this.current(), this.type());
    const t = this.type();
    return computed && t ? `Computed from the previous expiry plus ${t.defaultValidityValue} ${words(t.defaultValidityUnit ?? 'Months').toLowerCase()}. Change it if it is not right.` : '';
  });

  constructor() {
    effect(() => {
      const t = this.type();
      const c = this.current();
      if (!t) return;
      untracked(() => {
        this.problem.set(null);
        this.form.reset({
          documentNumber: c?.documentNumber ?? '', provider: c?.provider ?? '', issueDate: null, expiryDate: UploadRenewDialogComponent.computedExpiry(c, t),
          linkPayment: false, linkAmount: null, linkPaidOn: todayDateOnly(),
        });
        this.matching.set(null);
        if (c && t.hasCost && t.linkedChargeTypeCode && this.ownerType() === 'Vehicle') this.loadMatching(t.linkedChargeTypeCode);
      });
    });
  }

  private loadMatching(chargeTypeCode: string): void {
    const find = (): void => {
      this.vehicles.payables(this.ownerId()).subscribe({
        next: (rows) => {
          const match = rows.find((p) => p.kind === 'RecurringCharge' && p.chargeTypeId !== null && p.chargeTypeId !== undefined && this.chargeTypeCodeById.get(p.chargeTypeId) === chargeTypeCode);
          this.matching.set(match ?? null);
          if (match) this.form.patchValue({ linkAmount: match.expectedAmount ?? null });
        },
        error: () => this.matching.set(null),
      });
    };
    if (this.chargeTypeCodeById.size > 0) { find(); return; }
    this.lookups.active('RECURRING_CHARGE_TYPE').subscribe({
      next: (rows) => { this.chargeTypeCodeById = new Map(rows.map((r) => [r.id, r.code])); find(); },
      error: () => this.matching.set(null),
    });
  }

  /** The API's `FileKinds` enum names (`Pdf`, `Word`, …) to this app's own `FileKind` strings (`pdf`, `docx`, …). */
  private static readonly KIND_NAMES: Record<string, 'pdf' | 'png' | 'jpeg' | 'docx' | 'xlsx'> = { Pdf: 'pdf', Png: 'png', Jpeg: 'jpeg', Word: 'docx', Excel: 'xlsx' };

  protected kindsOf(t: DocumentType): ('pdf' | 'png' | 'jpeg' | 'docx' | 'xlsx')[] {
    return t.allowedFormats.map((f) => UploadRenewDialogComponent.KIND_NAMES[f]).filter((f): f is 'pdf' | 'png' | 'jpeg' | 'docx' | 'xlsx' => !!f);
  }

  /** Given to `vms-file-upload`: confirms the linked payment first (if asked), then sends the file with the metadata. */
  protected readonly upload = (file: File): ReturnType<typeof uploadFile> => {
    const t = this.type();
    const c = this.current();
    if (!t) throw new Error('No document type chosen.');
    const v = this.form.getRawValue();
    const fields: UploadDocumentFields = {};
    if (!c) fields.documentTypeId = String(t.id);   // a renewal's type comes from the URL; a fresh upload has no slot to carry it otherwise
    if ((v.documentNumber ?? '').trim()) fields.documentNumber = v.documentNumber!.trim();
    if ((v.provider ?? '').trim()) fields.provider = v.provider!.trim();
    if (v.issueDate) fields.issueDate = v.issueDate;
    if (v.expiryDate) fields.expiryDate = v.expiryDate;

    const url = c ? this.docs.renewUrl(this.ownerType(), this.ownerId(), t.id) : this.docs.uploadUrl(this.ownerType(), this.ownerId());
    const m = this.matching();
    if (c && v.linkPayment && m) {
      this.linking.set(true);
      // Confirming the matching charge entry is the sole posting (BR-DOC-006); the upload that follows only tags the
      // new version with the transaction it produced, so the premium is never posted a second time by this dialog.
      return new Observable<UploadEvent>((subscriber) => {
        this.vehicles.confirmChargeEntry(this.ownerId(), m.id, { amount: v.linkAmount, paidOn: v.linkPaidOn, paymentMode: null, reference: null }).subscribe({
          next: (payment) => {
            this.linking.set(false);
            fields.linkedTransactionId = String(payment.transactionId);
            uploadFile(this.http, url, file, fields as unknown as Record<string, string>).subscribe(subscriber);
          },
          error: (err) => { this.linking.set(false); subscriber.error(err); },
        });
      });
    }
    return uploadFile(this.http, url, file, fields as unknown as Record<string, string>);
  };

  protected onUploaded(): void {
    this.notify.success(this.current() ? 'Document renewed.' : 'Document uploaded.');
    this.saved.emit();
  }

  protected close(): void {
    if (this.linking()) return;
    this.closed.emit();
  }
}

/** Rejecting a document version with a reason (§23A.4, BR-DOC-008): the slot is left empty, not deleted or replaced. */
@Component({
  selector: 'app-reject-document-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, TextareaModule, FieldComponent],
  template: `
    <p-dialog [visible]="!!documentId()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '460px' }" header="Reject this document" [closable]="!busy()">
      @if (problem(); as p) { <div class="alert error" role="alert">{{ p }}</div> }
      <form [formGroup]="form" novalidate>
        <vms-field label="Reason" [control]="form.controls.reason" for="rej-reason"><textarea pTextarea id="rej-reason" formControlName="reason" rows="3" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Back" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Reject" icon="pi pi-ban" severity="danger" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [``],
})
export class RejectDocumentDialogComponent {
  private readonly api = inject(DocumentsApi);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);

  readonly documentId = input<number | null>(null);
  readonly closed = output<void>();
  readonly rejected = output<void>();

  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);
  readonly form = this.fb.group({ reason: ['', [Validators.required, Validators.maxLength(500)]] });

  constructor() {
    effect(() => { if (this.documentId()) untracked(() => { this.form.reset({ reason: '' }); this.problem.set(null); }); });
  }

  protected save(): void {
    const id = this.documentId();
    if (!id || this.busy()) return;
    this.form.controls.reason.setValue((this.form.controls.reason.value ?? '').trim());
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.busy.set(true);
    this.api.reject(id, this.form.getRawValue().reason ?? '').subscribe({
      next: () => { this.busy.set(false); this.notify.success('Document rejected.'); this.rejected.emit(); },
      error: (err) => { this.busy.set(false); this.problem.set(errorMessage(err, 'Could not reject the document.')); },
    });
  }
}
