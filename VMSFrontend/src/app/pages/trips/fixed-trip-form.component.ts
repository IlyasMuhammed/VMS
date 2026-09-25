import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { AuthService } from '../../core/auth.service';
import { apiErrors } from '../../core/api-error';
import { TripConfigurationsApi, TripsApi } from '../../core/api.services';
import { MessagesService } from '../../core/messages.service';
import { NotifyService } from '../../core/notify.service';
import { RateResolutionResult, TripConfigurationModel, TripConfigurationVehicleModel } from '../../core/trip-configuration.models';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { FieldComponent } from '../../shared/field.component';
import { applyServerErrors } from '../../shared/server-errors';
import { PartnerPickerComponent } from '../../shared/partner-picker.component';
import { TripCustomerPickerComponent } from '../../shared/trip-customer-picker.component';

/**
 * New Fixed Trip (FSD §21, §48.4 screen 12). The FSD's own wireframe is a stepper (Customer → Configuration →
 * Vehicle → Driver → Date → Rate panel); built here as one progressively-filled form instead of a true multi-step
 * component — the underlying request is a single flat object regardless, and each field's own options already
 * depend on the one before it (a configuration's own vehicle list, a rate resolved for the chosen date), which
 * reads the same to the person filling it in as a stepper would, without a second navigation mechanism to build
 * and keep in sync with the form's own validity.
 */
