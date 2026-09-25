import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { Observable } from 'rxjs';
import { TripConfigurationsApi } from '../../core/api.services';
import { apiErrors } from '../../core/api-error';
import { ApiFieldError } from '../../core/message-format';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { TripConfigurationVehicleModel } from '../../core/trip-configuration.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
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

/** Add or edit a trip configuration's allowed vehicle (FSD §19). Identity (which vehicle, and when it started)
 * is fixed once assigned — mirroring the server's own `UpdateTripConfigurationVehicleRequest`, which likewise
 * excludes `vehicleId`/`effectiveFrom`; a different vehicle or start date is a new assignment, not an edit. */
@Component({
  selector: 'app-trip-configuration-vehicle-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule, TextareaModule, FieldComponent, VehiclePickerComponent, DatePickerComponent],
  template: `
    <p-dialog [visible]="open()" (visibleChange)="!$event && closed.emit()" [modal]="true" [style]="{ width: '480px' }" [header]="assignment() ? 'Edit vehicle assignment' : 'Assign a vehicle'" [closable]="!busy()">
      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (p of problems(); track p) { <li>{{ p }}</li> }</ul></div> }
      <form [formGroup]="form" class="stack" novalidate>
        @if (!assignment()) {
          <vms-field label="Vehicle" [control]="form.controls.vehicleId" for="tcv-vehicle"><vms-vehicle-picker formControlName="vehicleId" /></vms-field>
          <vms-field label="Effective from" [control]="form.controls.effectiveFrom" for="tcv-from" hint="Defaults to today."><vms-date-picker inputId="tcv-from" formControlName="effectiveFrom" /></vms-field>
        }
        <vms-field label="Effective to" [control]="form.controls.effectiveTo" for="tcv-to" hint="Optional."><vms-date-picker inputId="tcv-to" formControlName="effectiveTo" /></vms-field>
        @if (assignment()) {
          <vms-field label="Status" [control]="form.controls.status" for="tcv-status"><p-select inputId="tcv-status" formControlName="status" [options]="statusOptions" optionLabel="label" optionValue="value" [fluid]="true" appendTo="body" /></vms-field>
        }
        <vms-field label="Remarks" [control]="form.controls.remarks" for="tcv-remarks"><textarea pTextarea id="tcv-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
      </form>
      <ng-template pTemplate="footer">
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="closed.emit()" [disabled]="busy()" />
        <p-button [label]="assignment() ? 'Save changes' : 'Assign vehicle'" icon="pi pi-check" [loading]="busy()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`ul { margin: .25rem 0; padding-left: 1.25rem; } .stack { display: flex; flex-direction: column; gap: 1rem; }`],
})
export class TripConfigurationVehicleDialogComponent extends ActionDialog {
  private readonly api = inject(TripConfigurationsApi);
  private readonly fb = inject(FormBuilder);

  readonly configurationId = input<number | null>(null);
  readonly assignment = input<TripConfigurationVehicleModel | null>(null);
  readonly adding = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly open = computed(() => this.adding() || !!this.assignment());
  protected readonly statusOptions = [{ label: 'Active', value: 'Active' }, { label: 'Inactive', value: 'Inactive' }];

  readonly form = this.fb.group({
    vehicleId: this.fb.control<number | null>(null, Validators.required),
    effectiveFrom: this.fb.control<string | null>(null),
    effectiveTo: this.fb.control<string | null>(null),
    status: this.fb.nonNullable.control<'Active' | 'Inactive'>('Active'),
    remarks: this.fb.nonNullable.control(''),
  });

  constructor() {
    super();
    effect(() => {
      const a = this.assignment();
      const adding = this.adding();
      if (!adding && !a) return;
      untracked(() => {
        this.problems.set([]);
        this.form.reset(a
          ? { vehicleId: a.vehicleId, effectiveFrom: a.effectiveFrom, effectiveTo: a.effectiveTo ?? null, status: a.status, remarks: a.remarks ?? '' }
          : { vehicleId: null, effectiveFrom: null, effectiveTo: null, status: 'Active', remarks: '' });
      });
    });
  }

  save(): void {
    if (this.busy() || this.invalid(this.form)) return;
    const cid = this.configurationId();
    const f = this.form.getRawValue();
    const a = this.assignment();
    const request = a
      ? this.api.updateVehicle(a.tripConfigurationVehicleId, { effectiveTo: f.effectiveTo, status: f.status, remarks: f.remarks.trim() || null })
      : cid !== null
        ? this.api.assignVehicle(cid, { vehicleId: f.vehicleId!, effectiveFrom: f.effectiveFrom, effectiveTo: f.effectiveTo, remarks: f.remarks.trim() || null })
        : null;
    if (!request) return;
    this.run(request, this.form, a ? 'Assignment updated' : 'Vehicle assigned', () => this.saved.emit());
  }
}
