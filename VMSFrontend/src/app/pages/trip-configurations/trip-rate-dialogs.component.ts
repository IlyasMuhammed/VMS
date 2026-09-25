import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { TripConfigurationsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { TripRateModel } from '../../core/trip-configuration.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';

abstract class ActionDialog {
  protected readonly notify = inject(NotifyService);
  protected readonly messages = inject(MessagesService);
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);

  protected run<T>(request: Observable<T>, form: AbstractControl, done: string, finished: (result: T) => void, onError?: (error: ApiFieldError) => boolean): void {
    this.busy.set(true);
    this.problems.set([]);
    request.subscribe({
      next: (result) => { this.busy.set(false); this.notify.success(done); finished(result); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        if (onError && found.length === 1 && onError(found[0])) return;
        this.problems.set(applyServerErrors(form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }

  protected invalid(form: AbstractControl): boolean {
    if (!form.invalid) return false;
    form.markAllAsTouched();
    return true;
  }
}

/** Add a rate, or edit one already there (FSD §26, screen 11). A start date that lands inside an existing
 * open-ended rate auto-closes it the day before — the server's own rule, surfaced here only as the "effective
 * from" hint; nothing pre-computes it client-side, since the exact cutoff depends on rows this screen may not
 * have refreshed. */
@Component({
  selector: 'app-trip-rate-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, TextareaModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" [header]="rate() ? 'Edit rate' : 'Add a rate'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Effective from" [control]="form.controls.effectiveFrom" for="tr-from" [hint]="rate() ? '' : 'Starting this date closes any open-ended rate the day before.'">
          <vms-date-picker inputId="tr-from" formControlName="effectiveFrom" />
        </vms-field>
        <vms-field label="Effective to" [control]="form.controls.effectiveTo" for="tr-to" hint="Blank = open-ended."><vms-date-picker inputId="tr-to" formControlName="effectiveTo" /></vms-field>
        <vms-field label="Rate amount" [control]="form.controls.rateAmount" for="tr-amount"><p-inputnumber inputId="tr-amount" formControlName="rateAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Remarks" [control]="form.controls.remarks" for="tr-remarks"><textarea pTextarea id="tr-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="rate() ? 'Save changes' : 'Add rate'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class TripRateDialogComponent extends ActionDialog {
  private readonly api = inject(TripConfigurationsApi);
  private readonly fb = inject(FormBuilder);

  readonly configurationId = input<number | null>(null);
  readonly rate = input<TripRateModel | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.rate());

  readonly form = this.fb.group({
    effectiveFrom: this.fb.control<string | null>(null, Validators.required),
    effectiveTo: this.fb.control<string | null>(null),
    rateAmount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      const r = this.rate();
      const adding = this.adding();
      if (!adding && !r) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(r
          ? { effectiveFrom: r.effectiveFrom, effectiveTo: r.effectiveTo ?? null, rateAmount: r.rateAmount, remarks: r.remarks ?? '' }
          : { effectiveFrom: null, effectiveTo: null, rateAmount: null, remarks: '' });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    const r = this.rate();
    if (r) {
      this.run(this.api.updateRate(r.tripRateId, { rateAmount: f.rateAmount, effectiveFrom: f.effectiveFrom, effectiveTo: f.effectiveTo, remarks: f.remarks.trim() || null, rowVersion: r.rowVersion }),
        this.form, 'Rate updated', () => this.saved.emit());
      return;
    }
    const cid = this.configurationId();
    if (cid === null) return;
    this.run(this.api.createRate(cid, { effectiveFrom: f.effectiveFrom!, effectiveTo: f.effectiveTo, rateAmount: f.rateAmount!, remarks: f.remarks.trim() || null }),
      this.form, 'Rate added', () => this.saved.emit());
  }
}

/** "Insert One-Day Rate" (FSD §26's own worked example): splits the rate covering one date into up to three
 * rows — before / that one day / after — for a single-day exception rate. */
@Component({
  selector: 'app-trip-rate-split-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, TextareaModule, FieldComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '460px' }" header="Insert a one-day rate" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Date" [control]="form.controls.date" for="trs-date" hint="Must fall inside an existing Active rate."><vms-date-picker inputId="trs-date" formControlName="date" /></vms-field>
        <vms-field label="Rate for that day" [control]="form.controls.rateAmount" for="trs-amount"><p-inputnumber inputId="trs-amount" formControlName="rateAmount" mode="decimal" [minFractionDigits]="2" [maxFractionDigits]="2" [fluid]="true" /></vms-field>
        <vms-field label="Remarks" [control]="form.controls.remarks" for="trs-remarks"><textarea pTextarea id="trs-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Insert" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class TripRateSplitDialogComponent extends ActionDialog {
  private readonly api = inject(TripConfigurationsApi);
  private readonly fb = inject(FormBuilder);

  readonly configurationId = input<number | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() && this.configurationId() !== null);

  readonly form = this.fb.group({
    date: this.fb.control<string | null>(null, Validators.required),
    rateAmount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      if (!this.adding()) return;
      untracked(() => { this.problems.set([]); this.form.reset({ date: null, rateAmount: null, remarks: '' }); });
    });
  }

  save(): void {
    const cid = this.configurationId();
    if (cid === null || this.busy() || this.invalid(this.form)) return;
    const f = this.form.getRawValue();
    this.run(this.api.splitRate(cid, { date: f.date!, rateAmount: f.rateAmount!, remarks: f.remarks.trim() || null }), this.form, 'Rate inserted', () => this.saved.emit());
  }
}
