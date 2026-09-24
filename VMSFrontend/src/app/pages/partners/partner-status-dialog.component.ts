import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { PartnersApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { PartnerUsage } from '../../core/partner.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { PartnerStatusComponent } from './partner-bits.component';

/** What the dialog needs to know about the partner. Both the list row and the full partner fit. */
export interface StatusTarget {
  id: number;
  legalName: string;
  status: string;
}

/** The moves of FSD §13.1: Active to Inactive or Blacklisted, and back to Active. */
export const nextStatuses = (status: string): string[] => (status === 'Active' ? ['Inactive', 'Blacklisted'] : status === 'Merged' ? [] : ['Active']);

/**
 * Change a partner's status (FSD §13.1): choose the new status, say why (a blacklisting must, at 10 characters or more) and
 * from when. Before a partner is set Inactive the records that still depend on it are listed, and the change is not offered
 * until they are dealt with (BR-BP-014).
 */
@Component({
  selector: 'app-partner-status-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, TextareaModule, FieldComponent, DatePickerComponent, PartnerStatusComponent],
  template: `
    <p-dialog [visible]="!!partner()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '520px' }" header="Change status" [closable]="!busy()">
      @if (partner(); as p) {
        <div class="who">{{ p.legalName }} <vms-partner-status [status]="p.status" /></div>

        <form [formGroup]="form" class="stack" (ngSubmit)="save()" novalidate>
          <vms-field label="New status" [control]="form.controls.status" for="ps-status">
            <p-select inputId="ps-status" formControlName="status" [options]="options()" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" />
          </vms-field>

          @if (blockers().length > 0 && form.controls.status.value === 'Inactive') {
            <div class="alert warning" role="alert">
              <strong>Not yet: {{ blockers().length }} {{ blockers().length === 1 ? 'record still depends' : 'records still depend' }} on this partner.</strong>
              <ul>
                @for (b of blockers(); track b.recordCode) { <li><span class="mono">{{ b.recordCode }}</span> — {{ b.description }}</li> }
              </ul>
              Close or reassign these first, then set the partner Inactive.
            </div>
          }

          <vms-field [label]="form.controls.status.value === 'Blacklisted' ? 'Reason (why is this partner blacklisted?)' : 'Reason'" [control]="form.controls.reason"
                     [hint]="form.controls.status.value === 'Blacklisted' ? 'At least 10 characters. It is kept in the history.' : 'Optional.'" for="ps-reason">
            <textarea pTextarea id="ps-reason" formControlName="reason" rows="3" [fluid]="true"></textarea>
          </vms-field>

          <vms-field label="Effective from" [control]="form.controls.effectiveDate" hint="Today unless you enter an earlier date." for="ps-date">
            <vms-date-picker inputId="ps-date" formControlName="effectiveDate" [notFuture]="true" [prefillToday]="true" />
          </vms-field>
        </form>
      }
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Change status" icon="pi pi-check" [loading]="busy()" [disabled]="blocked()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .who { display: flex; align-items: center; gap: .5rem; font-weight: 600; margin-bottom: 1rem; }
      .stack { display: flex; flex-direction: column; gap: 1rem; }
      ul { margin: .5rem 0; padding-left: 1.25rem; }
    `,
  ],
})
export class PartnerStatusDialogComponent {
  private readonly api = inject(PartnersApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly partner = input<StatusTarget | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();

  readonly busy = signal(false);
  readonly options = signal<{ label: string; value: string }[]>([]);
  readonly blockers = signal<PartnerUsage[]>([]);

  readonly form = this.fb.nonNullable.group({
    status: ['', Validators.required],
    reason: ['', Validators.maxLength(500)],
    effectiveDate: this.fb.control<string | null>(null),
  });

  constructor() {
    // Each time the dialog opens for a partner: offer the moves that are allowed, and look up what would block deactivating.
    effect(() => {
      const p = this.partner();
      if (!p) return;
      untracked(() => {
        const moves = nextStatuses(p.status);
        this.options.set(moves.map((m) => ({ label: m, value: m })));
        this.form.reset({ status: moves[0] ?? '', reason: '', effectiveDate: null });
        this.blockers.set([]);
        if (p.status === 'Active') this.api.usage(p.id, 'Deactivate').subscribe({ next: (u) => this.blockers.set(u), error: () => undefined });
      });
    });

    // A blacklisting must say why.
    this.form.controls.status.valueChanges.subscribe((status) => {
      const reason = this.form.controls.reason;
      reason.setValidators(status === 'Blacklisted' ? [Validators.required, Validators.minLength(10), Validators.maxLength(500)] : [Validators.maxLength(500)]);
      reason.updateValueAndValidity();
    });
  }

  blocked(): boolean {
    return this.form.controls.status.value === 'Inactive' && this.blockers().length > 0;
  }

  save(): void {
    const p = this.partner();
    if (!p || this.busy() || this.blocked()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.api.changeStatus(p.id, { status: v.status, reason: v.reason.trim() || null, effectiveDate: v.effectiveDate }).subscribe({
      next: () => {
        this.busy.set(false);
        this.notify.success(`${p.legalName} is now ${v.status}`);
        this.changed.emit();
      },
      error: (err) => {
        this.busy.set(false);
        const problems = apiErrors(err);
        if (problems.length === 0 || applyServerErrors(this.form, problems, this.messages).length > 0) this.notify.error(err);
      },
    });
  }
}