@Component({
  selector: 'app-fixed-trip-form',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, SelectModule, TextareaModule, FieldComponent, DatePickerComponent, TripCustomerPickerComponent, PartnerPickerComponent],
  template: `
    <div class="page">
      <div class="crumbs"><a routerLink="/trips">Trip Desk</a> <span aria-hidden="true">/</span> <span>New fixed trip</span></div>
      <h1>New fixed trip</h1>

      @if (problems().length > 0) { <div class="alert error" role="alert"><ul>@for (m of problems(); track m) { <li>{{ m }}</li> }</ul></div> }
      @for (w of warnings(); track w) { <div class="alert warning">{{ w }}</div> }

      <div class="card">
        <form [formGroup]="form" class="form-grid" novalidate>
          <vms-field label="Customer" [control]="form.controls.customerId" for="ft-customer"><vms-trip-customer-picker formControlName="customerId" /></vms-field>

          <vms-field label="Trip configuration" [control]="form.controls.tripConfigurationId" for="ft-config" [hint]="!form.controls.customerId.value ? 'Choose a customer first.' : ''">
            <p-select inputId="ft-config" formControlName="tripConfigurationId" [options]="configOptions()" optionLabel="label" optionValue="value" [disabled]="!form.controls.customerId.value" placeholder="Choose a configuration" [fluid]="true" appendTo="body" />
          </vms-field>

          <vms-field label="Vehicle" [control]="form.controls.vehicleId" for="ft-vehicle" [hint]="!form.controls.tripConfigurationId.value ? 'Choose a configuration first.' : ''">
            <p-select inputId="ft-vehicle" formControlName="vehicleId" [options]="vehicleOptions()" optionLabel="label" optionValue="value" [disabled]="!form.controls.tripConfigurationId.value" placeholder="Choose a vehicle" [fluid]="true" appendTo="body" />
          </vms-field>

          <vms-field label="Driver" [control]="form.controls.driverId" for="ft-driver" hint="Leave blank to use the vehicle's own default driver.">
            <vms-partner-picker role="Driver" [allowCreate]="false" formControlName="driverId" inputId="ft-driver" />
          </vms-field>
          @if (needsOverrideReason()) {
            <vms-field label="Override reason" [control]="form.controls.driverOverrideReason" for="ft-override" hint="Required — this driver differs from the vehicle's own default.">
              <input pInputText id="ft-override" formControlName="driverOverrideReason" />
            </vms-field>
          }

          <vms-field label="Trip date" [control]="form.controls.tripDate" for="ft-date"><vms-date-picker inputId="ft-date" formControlName="tripDate" /></vms-field>
          <vms-field label="Customer reference" [control]="form.controls.customerTripReference" for="ft-ref"><input pInputText id="ft-ref" formControlName="customerTripReference" /></vms-field>
          <vms-field class="full" label="Remarks" [control]="form.controls.remarks" for="ft-remarks"><textarea pTextarea id="ft-remarks" formControlName="remarks" rows="2" [fluid]="true"></textarea></vms-field>
        </form>

        <div class="rate-panel">
          <h2>Rate</h2>
          @if (!form.controls.tripConfigurationId.value || !form.controls.tripDate.value) {
            <p class="muted">Choose a configuration and a trip date to resolve the rate.</p>
          } @else if (resolving()) {
            <p class="muted">Resolving…</p>
          } @else if (rate()?.found) {
            <p class="rate-ok">{{ rate()!.rateAmount }} {{ rate()!.currencyCode }} <span class="muted">({{ rate()!.effectiveFrom }}@if (rate()!.effectiveTo) { – {{ rate()!.effectiveTo }} } @else { , open-ended })</span></p>
          } @else {
            <p class="rate-missing">Rate not configured for this date. <a [routerLink]="['/trip-configurations', form.controls.tripConfigurationId.value]">View rates</a></p>
          }
        </div>

        <div class="savebar">
          <span class="buttons">
            <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="cancel()" />
            <p-button label="Save Draft" severity="secondary" [outlined]="true" [loading]="busy()" (onClick)="save(null)" />
            <p-button label="Save & Plan" severity="secondary" [outlined]="true" [loading]="busy()" (onClick)="save('Planned')" />
            <p-button label="Save & Assign" icon="pi pi-check" [loading]="busy()" (onClick)="save('Assigned')" />
          </span>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .crumbs { color: var(--vms-muted); margin-bottom: .75rem; font-size: .9rem; } h1 { font-size: 1.5rem; font-weight: 600; margin-bottom: 1rem; }
      .form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: 1rem; } .full { grid-column: 1 / -1; }
      .rate-panel { margin-top: 1.5rem; padding-top: 1rem; border-top: 1px solid var(--vms-border); } .rate-panel h2 { font-size: 1.05rem; margin-bottom: .5rem; }
      .rate-ok { font-size: 1.1rem; font-weight: 600; color: var(--vms-success-text); }
      .rate-missing { color: var(--vms-danger-text); font-weight: 600; }
      .savebar { margin-top: 1.5rem; display: flex; justify-content: flex-end; } .buttons { display: inline-flex; gap: .5rem; }
      .alert { margin-bottom: 1rem; }
    `,
  ],
})
export class FixedTripFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(TripsApi);
  private readonly configsApi = inject(TripConfigurationsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly messages = inject(MessagesService);

  readonly problems = signal<string[]>([]);
  readonly warnings = signal<string[]>([]);
  readonly busy = signal(false);
  readonly resolving = signal(false);
  readonly configs = signal<TripConfigurationModel[]>([]);
  readonly vehicles = signal<TripConfigurationVehicleModel[]>([]);
  readonly rate = signal<RateResolutionResult | null>(null);
  protected readonly canOverride = computed(() => this.auth.hasPermission('TRP.TRIP.OVERRIDEDRIVER'));

  readonly form = this.fb.group({
    customerId: this.fb.control<number | null>(null, Validators.required),
    tripConfigurationId: this.fb.control<number | null>(null, Validators.required),
    vehicleId: this.fb.control<number | null>(null, Validators.required),
    driverId: this.fb.control<number | null>(null),
    driverOverrideReason: this.fb.nonNullable.control(''),
    tripDate: this.fb.control<string | null>(null, Validators.required),
    customerTripReference: this.fb.nonNullable.control(''),
    remarks: this.fb.nonNullable.control(''),
  });

  protected readonly configOptions = computed(() => this.configs().map((c) => ({ label: `${c.tripCode} — ${c.name}`, value: c.tripConfigurationId })));
  protected readonly vehicleOptions = computed(() => this.vehicles().filter((v) => v.status === 'Active').map((v) => ({ label: v.registrationNo || `#${v.vehicleId}`, value: v.vehicleId })));
  protected readonly needsOverrideReason = () => !!this.form.controls.driverId.value;

  constructor() {
    effect(() => {
      const customerId = this.form.controls.customerId.value;
      untracked(() => {
        this.form.controls.tripConfigurationId.setValue(null);
        this.configs.set([]);
        if (customerId) this.configsApi.list(customerId, false).subscribe({ next: (rows) => this.configs.set(rows), error: () => undefined });
      });
    });
    this.form.controls.customerId.valueChanges.subscribe(() => this.form.controls.tripConfigurationId.updateValueAndValidity());

    let lastConfigId: number | null = null;
    this.form.controls.tripConfigurationId.valueChanges.subscribe((id) => {
      if (id === lastConfigId) return;
      lastConfigId = id;
      this.form.controls.vehicleId.setValue(null);
      this.vehicles.set([]);
      this.rate.set(null);
      if (id) this.configsApi.vehicles(id).subscribe({ next: (rows) => this.vehicles.set(rows), error: () => undefined });
      this.resolveRate();
    });
    this.form.controls.tripDate.valueChanges.subscribe(() => this.resolveRate());
  }

  private resolveRate(): void {
    const customerId = this.form.controls.customerId.value;
    const configId = this.form.controls.tripConfigurationId.value;
    const date = this.form.controls.tripDate.value;
    if (!customerId || !configId || !date) { this.rate.set(null); return; }
    this.resolving.set(true);
    this.configsApi.resolveRate(customerId, configId, date).subscribe({
      next: (r) => { this.resolving.set(false); this.rate.set(r); },
      error: () => { this.resolving.set(false); this.rate.set(null); },
    });
  }

  cancel(): void {
    void this.router.navigate(['/trips']);
  }

  /** `andThen`: null = Save Draft; 'Planned' = Save & Plan; 'Assigned' = Save & Assign (chains Draft→Planned→Assigned,
   * since there is no skip from Draft straight to Assigned in the normal transition table). */
  save(andThen: 'Planned' | 'Assigned' | null): void {
    if (this.busy() || this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.problems.set([]);
    this.warnings.set([]);
    const f = this.form.getRawValue();
    this.busy.set(true);
    this.api.createFixed({
      customerId: f.customerId!, tripConfigurationId: f.tripConfigurationId!, vehicleId: f.vehicleId!, driverId: f.driverId,
      driverOverrideReason: f.driverOverrideReason.trim() || null, tripDate: f.tripDate!, customerTripReference: f.customerTripReference.trim() || null, remarks: f.remarks.trim() || null,
    }).subscribe({
      next: (trip) => {
        this.warnings.set(trip.warnings);
        if (!andThen) { this.busy.set(false); this.finish(trip.tripId); return; }
        this.advance(trip.tripId, trip.rowVersion, andThen);
      },
      error: (err) => this.refused(err),
    });
  }

  private advance(tripId: number, rowVersion: string, target: 'Planned' | 'Assigned'): void {
    this.api.transition(tripId, 'Planned', { rowVersion }).subscribe({
      next: (planned) => {
        if (target === 'Planned') { this.busy.set(false); this.finish(tripId); return; }
        this.api.transition(tripId, 'Assigned', { rowVersion: planned.rowVersion }).subscribe({
          next: () => { this.busy.set(false); this.finish(tripId); },
          error: () => { this.busy.set(false); this.notify.warn('Trip saved, but could not be moved to Assigned. Continue from Trip Details.', 'Partly done'); this.finish(tripId); },
        });
      },
      error: () => { this.busy.set(false); this.notify.warn('Trip saved, but could not be moved to Planned. Continue from Trip Details.', 'Partly done'); this.finish(tripId); },
    });
  }

  private finish(tripId: number): void {
    this.notify.success('Trip saved');
    void this.router.navigate(['/trips', tripId]);
  }

  private refused(err: unknown): void {
    this.busy.set(false);
    const found = apiErrors(err);
    if (found.length === 0) { this.notify.error(err); return; }
    this.problems.set(applyServerErrors(this.form, found, this.messages).map((e) => this.messages.describe(e)));
  }
}
