import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { FuelCardsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { FuelCardModel } from '../../core/fuel-card.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { applyServerErrors } from '../../shared/server-errors';
import { VehiclePickerComponent } from '../../shared/vehicle-picker.component';

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

/** Assign or reassign a fuel card (FSD §28): "Reassignment closes the current row, opens a new one" — the same
 * dialog for both, since assigning when nothing is assigned and reassigning when something already is are the
 * same request either way. */
@Component({
  selector: 'app-fuel-card-assign-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, TextareaModule, FieldComponent, VehiclePickerComponent, PartnerPickerComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="!!card()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" [header]="card()?.vehicleId || card()?.driverId ? 'Reassign fuel card' : 'Assign fuel card'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        <vms-field label="Vehicle" [control]="form.controls.vehicleId" for="fca-vehicle"><vms-vehicle-picker formControlName="vehicleId" /></vms-field>
        <vms-field label="Driver" [control]="form.controls.driverId" for="fca-driver"><vms-partner-picker role="Driver" formControlName="driverId" inputId="fca-driver" /></vms-field>
        <vms-field label="Assigned from" [control]="form.controls.assignedFrom" for="fca-from" hint="Defaults to today."><vms-date-picker inputId="fca-from" formControlName="assignedFrom" /></vms-field>
        <vms-field label="Reason" [control]="form.controls.reason" for="fca-reason"><textarea pTextarea id="fca-reason" formControlName="reason" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button label="Assign" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class FuelCardAssignDialogComponent extends ActionDialog {
  private readonly api = inject(FuelCardsApi);
  private readonly fb = inject(FormBuilder);

  readonly card = input<FuelCardModel | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly form = this.fb.group({
    vehicleId: this.fb.control<number | null>(null),
    driverId: this.fb.control<number | null>(null),
    assignedFrom: this.fb.control<string | null>(null),
    reason: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      const c = this.card();
      if (!c) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset({ vehicleId: c.vehicleId ?? null, driverId: c.driverId ?? null, assignedFrom: null, reason: '' });
      });
    });
  }

  save(): void {
    const c = this.card();
    if (!c || this.busy()) return;
    const f = this.form.getRawValue();
    if (f.vehicleId === null && f.driverId === null) { this.problems.set(['Choose a vehicle or a driver.']); return; }
    this.run(this.api.assign(c.fuelCardId, { vehicleId: f.vehicleId, driverId: f.driverId, assignedFrom: f.assignedFrom, reason: f.reason.trim() || null, rowVersion: c.rowVersion }),
      this.form, 'Fuel card assigned', () => this.saved.emit());
  }
}
