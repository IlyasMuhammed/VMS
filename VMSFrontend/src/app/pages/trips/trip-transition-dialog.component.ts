import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { TripsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { TripModel } from '../../core/trip.models';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { needsEndOdometer, needsStartOdometer, words } from './trip-logic';

/** The normal "next status" move (FSD §24): most transitions need nothing beyond a confirm, but Started needs a
 * start odometer and Delivered needs an end odometer — the two the FSD's own screen table calls out by name. */
@Component({
  selector: 'app-trip-transition-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputNumberModule, FieldComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '420px' }" [header]="'Move to ' + words(toStatus())" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        @if (needsStart()) { <vms-field label="Start odometer (km)" [control]="form.controls.startOdometer" for="tr-start"><p-inputnumber inputId="tr-start" formControlName="startOdometer" [useGrouping]="false" [fluid]="true" /></vms-field> }
        @if (needsEnd()) { <vms-field label="End odometer (km)" [control]="form.controls.endOdometer" for="tr-end"><p-inputnumber inputId="tr-end" formControlName="endOdometer" [useGrouping]="false" [fluid]="true" /></vms-field> }
        @if (!needsStart() && !needsEnd()) { <p>Move this trip to {{ words(toStatus()) }}?</p> }
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Confirm" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; } .alert { margin-bottom: 1rem; }`],
})
export class TripTransitionDialogComponent {
  private readonly api = inject(TripsApi);
  private readonly fb = inject(FormBuilder);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly trip = input<TripModel | null>(null);
  readonly toStatus = input<string>('');
  readonly closed = output<void>();
  readonly changed = output<TripModel>();

  readonly open = computed(() => !!this.trip() && !!this.toStatus());
  readonly busy = signal(false);
  readonly problems = signal<string[]>([]);
  protected readonly needsStart = () => needsStartOdometer(this.toStatus());
  protected readonly needsEnd = () => needsEndOdometer(this.toStatus());
  protected readonly words = words;

  readonly form = this.fb.group({
    startOdometer: this.fb.control<number | null>(null),
    endOdometer: this.fb.control<number | null>(null),
  });

  constructor() {
    effect(() => {
      if (!this.open()) return;
      untracked(() => { this.problems.set([]); this.form.reset({ startOdometer: null, endOdometer: null }); });
    });
  }

  save(): void {
    const t = this.trip();
    if (!t || this.busy()) return;
    if (this.needsStart() && this.form.controls.startOdometer.value === null) { this.form.controls.startOdometer.setErrors({ required: true }); this.form.controls.startOdometer.markAsTouched(); return; }
    if (this.needsEnd() && this.form.controls.endOdometer.value === null) { this.form.controls.endOdometer.setErrors({ required: true }); this.form.controls.endOdometer.markAsTouched(); return; }

    this.busy.set(true);
    this.problems.set([]);
    const f = this.form.getRawValue();
    this.api.transition(t.tripId, this.toStatus(), { startOdometer: f.startOdometer, endOdometer: f.endOdometer, rowVersion: t.rowVersion }).subscribe({
      next: (r) => { this.busy.set(false); this.notify.success(`Trip moved to ${words(r.status)}`); this.changed.emit(r); },
      error: (err) => {
        this.busy.set(false);
        const found = apiErrors(err);
        if (found.length === 0) return this.notify.error(err);
        this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
      },
    });
  }
}
